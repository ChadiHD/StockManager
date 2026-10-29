using Microsoft.AspNetCore.DataProtection;

namespace StockManager.Payments;

/// <summary>
/// A store's payment provider keys under Data Protection (T13): the portal's API encrypts them,
/// the storefront decrypts them.
/// </summary>
/// <remarks>
/// Here because both hosts must use one purpose, and the shared key ring
/// (<c>AddSharedDataProtection</c>) is what lets one read what the other wrote. The feed password
/// has its own purpose, so neither ciphertext opens with the other's protector.
/// </remarks>
public static class PaymentSecrets
{
    public const string Purpose = "StockManager.SitePayment.v1";

    public static IDataProtector Protector(IDataProtectionProvider provider) => provider.CreateProtector(Purpose);
}
