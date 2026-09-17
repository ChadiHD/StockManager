using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Feeds;
using SMDataManager.Library.Models;
using StockApi.Feeds;
using StockApi.Sites;

namespace StockApi.Controllers
{
    // Distributor feed management. The password is write-only across this whole controller:
    // it can be supplied on create/update, but no response ever contains it — reads expose
    // only HasCredential so the UI can show whether one is set.
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class DistributorFeedController : ControllerBase
    {
        private readonly IDistributorFeedData _feedData;
        private readonly IDistributorFeedSyncService _sync;
        private readonly IFeedSecretStoreResolver _secrets;
        private readonly IAdminSiteContext _site;

        public DistributorFeedController(
            IDistributorFeedData feedData,
            IDistributorFeedSyncService sync,
            IFeedSecretStoreResolver secrets,
            IAdminSiteContext site)
        {
            _feedData = feedData;
            _sync = sync;
            _secrets = secrets;
            _site = site;
        }

        // What the client is allowed to see. Deliberately has no password and no SecretRef —
        // the reference is ciphertext or a vault name, and neither belongs in a browser.
        public record FeedView(int Id, string Name, string Host, int Port, string Username,
            string RemoteDirectory, string HostKeySha256, bool Enabled, bool HasCredential,
            string SecretProvider, DateTime? LastSyncedUtc, string LastSyncStatus,
            DateTime? SyncStartedUtc,
            string FieldSku, string FieldName, string FieldDescription, string FieldCategory,
            string FieldCost, string FieldSrp, string FieldQuantity,
            string FieldManufacturer, string FieldMpn, string FieldEan, string FieldIcecat);

        private static FeedView ToView(DistributorFeedModel feed) => new(
            feed.Id, feed.Name, feed.Host, feed.Port, feed.Username,
            feed.RemoteDirectory, feed.HostKeySha256, feed.Enabled, feed.HasCredential,
            feed.SecretProvider, feed.LastSyncedUtc, feed.LastSyncStatus, feed.SyncStartedUtc,
            feed.FieldSku, feed.FieldName, feed.FieldDescription, feed.FieldCategory,
            feed.FieldCost, feed.FieldSrp, feed.FieldQuantity,
            feed.FieldManufacturer, feed.FieldMpn, feed.FieldEan, feed.FieldIcecat);

        public record FeedInput(string? Name, string? Host, int Port, string? Username,
            string? Password, string? RemoteDirectory, string? HostKeySha256, bool Enabled,
            string? FieldSku, string? FieldName, string? FieldDescription, string? FieldCategory,
            string? FieldCost, string? FieldSrp, string? FieldQuantity,
            string? FieldManufacturer, string? FieldMpn, string? FieldEan, string? FieldIcecat);

        [HttpGet]
        public IEnumerable<FeedView> GetAll() => _feedData.GetFeeds(_site.SiteId).Select(ToView);

        [HttpGet("{id:int}")]
        public ActionResult<FeedView> GetById(int id)
        {
            var feed = _feedData.GetFeedById(id, _site.SiteId);

            return feed is null ? NotFound() : ToView(feed);
        }

        [HttpPost]
        public async Task<ActionResult<FeedView>> Create(FeedInput input)
        {
            if (string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Host)
                || string.IsNullOrWhiteSpace(input.Username))
            {
                return BadRequest("Name, Host and Username are required.");
            }

            if (string.IsNullOrWhiteSpace(input.Password))
            {
                return BadRequest("A password is required when creating a feed.");
            }

            var store = _secrets.Active;
            var model = Apply(new DistributorFeedModel(), input);
            model.SecretProvider = store.ProviderName;
            model.SecretRef = await store.ProtectAsync(input.Name, input.Password);

            var created = _feedData.CreateFeed(model, _site.SiteId);

            return created is null ? BadRequest("The feed could not be created.") : ToView(created);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, FeedInput input)
        {
            var existing = _feedData.GetFeedById(id, _site.SiteId);
            if (existing is null) return NotFound();

            var model = Apply(existing, input);
            model.Id = id;
            _feedData.UpdateFeed(model, _site.SiteId);

            // An empty password means "leave the stored credential alone", so editing a feed
            // never silently clears it.
            if (!string.IsNullOrWhiteSpace(input.Password))
            {
                var store = _secrets.Active;
                var reference = await store.ProtectAsync(input.Name ?? existing.Name, input.Password);
                _feedData.UpdateSecret(id, store.ProviderName, reference, _site.SiteId);
            }

            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var existing = _feedData.GetFeedById(id, _site.SiteId);
            if (existing is null) return NotFound();

            if (!string.IsNullOrWhiteSpace(existing.SecretRef))
            {
                await _secrets.For(existing.SecretProvider).RemoveAsync(existing.SecretRef);
            }

            _feedData.DeleteFeed(id, _site.SiteId);

            return NoContent();
        }

