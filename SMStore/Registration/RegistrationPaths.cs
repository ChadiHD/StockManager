namespace SMStore.Registration;

/// <summary>
/// Where the customer application lives.
/// </summary>
/// <remarks>
/// A constant because the rate limiter has to recognise the path before routing has picked an
/// endpoint — see <c>CustomerRateLimiting</c>. It duplicates the <c>@page</c> directive in
/// <c>Register.razor</c>, which cannot read a constant; if one moves, the other has to.
/// </remarks>
public static class RegistrationPaths
{
    public const string RegisterPath = "/register";
}
