using System;
using System.Collections.Generic;
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
            AccountApproved,
            AccountRejected,
            MailUndeliverable,
        ];

        private static readonly Dictionary<string, EmailTemplate> ByKey =
            All.ToDictionary(template => template.Key, StringComparer.Ordinal);

        /// <summary>The template a stored key names, or null when this build does not know it.</summary>
        public static EmailTemplate Find(string key) =>
            key is not null && ByKey.TryGetValue(key, out var template) ? template : null;
    }
}
