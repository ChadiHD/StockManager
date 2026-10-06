namespace StockApi.Models
{
    public class ApplicationUserModel
    {
        public string? UserId { get; set; }
        public string? Email { get; set; }
        public Dictionary<string, string> Roles { get; set; } = new Dictionary<string, string>();

        // The stores they may act for in the portal: every one, or these.
        public bool AllSites { get; set; }
        public List<int> SiteIds { get; set; } = new List<int>();
    }
}
