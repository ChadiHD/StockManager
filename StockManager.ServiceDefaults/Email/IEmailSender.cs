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
/// Plain text. Deliberately not HTML: customers' own words are substituted into several of
/// these messages, and plain text is what makes that substitution safe without an escaper.
/// </param>
public sealed record EmailMessage(
    string SiteKey, string To, string? ToName, string Subject, string Body);

/// <summary>
/// The mail transport: hands one rendered message to whatever delivers it.
/// </summary>
/// <remarks>
/// Since T6 this has exactly one caller, <c>EmailDispatcher</c> in StockApi. Everything else
/// queues through <c>IEmailOutbox</c> or, inside a procedure, <c>spEmailOutbox_Enqueue</c>,
/// and the dispatcher renders, sends, retries and dead-letters. A call site that came here
/// directly would skip all four and wait on a relay inside its own request;
/// <c>TransportCallerTests</c> is what keeps it to one.
///
/// The only implementation is still <see cref="LoggingEmailSender"/>, so nothing is delivered
/// anywhere. Choosing a real one — a provider and a sending domain per store — is T7's, with
/// the hosting decision, and it is now a registration rather than a rewrite.
/// </remarks>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
