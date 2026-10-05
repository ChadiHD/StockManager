using System.Security.Cryptography;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Email;
using SMDataManager.Library.Models;
using StockManager.Notifications;

namespace StockApi.Email
{
    /// <summary>
    /// Sends one batch of queued mail: claim, render, hand to the transport, record.
    /// </summary>
    /// <remarks>
    /// Rendering happens here, at send time, and not when the message was queued. That is
    /// what lets a corrected template reach the messages still waiting, and it is why the
    /// storefront can queue an acknowledgement that <c>StockApi</c> writes out — the wording
    /// lives in <see cref="EmailTemplates"/>, which both hosts reference.
    ///
    /// Every failure is caught per message. One unrenderable row must not stand between the
    /// rest of the batch and the transport, and a failure is a row with a status and a next
    /// attempt rather than an exception that ends the loop.
    /// </remarks>
    public class EmailDispatcher
    {
        public const int BatchSize = 20;

        /// <summary>
        /// How long a claim holds a row. Far longer than one send; short enough that a message
        /// a dead host was holding goes out within minutes rather than after the next deploy.
        /// </summary>
        public const int LeaseMinutes = 5;

        private readonly IEmailOutboxData _outbox;
        private readonly IEmailOutbox _queue;
        private readonly ISiteData _sites;
        private readonly ISiteEmailTemplateData _wording;
        private readonly IEmailSender _transport;
        private readonly OutboxPayloadProtector _protector;
        private readonly ILogger<EmailDispatcher> _logger;

        public EmailDispatcher(
            IEmailOutboxData outbox,
            IEmailOutbox queue,
            ISiteData sites,
            ISiteEmailTemplateData wording,
            IEmailSender transport,
            OutboxPayloadProtector protector,
            ILogger<EmailDispatcher> logger)
        {
            _outbox = outbox;
            _queue = queue;
            _sites = sites;
            _wording = wording;
            _transport = transport;
            _protector = protector;
            _logger = logger;
        }

        /// <summary>Sends what is due. Returns how many rows were claimed.</summary>
        public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken = default)
        {
            var claim = Guid.NewGuid();
            var claimed = _outbox.Claim(claim, BatchSize, LeaseMinutes);

            if (claimed.Count == 0)
            {
                return 0;
            }

            var sites = _sites.GetSites().ToDictionary(site => site.Id);
            var givenUp = new List<UndeliverableRow>();
            _wordingThisBatch.Clear();

            foreach (var row in claimed)
            {
                // The rows not reached stay claimed until their lease runs out, then go. A
                // shutdown is not the moment to start a send that may not finish.
                if (cancellationToken.IsCancellationRequested) break;

                if (await DeliverAsync(row, sites, cancellationToken) is { } undeliverable)
                {
                    givenUp.Add(undeliverable);
                }
            }

            AlertOperators(givenUp, sites);

            return claimed.Count;
        }

        private sealed record UndeliverableRow(EmailOutboxModel Row, string Error);

        // Per batch: a backlog of one store's confirmations reads its wording once, not twenty
        // times, and a row edited mid-batch reaches the next batch.
        private readonly Dictionary<(int SiteId, string Key), SiteEmailTemplateModel?> _wordingThisBatch = [];

        private SiteEmailTemplateModel? Wording(int siteId, string key)
        {
            if (!_wordingThisBatch.TryGetValue((siteId, key), out var wording))
            {
                wording = _wording.Get(siteId, key);
                _wordingThisBatch[(siteId, key)] = wording;
            }

            return wording;
        }

