namespace SMDataManager.Library.Models
{
    /// <summary>
    /// A store's own wording for one message. Null halves keep the platform's.
    /// </summary>
    public class SiteEmailTemplateModel
    {
        public int SiteId { get; set; }
        public string TemplateKey { get; set; }
        public string Subject { get; set; }
        public string Body { get; set; }
    }
}
