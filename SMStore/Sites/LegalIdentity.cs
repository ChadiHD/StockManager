using SMDataManager.Library.Models;

namespace SMStore.Sites;

/// <summary>
/// The store's legal identity as printed lines, for the footer and the document sheet (T9).
/// One place, so the two cannot disagree about what a company number is called.
/// </summary>
public static class LegalIdentity
{
    public static List<string> Lines(SiteModel site)
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(site.LegalName)) lines.Add(site.LegalName);
        if (!string.IsNullOrWhiteSpace(site.CompanyRegistrationNumber)) lines.Add($"Company no. {site.CompanyRegistrationNumber}");
        if (!string.IsNullOrWhiteSpace(site.TaxRegistrationNumber)) lines.Add($"VAT no. {site.TaxRegistrationNumber}");
        if (!string.IsNullOrWhiteSpace(site.RegisteredAddress)) lines.Add($"Registered office: {site.RegisteredAddress}");

        return lines;
    }
}
