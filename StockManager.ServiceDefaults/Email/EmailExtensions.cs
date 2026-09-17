using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockManager.Notifications;

namespace Microsoft.Extensions.Hosting;

public static class EmailExtensions
{
    /// <summary>
    /// Registers the outbound mail seam. Both hosts send: the storefront acknowledges an
    /// application, the admin API tells the applicant what was decided.
    /// </summary>
    /// <remarks>
    /// <see cref="LoggingEmailSender"/> is registered with TryAdd, so a host that has already
    /// registered a real transport keeps it. T6 replaces this line and nothing above it.
    ///
    /// It is registered in every environment rather than Development only, because a host with
    /// no <see cref="IEmailSender"/> at all fails to resolve one at the first registration —
    /// an unsendable message is a worse outcome than an unsent one. What it logs is gated on
    /// the environment instead; see that class.
    /// </remarks>
    public static TBuilder AddEmail<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.TryAddSingleton<IEmailSender, LoggingEmailSender>();

        return builder;
    }
}
