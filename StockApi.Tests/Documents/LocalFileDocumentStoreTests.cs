using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StockManager.Documents;
using Xunit;

namespace StockApi.Tests.Documents;

/// <summary>
/// LocalFileDocumentStore is the one place an attacker-supplied name could reach a filesystem
/// path — see the remarks on IDocumentStore and on TryResolve. Every test here exercises that
/// boundary, or the per-store isolation the container layout exists to provide, rather than
/// the plumbing around it.
/// </summary>
public class LocalFileDocumentStoreTests : IDisposable
{
    private readonly string _root;
    private readonly LocalFileDocumentStore _store;

    public LocalFileDocumentStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sm-doc-store-tests-" + Guid.NewGuid().ToString("N"));

        _store = new LocalFileDocumentStore(
            Options.Create(new DocumentStoreOptions { RootPath = _root }),
            contentRootPath: _root,
            NullLogger<LocalFileDocumentStore>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static MemoryStream Content(string text = "document body") =>
        new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task SaveAsyncGeneratesTheStoredNameRatherThanDerivingItFromTheUpload()
    {
        // SaveAsync takes no filename parameter at all; the only name it can return is one it
        // made up — see the remarks on IDocumentStore.SaveAsync.
        var storedName = await _store.SaveAsync("site-a", Content(), ".pdf", CancellationToken.None);

        storedName.Should().MatchRegex(@"^[0-9a-fA-F]{32}\.pdf$");
    }

    [Fact]
    public async Task SaveAsyncNamesTwoUploadsOfIdenticalContentDifferently()
    {
        var first = await _store.SaveAsync("site-a", Content("same body"), ".pdf", CancellationToken.None);
        var second = await _store.SaveAsync("site-a", Content("same body"), ".pdf", CancellationToken.None);

        // Proves the name is generated rather than derived from the upload — content-addressing
        // would have made two identical uploads collide.
        first.Should().NotBe(second);
    }

    [Fact]
    public async Task SavedContentCanBeReadBackUnchanged()
    {
        var storedName = await _store.SaveAsync("site-a", Content("round trip"), ".pdf", CancellationToken.None);

        await using var stream = await _store.OpenAsync("site-a", storedName, CancellationToken.None);
        using var reader = new StreamReader(stream!);

        (await reader.ReadToEndAsync()).Should().Be("round trip");
    }

    [Theory]
    [InlineData("../../x.pdf")]
    [InlineData("..\\..\\x.pdf")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.exe")] // right shape, wrong extension
    [InlineData("not-a-generated-name.pdf")]
    public async Task OpenAsyncRefusesAStoredNameThisStoreDidNotGenerate(string storedName)
    {
        (await _store.OpenAsync("site-a", storedName, CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData("../../x.pdf")]
    [InlineData("..\\..\\x.pdf")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.exe")]
    [InlineData("not-a-generated-name.pdf")]
    public async Task DeleteAsyncRefusesAStoredNameThisStoreDidNotGenerate(string storedName)
    {
        (await _store.DeleteAsync("site-a", storedName, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsyncReturnsFalseWhenTheFileIsAlreadyGone()
    {
        var wellShapedButNeverSaved = $"{Guid.NewGuid():N}.pdf";

        (await _store.DeleteAsync("site-a", wellShapedButNeverSaved, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsyncRemovesAFileItActuallyWrote()
    {
        var storedName = await _store.SaveAsync("site-a", Content(), ".pdf", CancellationToken.None);

        (await _store.DeleteAsync("site-a", storedName, CancellationToken.None)).Should().BeTrue();
        (await _store.OpenAsync("site-a", storedName, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task OneSiteCannotOpenAnotherSitesFile()
    {
        // Containers are laid out one per store — see the remarks on IDocumentStore — so this
        // proves isolation comes from more than an unguessable name.
        var storedName = await _store.SaveAsync("site-a", Content("site A's document"), ".pdf", CancellationToken.None);

        (await _store.OpenAsync("site-b", storedName, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task OneSiteCannotDeleteAnotherSitesFile()
    {
        var storedName = await _store.SaveAsync("site-a", Content(), ".pdf", CancellationToken.None);

        (await _store.DeleteAsync("site-b", storedName, CancellationToken.None)).Should().BeFalse();

        // Still there — the wrong-site delete attempt must not have reached it. Disposed
        // immediately rather than left open, or the fixture's own cleanup cannot remove it.
        await using var stream = await _store.OpenAsync("site-a", storedName, CancellationToken.None);
        stream.Should().NotBeNull();
    }
}
