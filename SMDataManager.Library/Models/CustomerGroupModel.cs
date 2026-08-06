namespace SMDataManager.Library.Models
{
    public class CustomerGroupModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Slug { get; set; }
        public int Discount { get; set; }
        public string Terms { get; set; }
        public string Note { get; set; }
        public int Accounts { get; set; }
        public int Overrides { get; set; }
    }
}
