namespace SMDataManager.Library.Tests;

/// <summary>
/// Every SaveData/LoadData call takes an anonymous object built fresh at its own call site, so
/// there is no shared, nameable type an NSubstitute call spec could be written against —
/// SaveData's own type parameter is inferred from that anonymous type and cannot be named from
/// this assembly at all. Matching by reflected property name is what lets the one parameter
/// that matters most, SiteId, be pinned down without coupling a test to a shape or a property
/// order it does not control.
/// </summary>
internal static class SqlParameterMatch
{
    public static bool Has(object parameters, string propertyName, object? expected) =>
        Equals(parameters.GetType().GetProperty(propertyName)?.GetValue(parameters), expected);
}
