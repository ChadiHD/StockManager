using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SMDataManager.Library.Models;

namespace SMDataManager.Library.Email
{
    /// <summary>
    /// Every message the platform sends, in its platform wording.
    /// </summary>
    /// <remarks>
    /// One registry rather than a copy class per host, because the host that queues a message
    /// is no longer the host that renders it: the storefront queues an acknowledgement and
    /// <c>StockApi</c>'s dispatcher writes it out. Before T6 the wording lived beside each call
    /// site — <c>RegistrationEmails</c>, <c>AccountDecisionEmails</c> — and those files said
    /// that this is what would replace them.
    ///
    /// Nothing in here names a store. The store's name, its domain and its signature are
    /// placeholders, so a second tenant is a <c>Site</c> row rather than a second set of copy.
    /// </remarks>
    public static class EmailTemplates
    {
        /// <summary>
        /// Acknowledges a trade application, and carries the link that confirms the address.
        /// </summary>
        /// <remarks>
        /// Says nothing about whether the address was already registered, because
        /// <c>RegistrationService</c> answers a duplicate exactly as it answers a new
        /// application and this message must not be the thing that gives the game away. The
        /// wording therefore has to work with no reference and no link — "we have sent you a
        /// link" over a message carrying none would tell the reader their address is the one
        /// that already has an account.
        ///
        /// Nothing here reveals anything the recipient did not type. It goes to the address on
        /// the application, so quoting account state would be quoting it to whoever typed the
        /// address.
        /// </remarks>
        public static readonly EmailTemplate<RegistrationReceivedPayload> RegistrationReceived = new(
            "registration.received",
            EmailAudience.Customer,
            "We have your application — {SiteName}",
            """
            Thanks for applying for a trade account with {SiteName}.

            {ReferenceLine}

            {ConfirmationParagraph}

            Someone will check the details and the documents you sent, and email you when the
            account is open. That usually takes a working day or two.

            If you do not hear from us, reply to this message and we will find it.

            — {SiteName}
            """,
            (_, payload) => new Dictionary<string, string>
            {
                ["ReferenceLine"] = string.IsNullOrWhiteSpace(payload.Reference)
                    ? string.Empty
                    : $"Your reference is {payload.Reference}.",
                ["ConfirmationParagraph"] = string.IsNullOrWhiteSpace(payload.ConfirmationLink)
                    ? string.Empty
                    : $"""
                       First, confirm this is your address by opening this link:

                       {payload.ConfirmationLink}

                       You will not be able to sign in until you have.
                       """,
            },
            carriesCredential: true,
            // Without it an applicant can never sign in, and nothing on the page they were
            // shown says so.
            "ConfirmationParagraph");

        public static readonly EmailTemplate<AccountApprovedPayload> AccountApproved = new(
            "account.approved",
            EmailAudience.Customer,
            "Your trade account is open — {SiteName}",
            """
            Your application for {Company} has been approved.

            You can sign in at {SignInLink} with the email address you applied with. Prices you
            see once signed in are your account's own.

            — {SiteName}
            """,
            (site, payload) => new Dictionary<string, string>
            {
                ["Company"] = payload.Company ?? string.Empty,
                ["SignInLink"] = $"https://{site.Domain}/login",
            });

        /// <summary>Tells an applicant they were turned down, and why.</summary>
        /// <remarks>
        /// The reason is quoted verbatim, which is why <c>spAccount_Reject</c> insists on one:
        /// this message is the whole of what the applicant learns, and "your application was
        /// unsuccessful" with no reason is what generates the phone call that reverses the
        /// decision. It is also why a store's own wording may not leave it out.
        /// </remarks>
        public static readonly EmailTemplate<AccountRejectedPayload> AccountRejected = new(
            "account.rejected",
            EmailAudience.Customer,
            "About your trade account application — {SiteName}",
            """
            We are not able to open a trade account for {Company} at the moment.

            {Reason}

            If that looks wrong, or something has changed, reply to this message and we will
            take another look.

            — {SiteName}
            """,
            (_, payload) => new Dictionary<string, string>
            {
                ["Company"] = payload.Company ?? string.Empty,
                ["Reason"] = payload.Reason ?? string.Empty,
            },
            carriesCredential: false,
            "Reason");

