namespace SMDataManager.Library.Models
{
    /// <summary>One of a product's documents. StoredName is served from /product-docs/; FileName only labels it.</summary>
    public class ProductDocumentModel
    {
        public string StoredName { get; set; }
        public string FileName { get; set; }
        public string ContentType { get; set; }
        public long SizeBytes { get; set; }
    }
}
