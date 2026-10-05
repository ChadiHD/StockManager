using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SMDataManager.Library.Models;

namespace SMDataManager.Library.Email
{
    /// <summary>Who a message is for, which decides what happens when it cannot be delivered.</summary>
    public enum EmailAudience
    {
        /// <summary>Somebody buying from the store. An undeliverable one alerts the operator.</summary>
        Customer,

        /// <summary>
        /// The store's own operator. An undeliverable one is logged and goes no further: the
        /// alert would go to the same address that just refused this.
        /// </summary>
        Operator
    }

    /// <summary>
    /// One kind of message: its key, its platform wording, and how its payload becomes values.
    /// </summary>
    /// <remarks>
    /// The wording is plain text with <c>{Token}</c> placeholders, filled in by
    /// <see cref="EmailRenderer"/> at dispatch rather than when the message was owed — so a
    /// template corrected after a row was queued applies to the rows still waiting. That is
    /// the reason <c>dbo.EmailOutbox</c> holds values rather than a body.
    ///
    /// Plain text, never markup. Some of these messages quote a customer's own words —
    /// a rejection reason, a quote note — and a value is substituted as text and never read
    /// as template, which is the <c>SiteContent.BodyHtml</c> rule running in reverse.
    /// </remarks>
    public abstract class EmailTemplate
    {
        protected EmailTemplate(
            string key,
            EmailAudience audience,
            bool carriesCredential,
            string subject,
            string body,
            IEnumerable<string> requiredTokens)
        {
            Key = key;
            Audience = audience;
            CarriesCredential = carriesCredential;
            DefaultSubject = subject;
            DefaultBody = body;
            RequiredTokens = requiredTokens.ToArray();
        }

        /// <summary>What <c>EmailOutbox.TemplateKey</c> holds. Stored, so never renamed.</summary>
        public string Key { get; }

        public EmailAudience Audience { get; }

        /// <summary>
        /// Whether the payload holds a sign-in or confirmation link, and is therefore stored
        /// encrypted. See <c>EmailOutbox.PayloadJson</c>.
        /// </summary>
        public bool CarriesCredential { get; }

        public string DefaultSubject { get; }

        public string DefaultBody { get; }

        /// <summary>
        /// Placeholders a store's own wording may not leave out, because the message is
        /// useless without them — a reset mail with no link, a rejection that does not say
        /// why. They may still render empty; what they may not do is be missing.
        /// </summary>
        public IReadOnlyCollection<string> RequiredTokens { get; }

        /// <summary>The values this payload supplies, by placeholder name.</summary>
        public abstract IReadOnlyDictionary<string, string> Values(SiteModel site, string payloadJson);
    }

    /// <summary>A template and the shape of the payload it is queued with.</summary>
    /// <remarks>
    /// Typed so a call site cannot queue one template with another's payload — the mismatch
    /// would otherwise surface hours later as a message rendered with blanks.
    /// </remarks>
    public sealed class EmailTemplate<TPayload> : EmailTemplate
    {
        private readonly Func<SiteModel, TPayload, IReadOnlyDictionary<string, string>> _values;

        public EmailTemplate(
            string key,
            EmailAudience audience,
            string subject,
            string body,
            Func<SiteModel, TPayload, IReadOnlyDictionary<string, string>> values,
            bool carriesCredential = false,
            params string[] requiredTokens)
            : base(key, audience, carriesCredential, subject, body, requiredTokens)
        {
            _values = values;
        }

        public string Serialize(TPayload payload) =>
            JsonSerializer.Serialize(payload, EmailPayloadJson.Options);

        public override IReadOnlyDictionary<string, string> Values(SiteModel site, string payloadJson)
        {
            // An absent payload deserialises to a record of nulls rather than failing, so a
            // template whose values cope with nulls renders something legible. The tests hold
            // every template to that.
            var payload = JsonSerializer.Deserialize<TPayload>(
                string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson,
                EmailPayloadJson.Options);

            return _values(site, payload);
        }
    }

    /// <summary>
    /// How payloads are written and read, by C# and by the procedures that build them with
    /// <c>FOR JSON</c>.
    /// </summary>
    /// <remarks>
    /// Web defaults: camelCase on the way out, case-insensitive on the way in. A procedure
    /// writing <c>[company]</c> and a record declaring <c>Company</c> meet in the middle, and
    /// the database tests are what say that they do.
    /// </remarks>
    public static class EmailPayloadJson
    {
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}
