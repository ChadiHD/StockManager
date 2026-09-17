using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StockManager.Notifications;

/// <summary>
/// Writes the message to the log instead of sending it.
/// </summary>
/// <remarks>
/// The development implementation, and the only one T3 ships. It exists so the messages T3
/// needs can be written, read and reviewed before there is a transport — and so that the
/// absence of one is visible in the Aspire dashboard rather than silent.
///
/// **The body is logged in Development only**, because bodies now carry email-confirmation
/// and password-reset links. A reset link is a credential: whoever reads it can take the
/// account, and a log is copied, shipped to a telemetry backend and read by people who have no
/// business signing in as a customer. In Development that is exactly what makes the feature
/// testable — the link is read out of the Aspire dashboard — so the trade is drawn at the
/// environment rather than by stripping the body everywhere.
///
/// Outside Development the record is still useful: who was to be written to, about what, and
/// that nothing was sent. What it will not tell an operator is how to become that customer.
/// </remarks>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly ILogger<LoggingEmailSender> _logger;
    private readonly bool _logBodies;

    public LoggingEmailSender(ILogger<LoggingEmailSender> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _logBodies = environment.IsDevelopment();
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (_logBodies)
        {
            _logger.LogInformation(
                "Email not sent (no transport configured). From {SiteKey} to {To}: {Subject}\n{Body}",
                message.SiteKey, message.To, message.Subject, message.Body);

            return Task.CompletedTask;
        }

        _logger.LogWarning(
            "Email not sent (no transport configured). From {SiteKey} to {To}: {Subject}. The " +
            "body is withheld outside Development because it may carry a sign-in link.",
            message.SiteKey, message.To, message.Subject);

        return Task.CompletedTask;
    }
}