        /// <summary>A fresh confirmation link, for an applicant who asked for one.</summary>
        /// <remarks>
        /// Not an acknowledgement: this person applied some time ago and is stuck. It says
        /// nothing about the state of the application, and nothing that would differ depending
        /// on whether the account exists — the endpoint that queues it answers identically
        /// either way.
        /// </remarks>
        public static readonly EmailTemplate<ConfirmationReminderPayload> ConfirmationReminder = new(
            "registration.confirmation-reminder",
            EmailAudience.Customer,
            "Confirm your email address — {SiteName}",
            """
            Somebody asked us to resend this, so here it is. Open the link to confirm this is
            your address:

            {ConfirmationLink}

            If that was not you, nothing has happened and you can ignore this message.

            — {SiteName}
            """,
            (_, payload) => new Dictionary<string, string>
            {
                ["ConfirmationLink"] = payload.ConfirmationLink ?? string.Empty,
            },
            carriesCredential: true,
            "ConfirmationLink");

        /// <summary>A reset link, to an address whose owner has confirmed it.</summary>
        /// <remarks>
        /// Requested by anyone who can type an address, so what it says must be safe to send to
        /// a stranger who guessed it: no company, no account status, nothing but the link and
        /// how to ignore it.
        /// </remarks>
        public static readonly EmailTemplate<PasswordResetPayload> PasswordReset = new(
            "password.reset",
            EmailAudience.Customer,
            "Reset your password — {SiteName}",
            """
            Somebody asked to reset the password for this address at {SiteName}. Open the
            link to choose a new one:

            {ResetLink}

            The link expires, and it stops working once it has been used.

            If that was not you, nothing has happened and you can ignore this message. Your
            current password still works.

            — {SiteName}
            """,
            (_, payload) => new Dictionary<string, string>
            {
                ["ResetLink"] = payload.ResetLink ?? string.Empty,
            },
            carriesCredential: true,
            "ResetLink");

        /// <summary>Sent after a password actually changes.</summary>
        /// <remarks>
        /// The one message in the reset pair a customer needs and did not ask for. Somebody
        /// whose mailbox has been taken over gets no reset mail — the attacker deletes it — but
        /// this arrives after the fact and is the only signal the owner gets.
        ///
        /// It says other browsers were signed out, which is true only while
        /// <c>CustomerSessionValidator</c> compares the security stamp. Remove that and this
        /// lies.
        /// </remarks>
        public static readonly EmailTemplate<PasswordChangedPayload> PasswordChanged = new(
            "password.changed",
            EmailAudience.Customer,
            "Your password was changed — {SiteName}",
            """
            The password for this address at {SiteName} has just been changed, and any
            browsers that were signed in have been signed out.

            If that was you, there is nothing to do.

            If it was not, contact us straight away — whoever changed it can sign in.

            — {SiteName}
            """,
            (_, _) => new Dictionary<string, string>());

        /// <summary>Acknowledges a quote request, to the buyer who sent it.</summary>
        public static readonly EmailTemplate<QuoteReceivedPayload> QuoteReceived = new(
            "quote.received",
            EmailAudience.Customer,
            "We have your quote request {Reference} — {SiteName}",
            """
            Thank you. We have your quote request {Reference}, for {Lines} product(s).

            Somebody will price it and email you when it is ready for you to accept. You can see
            it, and anything you wrote with it, here once you have signed in:

            {QuoteLink}

            — {SiteName}
            """,
            (site, payload) => new Dictionary<string, string>
            {
                ["Reference"] = payload.Reference ?? string.Empty,
                ["Lines"] = payload.Lines.ToString(),
                ["QuoteLink"] = AccountLink(site, "quotes", payload.Reference),
            },
            carriesCredential: false,
            "Reference");

        /// <summary>A priced quote, which the customer can now accept.</summary>
        /// <remarks>
        /// The value is net, and says so. Tax turns on the customer at the moment the order is
        /// raised — their country and VAT number against the store's — so a quote asserting a
        /// tax figure would be asserting something nobody has decided. The quote page says the
        /// same thing.
        /// </remarks>
        public static readonly EmailTemplate<QuotePricedPayload> QuotePriced = new(
            "quote.priced",
            EmailAudience.Customer,
            "Your quote {Reference} is ready — {SiteName}",
            """
            Your quote {Reference} has been priced at {Value} before tax.

            {ExpiryLine}

            Tax is worked out when you accept it, from the details on your account. Review the
            quote, and accept or decline it, here once you have signed in:

            {QuoteLink}

            — {SiteName}
            """,
            (site, payload) => new Dictionary<string, string>
            {
                ["Reference"] = payload.Reference ?? string.Empty,
                ["Value"] = SiteMoney.Format(site, payload.Value),
                ["ExpiryLine"] = payload.ExpiresDate is { } expires
                    ? $"It is valid until {Date(expires)}."
                    : string.Empty,
                ["QuoteLink"] = AccountLink(site, "quotes", payload.Reference),
            },
            carriesCredential: false,
            "QuoteLink");

