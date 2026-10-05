using Microsoft.Extensions.Logging;
using SMDataManager.Library.DataAccess;

namespace SMDataManager.Library.Email
{
    /// <summary>
    /// The C# half of <c>dbo.EmailOutbox</c>'s single writer, <c>spEmailOutbox_Enqueue</c>.
    /// </summary>
    /// <remarks>
    /// A message whose trigger is a procedure is queued by that procedure, inside its own
    /// transaction — approvals and rejections are. This is for the rest: a registration spans
    /// two databases, so there is no one transaction for the message to join, and a password
    /// reset is Identity's.
    ///
    /// For those, queueing is the last step after the write rather than part of it, and a
    /// host that dies in between loses the message. That window is one insert on the database
    /// the write just reached. What the outbox closes for them is the window that actually
    /// loses mail — the minutes or hours a mail relay can be unreachable for — and that one it
    /// closes completely.
    /// </remarks>
    public sealed class EmailOutbox : IEmailOutbox
    {
        private readonly IEmailOutboxData _data;
        private readonly OutboxPayloadProtector _protector;
        private readonly ILogger<EmailOutbox> _logger;

        public EmailOutbox(
            IEmailOutboxData data, OutboxPayloadProtector protector, ILogger<EmailOutbox> logger)
        {
            _data = data;
            _protector = protector;
            _logger = logger;
        }

        public bool Enqueue<TPayload>(
            int siteId, string to, string toName, EmailTemplate<TPayload> template, TPayload payload)
        {
            if (string.IsNullOrWhiteSpace(to))
            {
                _logger.LogWarning(
                    "No address to send {TemplateKey} to at site {SiteId}; nothing queued.",
                    template.Key, siteId);

                return false;
            }

            var json = template.Serialize(payload);

            _data.Enqueue(
                siteId,
                to.Trim(),
                toName,
                template.Key,
                template.CarriesCredential ? _protector.Protect(json) : json,
                template.CarriesCredential);

            return true;
        }
    }
}
