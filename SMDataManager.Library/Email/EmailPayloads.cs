using System;
using System.Collections.Generic;

namespace SMDataManager.Library.Email
{
    /*
    What each template is queued with. Values, never prose: the prose is the template's.

    Several of these are written by procedures with FOR JSON rather than by C#, so the property
    names are a contract with T-SQL as much as with the call sites. EmailOutboxProcedureTests
    reads what each procedure writes back through these records, which is the tripwire that
    lets the two languages share the shape.
    */

    /// <param name="Reference">The AC- reference, or null on the neutral answer to a duplicate.</param>
    /// <param name="ConfirmationLink">
    /// Null on the same neutral answer: a token minted for an address already registered here
    /// would be a way of finding out that it is.
    /// </param>
    public sealed record RegistrationReceivedPayload(string Reference, string ConfirmationLink);

    /// <summary>Written by <c>spAccount_Approve</c>.</summary>
    public sealed record AccountApprovedPayload(string Company);

    /// <summary>Written by <c>spAccount_Reject</c>, which reads the reason back off the row.</summary>
    public sealed record AccountRejectedPayload(string Company, string Reason);

    public sealed record ConfirmationReminderPayload(string ConfirmationLink);

    public sealed record PasswordResetPayload(string ResetLink);

    /// <summary>Nothing to carry: the message is that it happened.</summary>
    public sealed record PasswordChangedPayload;

    /// <summary>Written by <c>spQuote_SubmitRequest</c>.</summary>
    public sealed record QuoteReceivedPayload(string Reference, int Lines);

    /// <summary>Written by <c>spQuote_Price</c>, every time the quote is sent.</summary>
    public sealed record QuotePricedPayload(string Reference, decimal Value, DateTime? ExpiresDate);

    /// <summary>Written by <c>spQuote_QueueExpiryNotices</c>, the nightly sweep.</summary>
    public sealed record QuoteExpiringPayload(string Reference, decimal Value, DateTime? ExpiresDate);

    /// <summary>Written by <c>spOrder_ConvertFromQuote</c>, from the order it has just totalled.</summary>
    public sealed record OrderConfirmedPayload(
        string Reference,
        string QuoteReference,
        string PoNumber,
        decimal SubTotal,
        decimal Tax,
        decimal Total,
        string TaxTreatment,
        string TaxLegend,
        DateTime? DueDate);

    public sealed record FeedFailedPayload(string FeedName, string Message);

    public sealed record FeedsStalePayload(IReadOnlyList<string> FeedNames);

    /// <summary>One message the dispatcher gave up on, as the operator is told about it.</summary>
    public sealed record UndeliverableMessage(
        int OutboxId, string TemplateKey, string Recipient, int Attempts, string LastError);

    public sealed record MailUndeliverablePayload(IReadOnlyList<UndeliverableMessage> Messages);
}
