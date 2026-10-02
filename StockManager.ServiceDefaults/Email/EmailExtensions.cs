using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockManager.Notifications;

namespace Microsoft.Extensions.Hosting;

public static class EmailExtensions
{
    /// <summary>
    /// Registers the mail transport. Since T6 only <c>StockApi</c> calls this, because only its
    /// outbox dispatcher sends; everything else queues.
    /// </summary>
    /// <remarks>
    /// <see cref="LoggingEmailSender"/> is registered with TryAdd, so a host that has already
    /// registered a real transport keeps it. Choosing that transport — a provider, a sending
    /// domain per store — is T7's, with the rest of the hosting decision.
    ///
    /// It is registered in every environment rather than Development only, because a
    /// dispatcher with no <see cref="IEmailSender"/> fails to resolve one and stops sending
    /// altogether — an unsendable message is a worse outcome than an unsent one. What it logs
    /// is gated on the environment instead; see that class.
    /// </remarks>
    public static TBuilder AddEmail<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.TryAddSingleton<IEmailSender, LoggingEmailSender>();

        return builder;
    }
}
