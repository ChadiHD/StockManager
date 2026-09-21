using System;
using System.Collections.Generic;
using System.Linq;

namespace SMDataManager.Library.Tax
{
    /// <summary>
    /// Picks the rule set a store trades under, from <c>Site.TaxRuleSet</c>.
    /// </summary>
    /// <remarks>
    /// The same shape as <c>OrderingModeProvider</c>, and keyed the same way, but it takes the
    /// key rather than an <c>ISiteContext</c>: this library is shared by both hosts and the
    /// site context lives in <c>SMStore</c>. The caller that has a site passes its key.
    /// </remarks>
    public sealed class TaxRuleSetProvider
    {
        private readonly IReadOnlyDictionary<string, ITaxRuleSet> _ruleSets;

        public TaxRuleSetProvider(IEnumerable<ITaxRuleSet> ruleSets)
        {
            _ruleSets = ruleSets.ToDictionary(set => set.Key, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The rule set for a site key, or a throw naming what is registered.
        /// </summary>
        /// <remarks>
        /// Failing loudly beats defaulting, exactly as it does for the ordering mode — and
        /// more so here. A site configured for a rule set nobody built would otherwise fall
        /// back to some other store's tax treatment and charge the wrong VAT quietly, on
        /// documents a customer files.
        /// </remarks>
        public ITaxRuleSet For(string ruleSetKey)
        {
            if (!string.IsNullOrWhiteSpace(ruleSetKey)
                && _ruleSets.TryGetValue(ruleSetKey, out var ruleSet))
            {
                return ruleSet;
            }

            throw new InvalidOperationException(
                $"No ITaxRuleSet is registered for TaxRuleSet '{ruleSetKey}'. "
                + $"Known rule sets: {string.Join(", ", _ruleSets.Keys)}.");
        }
    }
}
