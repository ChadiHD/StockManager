namespace StockApi.Models
{
    public class ApplicationUserModel
    {
        public string? UserId { get; set; }
        public string? Email { get; set; }
        public Dictionary<string, string> Roles { get; set; } = new Dictionary<string, string>();
    }
}
