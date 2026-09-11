using SMDataManager.Library.DataAccess;
using SMDataManager.Library.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.Feeds
{
    /// <summary>The brand to query a content provider with, if any.</summary>
    /// <param name="Brand">The provider's name for the manufacturer.</param>
    /// <param name="Usable">
    /// False when the feed value is not a brand — the caller should skip brand lookups and go
    /// straight to its EAN fallback.
    /// </param>
    public readonly struct ResolvedBrand
    {
        public ResolvedBrand(string brand, bool usable)
        {
            Brand = brand;
            Usable = usable;
        }

        public string Brand { get; }
        public bool Usable { get; }
    }

    public interface IBrandAliasResolver
    {
        ResolvedBrand Resolve(string distributor, string feedBrand);

        /// <summary>Drops the cache so an alias edit takes effect without a restart.</summary>
        void Invalidate();
    }

    /// <summary>
    /// Applies dbo.BrandAlias, preferring a row for the supplying distributor over a universal
    /// one. An unmapped brand passes through unchanged, so adding an alias is only necessary
    /// where the feed and the provider disagree.
    /// </summary>
    /// <remarks>
    /// Cached with a short lifetime: the table is a handful of rows read once per product
    /// during a backlog drain, and re-reading it thousands of times would be the dominant cost
    /// of an enrichment pass.
    /// </remarks>
    public class BrandAliasResolver : IBrandAliasResolver
    {
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

        // A factory rather than the data access itself: this resolver is a singleton so the
        // cache survives between enrichment batches, and holding a transient IDisposable for
        // the life of the app would be a captive dependency. Each refresh resolves its own.
        private readonly Func<IBrandAliasData> _dataFactory;
        private readonly object _gate = new object();

        private Dictionary<string, ResolvedBrand> _byDistributorAndBrand;
        private Dictionary<string, ResolvedBrand> _byBrand;
        private DateTime _loadedUtc;

        public BrandAliasResolver(Func<IBrandAliasData> dataFactory)
        {
            _dataFactory = dataFactory;
        }

        public ResolvedBrand Resolve(string distributor, string feedBrand)
        {
            if (string.IsNullOrWhiteSpace(feedBrand))
            {
                return new ResolvedBrand(null, false);
            }

            EnsureLoaded();

            var trimmed = feedBrand.Trim();

            Dictionary<string, ResolvedBrand> scoped;
            Dictionary<string, ResolvedBrand> universal;

            lock (_gate)
            {
                scoped = _byDistributorAndBrand;
                universal = _byBrand;
            }

            if (!string.IsNullOrWhiteSpace(distributor)
                && scoped.TryGetValue(Key(distributor, trimmed), out var specific))
            {
                return specific;
            }

            if (universal.TryGetValue(trimmed, out var general))
            {
                return general;
            }

            // No alias: the feed's own value is the best guess, and most of them are correct.
            return new ResolvedBrand(trimmed, true);
        }

        public void Invalidate()
        {
            lock (_gate)
            {
                _byDistributorAndBrand = null;
                _byBrand = null;
            }
        }

        private void EnsureLoaded()
        {
            lock (_gate)
            {
                bool fresh = _byBrand is not null
                    && DateTime.UtcNow - _loadedUtc < CacheLifetime;

                if (fresh)
                {
                    return;
                }

                var aliases = _dataFactory().GetAliases();

                _byDistributorAndBrand = aliases
                    .Where(alias => !string.IsNullOrWhiteSpace(alias.Distributor))
                    .GroupBy(alias => Key(alias.Distributor, alias.FeedBrand),
                             StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => ToResolved(group.First()),
                                  StringComparer.OrdinalIgnoreCase);

                _byBrand = aliases
                    .Where(alias => string.IsNullOrWhiteSpace(alias.Distributor))
                    .GroupBy(alias => alias.FeedBrand.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => ToResolved(group.First()),
                                  StringComparer.OrdinalIgnoreCase);

                _loadedUtc = DateTime.UtcNow;
            }
        }

        private static ResolvedBrand ToResolved(BrandAliasModel alias) =>
            string.IsNullOrWhiteSpace(alias.IcecatBrand)
                ? new ResolvedBrand(null, false)
                : new ResolvedBrand(alias.IcecatBrand.Trim(), true);

        // Joined with a unit separator, which cannot occur in a brand or distributor name, so
        // "AB" + "C" and "A" + "BC" cannot produce the same key.
        private static string Key(string distributor, string brand) =>
            distributor.Trim() + "" + brand.Trim();
    }
}
