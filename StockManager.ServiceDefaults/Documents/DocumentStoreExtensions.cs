using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockManager.Documents;

namespace Microsoft.Extensions.Hosting;

public static class DocumentStoreExtensions
{
    /// <summary>
    /// Registers the document store both hosts read from: the storefront writes customer
    /// uploads, the admin API serves them back to a reviewer.
    /// </summary>
    /// <remarks>
    /// Local files today. Swapping in blob storage is a different <c>IDocumentStore</c>
    /// registered here and nothing else, which is the reason the interface exists at all.
    /// </remarks>
    public static TBuilder AddDocumentStore<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.Configure<DocumentStoreOptions>(
            builder.Configuration.GetSection(DocumentStoreOptions.SectionName));

        // The content root is passed in rather than resolved through IWebHostEnvironment so
        // the store itself has no dependency on ASP.NET hosting — it is a file API, and a
        // background job or a migration tool should be able to construct one.
        builder.Services.AddSingleton<IDocumentStore>(services => new LocalFileDocumentStore(
            services.GetRequiredService<IOptions<DocumentStoreOptions>>(),
            builder.Environment.ContentRootPath,
            services.GetRequiredService<ILogger<LocalFileDocumentStore>>()));

        return builder;
    }
}
