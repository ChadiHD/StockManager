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
    /// </remarks>
    public static TBuilder AddEmail<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.TryAddSingleton<IEmailSender, LoggingEmailSender>();

        return builder;
    }
}
