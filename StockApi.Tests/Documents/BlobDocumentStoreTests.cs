using System.Text;
using Azure.Storage.Blobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using StockManager.Documents;
using Xunit;

namespace StockApi.Tests.Documents;

/// <summary>
/// The document store is the one place a name that came out of a URL names something in
/// storage. These hold that boundary and the per-store isolation the prefix exists for, against
/// a real blob service: Azurite, which the app host runs.
/// </summary>
/// <remarks>
/// They need <c>DOCUMENTS_TEST_BLOB_CONNECTION</c> — the <c>storage</c> resource's connection
/// string from the Aspire dashboard, or <c>UseDevelopmentStorage=true</c> for an Azurite on its
/// default ports — and skip without it, as the database tests skip without
/// <c>SMDATABASE_TEST_CONNECTION</c>. Each run uses a container of its own and deletes it.
/// <see cref="DocumentNamesTests"/> hold the name rules with no storage at all.
/// </remarks>
public sealed class BlobDocumentStoreTests : IAsyncLifetime
{
    private static readonly string? ConnectionString =
        Environment.GetEnvironmentVariable("DOCUMENTS_TEST_BLOB_CONNECTION");

    private const string SkipReason =
        "Set DOCUMENTS_TEST_BLOB_CONNECTION to an Azurite or storage account connection string to run these.";

    private BlobContainerClient? _container;
    private BlobDocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        _container = new BlobServiceClient(ConnectionString).GetBlobContainerClient($"test-{Guid.NewGuid():N}");
        await _container.CreateAsync();
        _store = new BlobDocumentStore(_container, NullLogger<BlobDocumentStore>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DeleteIfExistsAsync();
        }
    }

    private static MemoryStream Content(string text = "document body") =>
        new(Encoding.UTF8.GetBytes(text));

    [SkippableFact]
    public async Task SaveAsyncGeneratesTheStoredNameRatherThanDerivingItFromTheUpload()
    {
        Skip.If(_container is null, SkipReason);

        // SaveAsync takes no filename parameter at all; the only name it can return is one it
        // made up — see the remarks on IDocumentStore.SaveAsync.
        var storedName = await _store.SaveAsync("site-a", Content(), ".pdf");

        storedName.Should().MatchRegex(@"^[0-9a-fA-F]{32}\.pdf$");
    }

    [SkippableFact]
    public async Task SaveAsyncNamesTwoUploadsOfIdenticalContentDifferently()
    {
        Skip.If(_container is null, SkipReason);

        var first = await _store.SaveAsync("site-a", Content("same body"), ".pdf");
        var second = await _store.SaveAsync("site-a", Content("same body"), ".pdf");

        // Content-addressing would have made two identical uploads collide.
        first.Should().NotBe(second);
    }

    [SkippableFact]
    public async Task SavedContentCanBeReadBackUnchanged()
    {
        Skip.If(_container is null, SkipReason);

        var storedName = await _store.SaveAsync("site-a", Content("round trip"), ".pdf");

        await using var stream = await _store.OpenAsync("site-a", storedName);
        using var reader = new StreamReader(stream!);

        (await reader.ReadToEndAsync()).Should().Be("round trip");
    }

    [SkippableFact]
    public async Task DeleteAsyncReturnsFalseWhenTheBlobIsAlreadyGone()
    {
        Skip.If(_container is null, SkipReason);

        (await _store.DeleteAsync("site-a", $"{Guid.NewGuid():N}.pdf")).Should().BeFalse();
    }

    [SkippableFact]
    public async Task ARecordedNameWithNoBlobOpensAsNothing()
    {
        Skip.If(_container is null, SkipReason);

        (await _store.OpenAsync("site-a", $"{Guid.NewGuid():N}.pdf")).Should().BeNull();
    }

    [SkippableFact]
    public async Task DeleteAsyncRemovesABlobItActuallyWrote()
    {
        Skip.If(_container is null, SkipReason);

        var storedName = await _store.SaveAsync("site-a", Content(), ".pdf");

        (await _store.DeleteAsync("site-a", storedName)).Should().BeTrue();
        (await _store.OpenAsync("site-a", storedName)).Should().BeNull();
    }

    [SkippableFact]
    public async Task OneSiteCannotOpenAnotherSitesDocument()
    {
        Skip.If(_container is null, SkipReason);

        // Isolation comes from the prefix as well as from an unguessable name.
        var storedName = await _store.SaveAsync("site-a", Content("site A's document"), ".pdf");

        (await _store.OpenAsync("site-b", storedName)).Should().BeNull();
    }

    [SkippableFact]
    public async Task OneSiteCannotDeleteAnotherSitesDocument()
    {
        Skip.If(_container is null, SkipReason);

        var storedName = await _store.SaveAsync("site-a", Content(), ".pdf");

        (await _store.DeleteAsync("site-b", storedName)).Should().BeFalse();

        await using var stream = await _store.OpenAsync("site-a", storedName);
        stream.Should().NotBeNull();
    }

    [Theory]
    [InlineData("../../x.pdf")]
    [InlineData(@"..\..\x.pdf")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.exe")] // right shape, wrong extension
    [InlineData("not-a-generated-name.pdf")]
    public async Task ANameThisStoreDidNotGenerateIsRefusedBeforeStorageIsAsked(string storedName)
    {
        // A container that does not exist: if the store asked it anything, the call would fail
        // rather than return the refusal.
        var store = new BlobDocumentStore(
            new BlobContainerClient(new Uri("https://127.0.0.1:1/devstoreaccount1/absent")),
            NullLogger<BlobDocumentStore>.Instance);

        (await store.OpenAsync("site-a", storedName)).Should().BeNull();
        (await store.DeleteAsync("site-a", storedName)).Should().BeFalse();
    }
}

public class DocumentNamesTests
{
    [Theory]
    [InlineData("0123456789abcdef0123456789ABCDEF.pdf", true)]
    [InlineData("0123456789abcdef0123456789abcdef.jpg", true)]
    [InlineData("../../x.pdf", false)]
    [InlineData("0123456789abcdef0123456789abcdef.exe", false)]
    [InlineData("site-a/0123456789abcdef0123456789abcdef.pdf", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheShapeThisPlatformGeneratesIsAStoredName(string? name, bool accepted)
    {
        DocumentNames.IsStoredName(name).Should().Be(accepted);
    }

    [Theory]
    [InlineData("aclitrade")]
    [InlineData("site-b")]
    public void AnOrdinarySiteKeyIsAPrefix(string siteKey)
    {
        var check = () => DocumentNames.RequireSiteKey(siteKey);

        check.Should().NotThrow();
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("a/b")]
    [InlineData("-leading")]
    [InlineData("")]
    public void ASiteKeyThatCouldReachAnotherPrefixIsRefused(string siteKey)
    {
        var check = () => DocumentNames.RequireSiteKey(siteKey);

        check.Should().Throw<ArgumentException>();
    }
}
