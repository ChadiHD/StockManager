using Xunit;

namespace SMDataManager.Library.Tests;

/// <summary>
/// The tests that need a real database run one at a time.
/// </summary>
/// <remarks>
/// xUnit runs test classes in parallel by default, and every database-backed test here holds an
/// open transaction while it inserts a Site, a SiteCategory, a CategoryMapping and some
/// Products. Two of those transactions running together deadlock on exactly those tables —
/// which is what happened the moment a second database-backed class existed:
///
/// <c>Transaction (Process ID 54) was deadlocked on lock resources with another process and has
/// been chosen as the deadlock victim.</c>
///
/// Retrying would be the wrong fix. These tests are not concurrency tests, the contention is an
/// artefact of the fixtures rather than of anything under test, and a retry would turn a
/// deadlock into an intermittent pass. One collection makes them sequential, which costs a few
/// seconds on the only machine that runs them at all.
///
/// Anything new that opens <see cref="TestDatabase.OpenRollbackScope"/> belongs in this
/// collection. Anything that does not touch a database must stay out of it, or the whole
/// project loses parallelism for no reason.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection
{
    public const string Name = "SMDatabase";
}
