namespace SMDataManager.Library.Models
{
    // A product awaiting an image, with the keys Icecat can be queried by.
    public class ProductImageCandidate
    {
        public int Id { get; set; }
        public string Sku { get; set; }
        public string ProductName { get; set; }
        public string Manufacturer { get; set; }
        public string ManufacturerPartNumber { get; set; }
        public string Ean { get; set; }

        /// <summary>Feed hint that Icecat content exists; not required to attempt a lookup.</summary>
        public bool? IcecatAvailable { get; set; }
    }

    public class ImageEnrichmentResult
    {
        public int Considered { get; set; }
        public int Matched { get; set; }
        public int NotFound { get; set; }
        public int Failed { get; set; }
        public bool Enabled { get; set; }
        public string Message { get; set; }
    }
}
