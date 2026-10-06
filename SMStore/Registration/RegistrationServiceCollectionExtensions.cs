namespace SMStore.Registration;

public static class RegistrationServiceCollectionExtensions
{
    /// <summary>
    /// Every registration field set, and the provider that picks one per store.
    /// </summary>
    /// <remarks>
    /// One place, so a field set written but not registered — which the provider would refuse
    /// at a store's first registration — is caught by <c>SiteSettingKeysTests</c>, which
    /// resolves through this. Field sets are stateless rules, so singletons; the provider is
    /// scoped because it reads the request's site.
    /// </remarks>
    public static IServiceCollection AddRegistrationFieldSets(this IServiceCollection services)
    {
        services.AddSingleton<IRegistrationFieldSet, EuB2bRegistrationFieldSet>();
        services.AddScoped<RegistrationFieldSetProvider>();

        return services;
    }
}
