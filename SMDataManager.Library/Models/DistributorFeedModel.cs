using System;

namespace SMDataManager.Library.Models
{
    // A distributor feed as stored in dbo.DistributorFeed. Note there is no password property:
    // the credential is held by an IFeedSecretStore and only referenced here.
    public class DistributorFeedModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Host { get; set; }
        public int Port { get; set; } = 22;
        public string Username { get; set; }

        public string SecretProvider { get; set; }
        public string SecretRef { get; set; }

        public string RemoteDirectory { get; set; } = ".";
        public string HostKeySha256 { get; set; }
        public bool Enabled { get; set; } = true;

        public string FieldSku { get; set; }
        public string FieldName { get; set; }
        public string FieldDescription { get; set; }
        public string FieldCategory { get; set; }
        public string FieldCost { get; set; }
        public string FieldSrp { get; set; }
        public string FieldQuantity { get; set; }

        public DateTime? LastSyncedUtc { get; set; }
        public string LastSyncStatus { get; set; }
        public DateTime CreatedDate { get; set; }

        public bool HasCredential => !string.IsNullOrWhiteSpace(SecretRef);

        /// <summary>Projects the stored row onto the settings the SFTP client needs.</summary>
        public DistributorFeedSettings ToSettings(string password) => new()
        {
            Name = Name,
            Host = Host,
            Port = Port,
            Username = Username,
            Password = password,
            RemoteDirectory = string.IsNullOrWhiteSpace(RemoteDirectory) ? "." : RemoteDirectory,
            HostKeySha256 = HostKeySha256,
            Enabled = Enabled,
            Fields = new FeedFieldMap
            {
                Sku = FieldSku,
                Name = FieldName,
                Description = FieldDescription,
                Category = FieldCategory,
                Cost = FieldCost,
                Srp = FieldSrp,
                Quantity = FieldQuantity
            }
        };
    }
}
