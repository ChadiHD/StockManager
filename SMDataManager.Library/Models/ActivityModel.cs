using System;

namespace SMDataManager.Library.Models
{
    public class ActivityModel
    {
        public DateTime When { get; set; }
        public string Account { get; set; }
        public string What { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
        public string Screen { get; set; }
    }
}
