using Azure;
using AcsEmail = Azure.Communication.Email;
using StockManager.Notifications;

namespace StockApi.Email
{
    /// <summary>
    /// Delivers mail through Azure Communication Services, signed in as the app's own identity.
    /// </summary>
    /// <remarks>
    /// T7's transport, chosen by <c>Email:Transport = Acs</c> with <c>Email:AcsEndpoint</c>. The
    /// credential is the app's managed identity, so no mail password exists to store or leak.
    /// One ACS resource sends for every store: each store's domain is verified on it (SPF, DKIM,
    /// DMARC — a runbook step per tenant) and <c>Site.MailFromAddress</c> names the sender.
    ///
    /// Waits only until ACS has accepted the message, not until it is delivered. Delivery takes
    /// seconds to minutes, and holding a dispatcher batch open for it would hold every other
    /// store's mail behind one slow mailbox. A bounce after acceptance is not seen here; that
    /// needs ACS's delivery events, which are not built.
    /// </remarks>
    public sealed class AcsEmailSender : IEmailSender
    {
        private readonly AcsEmail.EmailClient _client;

        public AcsEmailSender(AcsEmail.EmailClient client)
        {
            _client = client;
        }

        public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(message.From))
            {
                throw new PermanentEmailFailureException(
                    $"Site {message.SiteKey} has no MailFromAddress, so its mail cannot be sent. Set " +
                    "it to an address on a domain verified for this store with the mail provider.");
            }

            var outgoing = new AcsEmail.EmailMessage(
                message.From,
                new AcsEmail.EmailRecipients([new AcsEmail.EmailAddress(message.To, message.ToName)]),
                new AcsEmail.EmailContent(message.Subject) { PlainText = message.Body });

            try
            {
                await _client.SendAsync(WaitUntil.Started, outgoing, cancellationToken);
            }
            catch (RequestFailedException exception) when (exception.Status is >= 400 and < 500 and not 408 and not 429)
            {
                // The message itself was refused — an unverified sender domain, a malformed
                // address. Throttling and timeouts are the exceptions, and are retried.
                throw new PermanentEmailFailureException(
                    $"The mail provider refused the message ({exception.Status} {exception.ErrorCode}).", exception);
            }
        }
    }
}
