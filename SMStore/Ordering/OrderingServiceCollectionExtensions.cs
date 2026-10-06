namespace SMStore.Ordering;

public static class OrderingServiceCollectionExtensions
{
    /// <summary>
    /// Every ordering mode, and the provider that picks one per store. One place for the reason
    /// <c>AddRegistrationFieldSets</c> is.
    /// </summary>
    public static IServiceCollection AddOrderingModes(this IServiceCollection services)
    {
        services.AddSingleton<IOrderingMode, RfqOrderingMode>();
        services.AddScoped<OrderingModeProvider>();

        return services;
    }
}
