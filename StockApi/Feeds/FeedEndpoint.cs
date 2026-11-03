using SMDataManager.Library.Models;

namespace StockApi.Feeds;

/// <summary>
/// Where a feed's stored password has been sent, and so the only place it may be sent again.
/// </summary>
/// <remarks>
/// The password is write-only to every admin. But Test and Update both accept an empty password as "use the stored one", and until
/// this existed they took the host, port, username and host key from the same request — so an
/// admin, or anyone holding an admin's token, could point a feed at their own SFTP server,
/// clear the host key so any server was trusted, and be handed the distributor's password in
/// plain text. Supplying a password lifts the rule: the caller then already knows what they
/// are sending.
/// </remarks>
public sealed record FeedEndpoint(string Host, int Port, string Username, string HostKeySha256)
{
    public const string ReenterPassword =
        "The stored password is only sent to the server it was saved for. Enter the password " +
        "to use a different host, port, username or host key.";

    public static FeedEndpoint Of(DistributorFeedModel feed) =>
        new(feed.Host, feed.Port, feed.Username, feed.HostKeySha256);

    public bool MayReceiveStoredPasswordFrom(FeedEndpoint stored) =>
        string.Equals(Host, stored.Host, StringComparison.OrdinalIgnoreCase)
        && Port == stored.Port
        && string.Equals(Username, stored.Username, StringComparison.Ordinal)
        // Pinning a key where there was none narrows who can receive the password; changing or
        // clearing one widens it.
        && (string.IsNullOrWhiteSpace(stored.HostKeySha256)
            || string.Equals(HostKeySha256, stored.HostKeySha256, StringComparison.Ordinal));
}
