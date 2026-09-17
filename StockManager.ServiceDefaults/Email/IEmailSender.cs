namespace StockManager.Notifications;

/// <summary>
/// One message to one person, in plain text.
/// </summary>
/// <param name="SiteKey">
/// Which store is writing. Not decoration: a platform running several stores has a
/// from-address, a signature and a sender reputation per store, and a message that cannot
/// say which one it belongs to cannot be sent correctly by any transport that eventually
/// replaces the logger.
/// </param>
/// <param name="To">The recipient address.</param>
/// <param name="ToName">Their name, for the display part of the address.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="Body">
/// Plain text. Deliberately not HTML: T3 defines the seam and T6 owns templates, and a body
/// that arrives as markup now is markup T6 has to unpick rather than a value it can render.
/// </param>
public sealed record EmailMessage(
    string SiteKey, string To, string? ToName, string Subject, string Body);

/// <summary>
/// The seam every outbound customer email goes through.
/// </summary>
/// <remarks>
/// T3 has three messages to send — an application acknowledgement, an approval and a
/// rejection — and no infrastructure to send them with. This is that gap named rather than
/// papered over: the development implementation writes to the log, so the copy can be read
/// and the call sites can be wired, and nothing pretends mail is being delivered.
///
/// **T6 owns the outbox, the retry policy and the per-site templates.** What it will replace
/// is the implementation behind this interface. What it should not have to replace is the
/// call sites, which is why they are being written now — a registration that does not even
/// attempt to acknowledge itself is a registration T6 has to go hunting for.
///
/// Sending must not fail the operation that triggered it. An approval that rolled back
/// because a mail server was briefly unreachable would be a worse system than one that
/// approves and logs a failed notification.
/// </remarks>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
