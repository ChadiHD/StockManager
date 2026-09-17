using System;

namespace SMDataManager.Library.Models
{
    // A distributor feed as stored in dbo.DistributorFeed. Note there is no password property:
    // the credential is held by an IFeedSecretStore and only referenced here.
    public class DistributorFeedModel
    {
        public int Id { get; set; }

        /// <summary>
        /// The store this feed belongs to. Projected so a sync can record its own result
        /// without the caller having to carry the site alongside the feed — every write over
        /// a feed is site-scoped, and a feed that cannot say which store it serves would have
        /// to be trusted instead.
        /// </summary>
        public int SiteId { get; set; }
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

        /// <summary>
        /// Set while a sync of this feed is running. See spDistributorFeed_ClaimForSync.
        /// </summary>
        /// <remarks>
        /// Read-only as far as anything outside the sync service is concerned: it is the claim,
        /// and writing it from a save would hand out a right nothing is holding. The portal
        /// shows it so an operator can tell a running sync from a stuck one.
        /// </remarks>
        public DateTime? SyncStartedUtc { get; set; }
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