        // Connects and parses without importing. Accepts an unsaved feed so the operator can
        // verify credentials before committing them.
        [HttpPost("Test")]
        public async Task<ActionResult<DistributorFeedResult>> Test(FeedInput input, [FromQuery] int? id = null)
        {
            var model = id.HasValue ? _feedData.GetFeedById(id.Value, _site.SiteId) : null;
            model = Apply(model ?? new DistributorFeedModel(), input);

            if (id.HasValue && model.Id == 0) model.Id = id.Value;

            return await _sync.TestAsync(model, input.Password);
        }

        /// <summary>
        /// What this feed's recent runs did, which <c>LastSyncStatus</c> cannot say.
        /// </summary>
        /// <remarks>
        /// The feed is looked up first so an id belonging to another store answers 404 rather
        /// than an empty history. The procedure filters by site too, so this is belt and
        /// braces — but "no history" and "not your feed" are different answers and only one of
        /// them is true.
        /// </remarks>
        [HttpGet("{id:int}/History")]
        public ActionResult<IEnumerable<FeedSyncLogModel>> History(int id, [FromQuery] int take = 20)
        {
            if (_feedData.GetFeedById(id, _site.SiteId) is null) return NotFound();

            return _feedData.GetSyncHistory(id, _site.SiteId, take).ToList();
        }

        /// <summary>Recent runs across every feed in this store.</summary>
        [HttpGet("History")]
        public IEnumerable<FeedSyncLogModel> RecentHistory([FromQuery] int take = 20) =>
            _feedData.GetRecentSyncs(_site.SiteId, take);

        /// <summary>
        /// Enabled feeds this store has not heard from inside its own staleness threshold.
        /// </summary>
        /// <remarks>
        /// Only the id, the name and when it last delivered. The full <c>FeedView</c> would do,
        /// and this is narrower on purpose: the banner that reads this needs three fields, and
        /// a feed list is the heaviest read in this controller.
        /// </remarks>
        public record StaleFeedView(int Id, string Name, DateTime? LastSyncedUtc);

        [HttpGet("Stale")]
        public IEnumerable<StaleFeedView> Stale() =>
            _feedData.GetStaleFeeds(_site.SiteId)
                .Select(feed => new StaleFeedView(feed.Id, feed.Name, feed.LastSyncedUtc));

        [HttpPost("{id:int}/Sync")]
        public async Task<ActionResult<DistributorFeedResult>> Sync(int id)
        {
            var result = await _sync.SyncAsync(id, _site.SiteId, FeedSyncTrigger.Operator);

            if (result.Succeeded) return result;

            // Conflict rather than 502: nothing is wrong with the feed or with this request,
            // something else is already doing the work. Once the nightly schedule exists this
            // is the ordinary answer to a button pressed inside the sync window, and reporting
            // it as a bad gateway would teach an operator to ignore the status that matters.
            return result.AlreadyRunning
                ? Conflict(result)
                : StatusCode(StatusCodes.Status502BadGateway, result);
        }

        [HttpPost("Sync")]
        public async Task<ActionResult<List<DistributorFeedResult>>> SyncAll()
        {
            var results = await _sync.SyncAllAsync(_site.SiteId, FeedSyncTrigger.Operator);

            if (FeedSyncOutcome.IsTotalFailure(results))
            {
                return StatusCode(StatusCodes.Status502BadGateway, results);
            }

            return results;
        }

        private static DistributorFeedModel Apply(DistributorFeedModel model, FeedInput input)
        {
            model.Name = input.Name ?? model.Name;
            model.Host = input.Host ?? model.Host;
            model.Port = input.Port <= 0 ? 22 : input.Port;
            model.Username = input.Username ?? model.Username;
            model.RemoteDirectory = string.IsNullOrWhiteSpace(input.RemoteDirectory) ? "." : input.RemoteDirectory;
            model.HostKeySha256 = input.HostKeySha256;
            model.Enabled = input.Enabled;
            model.FieldSku = input.FieldSku;
            model.FieldName = input.FieldName;
            model.FieldDescription = input.FieldDescription;
            model.FieldCategory = input.FieldCategory;
            model.FieldCost = input.FieldCost;
            model.FieldSrp = input.FieldSrp;
            model.FieldQuantity = input.FieldQuantity;
            model.FieldManufacturer = input.FieldManufacturer;
            model.FieldMpn = input.FieldMpn;
            model.FieldEan = input.FieldEan;
            model.FieldIcecat = input.FieldIcecat;

            return model;
        }
    }
}