        /// <summary>
        /// An order raised from a quote, whether the customer accepted it or staff converted it.
        /// </summary>
        /// <remarks>
        /// **It links to the document rather than attaching it**, which is the decision T6 took
        /// on the renderer T5 deferred. The route exists, it carries the account predicate, and
        /// a link needs no PDF library and no browser in the deployment.
        ///
        /// The figures are the ones the order stored, and the tax line is labelled with the
        /// treatment rather than "VAT", for the reason the order page gives: a reverse-charged
        /// order carries nothing, and nothing with no stated reason is what an accountant sends
        /// back. The legend is required of a store's own wording for the same reason.
        /// </remarks>
        public static readonly EmailTemplate<OrderConfirmedPayload> OrderConfirmed = new(
            "order.confirmed",
            EmailAudience.Customer,
            "Order {Reference} is confirmed — {SiteName}",
            """
            Thank you. Order {Reference} is confirmed.

            {QuoteLine}

            {PoLine}

            Net: {SubTotal}
            {TaxLabel}: {Tax}
            Total: {Total}

            {TaxLegend}

            {DueLine}

            The order document, to print or save, is here once you have signed in:

            {DocumentLink}

            — {SiteName}
            """,
            (site, payload) => new Dictionary<string, string>
            {
                ["Reference"] = payload.Reference ?? string.Empty,
                ["QuoteLine"] = string.IsNullOrWhiteSpace(payload.QuoteReference)
                    ? string.Empty
                    : $"Raised from quote {payload.QuoteReference}.",
                ["PoLine"] = string.IsNullOrWhiteSpace(payload.PoNumber)
                    ? string.Empty
                    : $"Your purchase-order number: {payload.PoNumber}.",
                ["SubTotal"] = SiteMoney.Format(site, payload.SubTotal),
                ["TaxLabel"] = string.IsNullOrWhiteSpace(payload.TaxTreatment) ? "Tax" : payload.TaxTreatment,
                ["Tax"] = SiteMoney.Format(site, payload.Tax),
                ["Total"] = SiteMoney.Format(site, payload.Total),
                ["TaxLegend"] = payload.TaxLegend ?? string.Empty,
                ["DueLine"] = payload.DueDate is { } due
                    ? $"Payment is due by {Date(due)}."
                    : string.Empty,
                ["DocumentLink"] = AccountLink(site, "orders", payload.Reference, "/print"),
            },
            carriesCredential: false,
            "DocumentLink", "TaxLegend");

        /// <summary>A scheduled sync that failed, and had not failed the time before.</summary>
        /// <remarks>
        /// Sent on the transition into failure, not on every failing run — see
        /// <c>FeedAlertService</c>. It points at the portal rather than carrying the diagnosis:
        /// a mail that tried to be the diagnosis would be the one copy of it, in the one place
        /// nobody can search.
        /// </remarks>
        public static readonly EmailTemplate<FeedFailedPayload> FeedFailed = new(
            "operator.feed-failed",
            EmailAudience.Operator,
            "Distributor feed failed: {FeedName} — {SiteName}",
            """
            Tonight's scheduled sync of the {FeedName} feed did not complete.

            {Message}

            Stock quantities and costs from this distributor are as old as the last
            successful sync. Nothing has been delisted — a failed sync changes no product
            rows at all — but the catalog is not being updated.

            The feeds page in the admin portal has the full history of attempts, including
            how far this one got before it stopped.

            You will not get another message about this feed until it succeeds and then
            fails again.

            — {SiteName}
            """,
            (_, payload) => new Dictionary<string, string>
            {
                ["FeedName"] = payload.FeedName ?? string.Empty,
                ["Message"] = string.IsNullOrWhiteSpace(payload.Message)
                    ? "No detail was recorded."
                    : payload.Message,
            });

