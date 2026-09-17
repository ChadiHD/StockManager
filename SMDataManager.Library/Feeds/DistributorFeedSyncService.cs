using Microsoft.Extensions.Logging;
using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SMDataManager.Library.Feeds
{
    public class DistributorFeedSyncService : IDistributorFeedSyncService
    {
        private readonly IDistributorFeedClient _feedClient;
        private readonly IProductData _productData;
        private readonly IDistributorFeedData _feedData;
        private readonly IFeedSecretStoreResolver _secrets;
        private readonly IImageEnrichmentSignal _enrichment;
        private readonly ILogger<DistributorFeedSyncService> _logger;

        public DistributorFeedSyncService(
            IDistributorFeedClient feedClient,
            IProductData productData,
            IDistributorFeedData feedData,
            IFeedSecretStoreResolver secrets,
            IImageEnrichmentSignal enrichment,
            ILogger<DistributorFeedSyncService> logger)
        {
            _feedClient = feedClient;
            _productData = productData;
            _feedData = feedData;
            _secrets = secrets;
            _enrichment = enrichment;
            _logger = logger;
        }

        public async Task<List<DistributorFeedResult>> SyncAllAsync(int siteId, FeedSyncTrigger trigger)
        {
            var results = new List<DistributorFeedResult>();

            foreach (var feed in _feedData.GetFeeds(siteId).Where(feed => feed.Enabled))
            {
                results.Add(await RunFeedAsync(feed, trigger));
            }

            return results;
        }

        public async Task<DistributorFeedResult> SyncAsync(int feedId, int siteId, FeedSyncTrigger trigger)
        {
            var feed = _feedData.GetFeedById(feedId, siteId);

            if (feed is null)
            {
                return new DistributorFeedResult
                {
                    Distributor = feedId.ToString(),
                    Succeeded = false,
                    Error = $"No distributor feed with id {feedId} exists."
                };
            }

            return await RunFeedAsync(feed, trigger);
        }

        public async Task<DistributorFeedResult> TestAsync(DistributorFeedModel feed, string password)
        {
            var result = new DistributorFeedResult { Distributor = feed.Name };

            try
            {
                // Resolve the stored credential when the caller did not supply a new one, so an
                // existing feed can be tested without re-entering its password.
                string secret = string.IsNullOrEmpty(password)
                    ? await ResolveSecretAsync(feed)
                    : password;

                var records = _feedClient.Fetch(feed.ToSettings(secret));

                result.RecordCount = records.Count;
                result.DiscoveredFields = DescribeFields(records);
                result.Succeeded = true;
            }
            catch (Exception exception)
            {
                result.Succeeded = false;
                result.Error = exception.Message;
            }

            return result;
        }

        /// <summary>
        /// Summarises which fields a feed actually carries, so its mapping can be chosen by
        /// looking rather than guessing.
        /// </summary>
        /// <remarks>
        /// Sampled rather than counted over the whole feed: these files run to thousands of
        /// records and the answer to "is this field on every row" does not change after the
        /// first few hundred.
        /// </remarks>
        private static List<FeedFieldSample> DescribeFields(IReadOnlyList<DistributorFeedRecord> records)
        {
            const int sampleSize = 500;

            var sample = records.Take(sampleSize).ToList();

            if (sample.Count == 0)
            {
                return new List<FeedFieldSample>();
            }

            return sample
                .SelectMany(record => record.Raw.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(key => new FeedFieldSample
                {
                    Name = key,
                    PopulatedPct = (int)Math.Round(
                        100.0 * sample.Count(record =>
                            record.Raw.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                        / sample.Count),
                    SampleValue = Truncate(
                        sample
                            .Select(record => record.Raw.TryGetValue(key, out var value) ? value : null)
                            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)))
                })
                // Best-populated first: the fields worth mapping are the ones that are always
                // there, and a feed can carry dozens of sparse ones nobody needs.
                .OrderByDescending(field => field.PopulatedPct)
                .ThenBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string Truncate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            var trimmed = value.Trim();

            return trimmed.Length <= 80 ? trimmed : trimmed.Substring(0, 77) + "...";
        }

        // One feed failing must not abort the others, so failures are captured per feed and
        // reported back rather than thrown.
        private async Task<DistributorFeedResult> RunFeedAsync(
            DistributorFeedModel feed, FeedSyncTrigger trigger)
        {
            var result = new DistributorFeedResult { Distributor = feed.Name };

            // Before the claim, so a run that waited on a busy database still reports how long
            // it really took. The procedure stamps the finish, and the pair is the duration an
            // operator reads when a feed starts taking twice as long as it used to.
            var startedUtc = DateTime.UtcNow;

            /*
            Claimed before anything is fetched, and nothing below runs without the claim.

            The claim is what makes this method safe to call from a scheduler as well as a
            button. Two concurrent imports of one feed are not corrupting — each builds its
            whole item set before the MERGE, and spProduct_BulkUpsertFromFeed is one
            transaction — but they are two SFTP sessions and two full imports for one result,
            and on a distributor that limits concurrent logins the second one fails and reports
            a broken feed that is not broken.
            */
            if (!_feedData.ClaimForSync(feed.Id, feed.SiteId))
            {
                result.AlreadyRunning = true;
                result.Error = "A sync of this feed is already running.";

                _logger.LogInformation(
                    "Skipped distributor feed {Distributor}: a sync is already running.", feed.Name);

                return result;
            }

            try
            {
                string secret = await ResolveSecretAsync(feed);
                var records = _feedClient.Fetch(feed.ToSettings(secret));
                result.RecordCount = records.Count;

                // One round trip for the whole feed, applied atomically.
                var upsert = _productData.BulkUpsertFromFeed(feed.Name, records);
                result.Imported = upsert.Received;
                result.Delisted = upsert.Delisted;
                result.Succeeded = true;

                _feedData.RecordSync(feed.Id, feed.SiteId, new FeedSyncRecord
                {
                    StartedUtc = startedUtc,
                    Succeeded = true,
                    Status = $"Imported {result.Imported} of {result.RecordCount}; {result.Delisted} delisted.",
                    RecordCount = result.RecordCount,
                    Imported = result.Imported,
                    Delisted = result.Delisted,
                    TriggeredBy = trigger
                });

                // A sync is the only thing that introduces products with no image, so it is
                // also the only moment worth starting a pass. Signalling rather than enriching
                // here keeps thousands of Icecat calls out of this request.
                _enrichment.RequestPass();

                _logger.LogInformation(
                    "Distributor feed {Distributor} imported {Imported} of {Total} records ({Delisted} delisted).",
                    feed.Name, result.Imported, result.RecordCount, result.Delisted);
            }
            catch (Exception exception)
            {
                result.Succeeded = false;
                result.Error = exception.Message;

                // Message only — the exception could carry connection detail, and the status is
                // shown in the portal.
                //
                // RecordCount is carried through even on a failure: a feed that fetched 40,000
                // records and then failed to import them is a different problem from one that
                // never connected, and the history is where that is answered.
                _feedData.RecordSync(feed.Id, feed.SiteId, new FeedSyncRecord
                {
                    StartedUtc = startedUtc,
                    Succeeded = false,
                    Status = $"Failed: {Trim(exception.Message, 380)}",
                    RecordCount = result.RecordCount,
                    TriggeredBy = trigger
                });

                _logger.LogError(exception, "Distributor feed {Distributor} failed.", feed.Name);
            }

            return result;
        }

        private async Task<string> ResolveSecretAsync(DistributorFeedModel feed)
        {
            if (string.IsNullOrWhiteSpace(feed.SecretRef))
            {
                throw new InvalidOperationException(
                    $"Feed '{feed.Name}' has no stored credential. Edit the feed and enter its password.");
            }

            return await _secrets.For(feed.SecretProvider).ResolveAsync(feed.SecretRef);
        }

        private static string Trim(string value, int maxLength) =>
            string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
