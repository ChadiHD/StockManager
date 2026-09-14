using Microsoft.Extensions.Logging;

namespace StockManager.Notifications;

/// <summary>
/// Writes the message to the log instead of sending it.
/// </summary>
/// <remarks>
/// The development implementation, and the only one T3 ships. It exists so the three
/// messages T3 needs can be written, read and reviewed before there is a transport — and so
/// that the absence of one is visible in the Aspire dashboard rather than silent.
///
/// It logs the whole body. That is fine while a body is an acknowledgement or a rejection
/// reason, and it stops being fine the moment one carries a password reset link — so when
/// T6 adds tokens, either this stops logging bodies or it stops being registered outside
/// Development. The registration in <c>AddEmail</c> already reads as development-only for
/// that reason.
/// </remarks>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Email not sent (no transport configured). From {SiteKey} to {To}: {Subject}\n{Body}",
            message.SiteKey, message.To, message.Subject, message.Body);

        return Task.CompletedTask;
    }
}
