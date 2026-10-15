using SMDataManager.Library.Models;
using static SMStore.Localization.StoreText;

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
        if (!string.IsNullOrWhiteSpace(site.CompanyRegistrationNumber)) lines.Add(L["Company no. {0}", site.CompanyRegistrationNumber]);
        if (!string.IsNullOrWhiteSpace(site.TaxRegistrationNumber)) lines.Add(L["VAT no. {0}", site.TaxRegistrationNumber]);
        if (!string.IsNullOrWhiteSpace(site.RegisteredAddress)) lines.Add(L["Registered office: {0}", site.RegisteredAddress]);

        return lines;
    }
}