        /// <summary>One message. Returns it when it has just been given up on.</summary>
        private async Task<UndeliverableRow?> DeliverAsync(
            EmailOutboxModel row, Dictionary<int, SiteModel> sites, CancellationToken cancellationToken)
        {
            /*
            Claimed more often than it is allowed attempts: every previous claim ended with the
            host dying mid-send, because a send that failed normally would have recorded it.
            Sending it again is how the host dies again.
            */
            if (row.Attempts > OutboxRetryPolicy.MaxAttempts)
            {
                return GiveUp(row,
                    "Abandoned mid-send on every attempt; the host stopped while sending it each time.");
            }

            string payload;

            try
            {
                payload = row.PayloadProtected ? _protector.Unprotect(row.PayloadJson) : row.PayloadJson;
            }
            catch (CryptographicException)
            {
                // Permanent, so not retried: the key that wrote it has left the ring, and two
                // hours of backoff will not bring it back. The customer asks for another link.
                return GiveUp(row,
                    "The payload was protected with a Data Protection key that is no longer in " +
                    "the key ring, so it cannot be read. The customer needs to request it again.");
            }

            try
            {
                if (!sites.TryGetValue(row.SiteId, out var site))
                {
                    throw new InvalidOperationException($"Site {row.SiteId} could not be read.");
                }

                var template = EmailTemplates.Find(row.TemplateKey)
                    ?? throw new InvalidOperationException(
                        $"No template is registered as '{row.TemplateKey}'. A host older than " +
                        "the row may be dispatching; it is retried in case a newer one arrives.");

                var rendered = EmailRenderer.Render(template, site, payload, Wording(site.Id, template.Key));

                if (rendered.WordingRefused is { } reason)
                {
                    // Sent anyway, in the platform's words: a store's typo is not a reason to
                    // leave its customer untold. The warning is how somebody fixes the row.
                    _logger.LogWarning(
                        "Site {SiteKey}'s own wording for {TemplateKey} was not used: {Reason}.",
                        site.SiteKey, template.Key, reason);
                }

                await _transport.SendAsync(
                    new EmailMessage(site.SiteKey, row.ToAddress, row.ToName, rendered.Subject, rendered.Body,
                        From: site.MailFromAddress),
                    cancellationToken);

                if (!_outbox.RecordSent(row.Id, row.ClaimToken))
                {
                    // The lease ran out mid-send and another dispatcher took the row. It may
                    // send it again: delivery is at least once, and this is the "more than".
                    _logger.LogWarning(
                        "Outbox message {OutboxId} was sent, but its claim had already been " +
                        "taken over, so it may be sent twice.", row.Id);
                }

                return null;
            }
            catch (PermanentEmailFailureException exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A store with no sender address, or a message the provider refused outright.
                // Eight attempts over eight hours would end the same way, later.
                return GiveUp(row, exception.Message);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                return Fail(row, exception.Message);
            }
        }

        private UndeliverableRow? Fail(EmailOutboxModel row, string error)
        {
            var retryAfter = OutboxRetryPolicy.After(row.Attempts);

            if (retryAfter is null)
            {
                return GiveUp(row, error);
            }

            _outbox.RecordFailure(row.Id, row.ClaimToken, error, retryAfter);

            _logger.LogWarning(
                "Outbox message {OutboxId} ({TemplateKey}) failed on attempt {Attempts}; " +
                "retrying in {RetryAfter}: {Error}",
                row.Id, row.TemplateKey, row.Attempts, retryAfter, error);

            return null;
        }

        private UndeliverableRow GiveUp(EmailOutboxModel row, string error)
        {
            _outbox.RecordFailure(row.Id, row.ClaimToken, error, retryAfter: null);

            _logger.LogError(
                "Outbox message {OutboxId} ({TemplateKey}) to site {SiteId} was dead-lettered " +
                "after {Attempts} attempt(s): {Error}",
                row.Id, row.TemplateKey, row.SiteId, row.Attempts, error);

            return new UndeliverableRow(row, error);
        }

        /// <summary>
        /// Tells each store's operator what was given up on, in one message per store.
        /// </summary>
        /// <remarks>
        /// Through the outbox like anything else, so the alert is itself retried. Operator mail
        /// is left out of it: an undeliverable alert would only produce an alert to the same
        /// address, and that is the wheel this stops after one turn. The Error above is the
        /// whole record of those.
        ///
        /// No platform-wide fallback when a store has no <c>OperatorEmail</c>, for the reason
        /// <c>FeedAlertService</c> has none: the message names this store's customers.
        /// </remarks>
        private void AlertOperators(List<UndeliverableRow> givenUp, Dictionary<int, SiteModel> sites)
        {
            var customerMail = givenUp
                .Where(item => EmailTemplates.Find(item.Row.TemplateKey)?.Audience != EmailAudience.Operator)
                .GroupBy(item => item.Row.SiteId);

            foreach (var group in customerMail)
            {
                if (!sites.TryGetValue(group.Key, out var site))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(site.OperatorEmail))
                {
                    _logger.LogWarning(
                        "Site {SiteKey}: {Count} message(s) were dead-lettered, and no " +
                        "OperatorEmail is set, so nobody was told.", site.SiteKey, group.Count());
                    continue;
                }

                try
                {
                    _queue.Enqueue(site.Id, site.OperatorEmail, null, EmailTemplates.MailUndeliverable,
                        new MailUndeliverablePayload(group
                            .Select(item => new UndeliverableMessage(
                                item.Row.Id, item.Row.TemplateKey, item.Row.ToAddress,
                                item.Row.Attempts, item.Error))
                            .ToList()));
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception,
                        "Could not queue the undeliverable-mail alert for site {SiteKey}.", site.SiteKey);
                }
            }
        }
    }
}
