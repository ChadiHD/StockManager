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
        public string FieldManufacturer { get; set; }
        public string FieldMpn { get; set; }
        public string FieldEan { get; set; }
        public string FieldIcecat { get; set; }

        public DateTime? LastSyncedUtc { get; set; }
        public string LastSyncStatus { get; set; }
        public DateTime CreatedDate { get; set; }

        public bool HasCredential => !string.IsNullOrWhiteSpace(SecretRef);

        /// <summary>Projects the stored row onto the settings the SFTP client needs.</summary>
        /// <remarks>
        /// Blank stored values fall back to <see cref="FeedFieldMap"/>'s defaults rather than
        /// overwriting them with null. Assigning the row's columns directly meant a feed
        /// created without the optional identity mappings silently lost them: Manufacturer,
        /// MPN and EAN imported as NULL on every row, the sync still reported success, and
        /// the gap only surfaced later as an empty brand facet and no product images.
        /// </remarks>
        public DistributorFeedSettings ToSettings(string password)
        {
            var defaults = new FeedFieldMap();

            return new DistributorFeedSettings
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
                    Sku = Or(FieldSku, defaults.Sku),
                    Name = Or(FieldName, defaults.Name),
                    Description = Or(FieldDescription, defaults.Description),
                    Category = Or(FieldCategory, defaults.Category),
                    Cost = Or(FieldCost, defaults.Cost),
                    Srp = Or(FieldSrp, defaults.Srp),
                    Quantity = Or(FieldQuantity, defaults.Quantity),
                    Manufacturer = Or(FieldManufacturer, defaults.Manufacturer),
                    Mpn = Or(FieldMpn, defaults.Mpn),
                    Ean = Or(FieldEan, defaults.Ean),
                    IcecatFlag = Or(FieldIcecat, defaults.IcecatFlag)
                }
            };
        }

        private static string Or(string stored, string fallback) =>
            string.IsNullOrWhiteSpace(stored) ? fallback : stored;
    }
}
