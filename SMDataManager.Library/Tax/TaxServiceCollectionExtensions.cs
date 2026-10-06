using Microsoft.Extensions.DependencyInjection;

namespace SMDataManager.Library.Tax
{
    public static class TaxServiceCollectionExtensions
    {
        /// <summary>
        /// Every tax rule set, the provider that picks one per store, and the assessor.
        /// </summary>
        /// <remarks>
        /// Both hosts call this, because an order is raised from either — a customer accepting
        /// their own quote in SMStore, an admin converting one in StockApi. Until T9 each host
        /// listed the rule sets itself, and a rule set added to one only would render on the
        /// storefront and throw when the admin converted. A new rule set is one line here, and
        /// a key in <c>SiteSettingKeys.TaxRuleSets</c>.
        /// </remarks>
        public static IServiceCollection AddTaxRuleSets(this IServiceCollection services)
        {
            services.AddSingleton<ITaxRuleSet, EuB2bTaxRuleSet>();
            services.AddSingleton<ITaxRuleSet, UkB2bTaxRuleSet>();
            services.AddSingleton<TaxRuleSetProvider>();
            services.AddSingleton<TaxAssessor>();

            return services;
        }
    }
}
