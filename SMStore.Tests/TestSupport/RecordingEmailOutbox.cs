using SMDataManager.Library.Email;
using SMDataManager.Library.Models;
using StockManager.Notifications;

namespace SMStore.Tests.TestSupport;

/// <summary>
/// An outbox that remembers what was queued, and renders it the way the dispatcher would.
/// </summary>
/// <remarks>
/// Rendering here rather than asserting on payloads keeps the tests reading the words a
/// customer would receive — and puts the shared templates under the same assertions the
/// per-host copy classes used to be held to. <c>StockApi.Tests</c> has the same class; test
/// projects do not reference one another.
/// </remarks>
internal sealed class RecordingEmailOutbox : IEmailOutbox
{
    private readonly SiteModel _site;

    public RecordingEmailOutbox(SiteModel site)
    {
        _site = site;
    }

    public sealed record Queued(int SiteId, string To, string? ToName, EmailTemplate Template, string PayloadJson);

    public List<Queued> Messages { get; } = [];

    /// <summary>Set to make the next enqueue throw, as a database that is down would.</summary>
    public Exception? Fails { get; set; }

    public bool Enqueue<TPayload>(
        int siteId, string to, string toName, EmailTemplate<TPayload> template, TPayload payload)
    {
        if (Fails is { } failure)
        {
            throw failure;
        }

        if (string.IsNullOrWhiteSpace(to))
        {
            return false;
        }

        Messages.Add(new Queued(siteId, to, toName, template, template.Serialize(payload)));

        return true;
    }

    /// <summary>
    /// Every queued message, as the transport would be handed it. Rendered against
    /// <paramref name="site"/> when given, since some wording reads the store's settings at
    /// dispatch rather than when the message was queued.
    /// </summary>
    public List<EmailMessage> Rendered(SiteModel? site = null) => Messages
        .Select(queued =>
        {
            var store = site ?? _site;
            var rendered = EmailRenderer.Render(queued.Template, store, queued.PayloadJson);

            return new EmailMessage(store.SiteKey, queued.To, queued.ToName, rendered.Subject, rendered.Body);
        })
        .ToList();
}
