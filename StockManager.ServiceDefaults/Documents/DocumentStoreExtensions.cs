using Microsoft.Extensions.DependencyInjection;
using StockManager.Documents;

namespace Microsoft.Extensions.Hosting;

public static class DocumentStoreExtensions
{
    /// <summary>The app host's blob container resource, and so the connection name.</summary>
    public const string ContainerName = "documents";

    /// <summary>
    /// Registers the document store both hosts read from: the storefront writes customer
    /// uploads, the admin API serves them back to a reviewer.
    /// </summary>
    /// <remarks>
    /// Blob storage since T7, from the app host's <c>documents</c> container — Azurite locally,
    /// a storage account when published. Both hosts must reference the same container, or a
    /// reviewer opens an application whose documents all 404.
    /// </remarks>
    public static TBuilder AddDocumentStore<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.Configure<DocumentStoreOptions>(
            builder.Configuration.GetSection(DocumentStoreOptions.SectionName));

        builder.AddAzureBlobContainerClient(ContainerName);
        builder.Services.AddSingleton<IDocumentStore, BlobDocumentStore>();

        return builder;
    }
}