        /// <summary>
        /// Feeds that have not delivered inside the store's threshold, whether or not anything
        /// failed — one message for all of them, because staleness is usually caused by
        /// something upstream of any single feed.
        /// </summary>
        /// <remarks>
        /// What staleness is doing to the storefront depends on a setting, and is worth the
        /// branch: "your products have disappeared" and "you are selling on figures nobody has
        /// confirmed" call for different urgency.
        /// </remarks>
        public static readonly EmailTemplate<FeedsStalePayload> FeedsStale = new(
            "operator.feeds-stale",
            EmailAudience.Operator,
            "Distributor data is going stale — {SiteName}",
            """
            These feeds have not delivered anything for longer than {SiteName} allows
            ({StaleAfterHours} hours):

            {FeedList}

            {Consequence}

            Nothing has failed, which is what makes this worth saying: if a sync had
            failed you would have had a message about that instead. A feed that is simply
            not being attempted usually means the scheduled sync is off or the host that
            runs it has been down.

            — {SiteName}
            """,
            (site, payload) => new Dictionary<string, string>
            {
                ["StaleAfterHours"] = site.FeedStaleAfterHours.ToString(),
                ["FeedList"] = string.Join("\n",
                    (payload.FeedNames ?? Array.Empty<string>()).Select(name => $"  - {name}")),
                ["Consequence"] = site.HideStaleProducts
                    ? "Those distributors' products are currently hidden from the storefront, "
                      + "because this store is set to hide stale stock."
                    : "Those distributors' products are still on sale, at the quantities and "
                      + "costs from the last successful sync, because this store is not set to "
                      + "hide stale stock.",
            },
            carriesCredential: false,
            "FeedList");

        /// <summary>
        /// Customer mail the dispatcher has given up on, one message per store per batch.
        /// </summary>
        /// <remarks>
        /// Operator audience, so if this itself cannot be delivered it is logged at Error and
        /// nothing further is queued — the alert would be going to the address that just
        /// refused it. One turn of that wheel is enough.
        ///
        /// It names the template and the recipient and quotes the transport's error, and never
        /// the message: a payload may have held a sign-in link, and this goes to a mailbox.
        /// </remarks>
        public static readonly EmailTemplate<MailUndeliverablePayload> MailUndeliverable = new(
            "operator.mail-undeliverable",
            EmailAudience.Operator,
            "Mail could not be delivered — {SiteName}",
            """
            {Count} message(s) from {SiteName} could not be delivered, and nothing more will be
            attempted for them:

            {Messages}

            Each is marked DeadLettered in the outbox with its last error in full. A customer
            who was owed a link can ask for another one. Anything else — an approval, an order
            confirmation — is worth telling them some other way.

            You will not be told like this about operator mail, including this message: it
            would be going to the address that just refused it.

            — {SiteName}
            """,
            (_, payload) =>
            {
                var messages = payload.Messages ?? Array.Empty<UndeliverableMessage>();

                return new Dictionary<string, string>
                {
                    ["Count"] = messages.Count.ToString(),
                    ["Messages"] = string.Join("\n", messages.Select(message =>
                        $"  - #{message.OutboxId} {message.TemplateKey} to {message.Recipient}, " +
                        $"after {message.Attempts} attempt(s): {message.LastError}")),
                };
            },
            carriesCredential: false,
            "Messages");

        public static IReadOnlyList<EmailTemplate> All { get; } =
        [
            RegistrationReceived,
            ConfirmationReminder,
            AccountApproved,
            AccountRejected,
            PasswordReset,
            PasswordChanged,
            QuoteReceived,
            QuotePriced,
            OrderConfirmed,
            FeedFailed,
            FeedsStale,
            MailUndeliverable,
        ];

        private static readonly Dictionary<string, EmailTemplate> ByKey =
            All.ToDictionary(template => template.Key, StringComparer.Ordinal);

        /// <summary>The template a stored key names, or null when this build does not know it.</summary>
        public static EmailTemplate Find(string key) =>
            key is not null && ByKey.TryGetValue(key, out var template) ? template : null;

        /// <summary>
        /// A page in the customer's account area, on the store's own domain.
        /// </summary>
        /// <remarks>
        /// Built from <c>Site.Domain</c>, never from wherever a request came in, for the reason
        /// <c>MailedTokenLink</c> gives: this lands in an inbox. The account area checks the
        /// session's account against the document, so the link is useful only to the customer
        /// it was sent to — a forwarded one opens a sign-in page.
        /// </remarks>
        // Invariant, because the wording is English and the dispatcher's own culture is
        // whatever its server was installed with. "1 Nov 2026" either way.
        private static string Date(DateTime date) =>
            date.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

        private static string AccountLink(SiteModel site, string area, string reference, string suffix = "") =>
            string.IsNullOrWhiteSpace(reference)
                ? $"https://{site.Domain}/account/{area}"
                : $"https://{site.Domain}/account/{area}/{Uri.EscapeDataString(reference)}{suffix}";
    }
}
