using Renci.SshNet;
using SMDataManager.Library.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace SMDataManager.Library.Feeds
{
    // Port of the StockViewer console prototype: connect over SFTP, take the newest XML file in
    // the directory tree, flatten each record and map it onto product fields. The prototype
    // wrote CSV; here the records go straight into dbo.Product via spProduct_UpsertFromFeed.
    public class SftpDistributorFeedClient : IDistributorFeedClient
    {
        public IReadOnlyList<DistributorFeedRecord> Fetch(DistributorFeedSettings settings)
        {
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(settings.Host))
                throw new InvalidOperationException($"Distributor feed '{settings.Name}' has no Host configured.");
            if (string.IsNullOrWhiteSpace(settings.Username) || string.IsNullOrWhiteSpace(settings.Password))
                throw new InvalidOperationException(
                    $"Distributor feed '{settings.Name}' is missing credentials. Edit the feed in the admin " +
                    "portal and enter its password.");

            using var client = new SftpClient(settings.Host, settings.Port, settings.Username, settings.Password);
            ConfigureHostKeyValidation(client, settings.HostKeySha256);
            client.Connect();

            RemoteXmlFile sourceFile;
            using var xmlStream = new MemoryStream();
            try
            {
                sourceFile = FindNewestXmlFile(client, settings.RemoteDirectory)
                    ?? throw new InvalidOperationException(
                        $"No XML files were found in '{settings.RemoteDirectory}' or its subdirectories.");

                client.DownloadFile(sourceFile.FullName, xmlStream);
            }
            finally
            {
                if (client.IsConnected) client.Disconnect();
            }

            xmlStream.Position = 0;
            var document = XDocument.Load(xmlStream, LoadOptions.None);

            return FindRecordElements(document)
                .Select(element => MapRecord(FlattenRecord(element), settings.Fields))
                .Where(record => !string.IsNullOrWhiteSpace(record.DistributorSku))
                .ToList();
        }

        private static DistributorFeedRecord MapRecord(Dictionary<string, string> values, FeedFieldMap fields)
        {
            return new DistributorFeedRecord
            {
                DistributorSku = Find(values, fields.Sku),
                Name = Find(values, fields.Name),
                Description = Find(values, fields.Description),
                Category = Find(values, fields.Category),
                Cost = ParseDecimal(Find(values, fields.Cost)),
                Srp = ParseDecimal(Find(values, fields.Srp)),
                Quantity = ParseInt(Find(values, fields.Quantity)),
                Raw = values
            };
        }

        // Feed keys are flattened paths ("item.sku"), so match on the trailing segment as well
        // as the whole key rather than forcing exact paths into configuration.
        private static string Find(Dictionary<string, string> values, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;

            if (values.TryGetValue(key, out var exact)) return exact;

            var match = values.FirstOrDefault(pair =>
                pair.Key.EndsWith("." + key, StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Equals("@" + key, StringComparison.OrdinalIgnoreCase));

            return match.Value;
        }

        private static decimal? ParseDecimal(string value) =>
            decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (decimal?)null;

        private static int ParseInt(string value) =>
            int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

        private static RemoteXmlFile FindNewestXmlFile(SftpClient client, string rootDirectory)
        {
            var directories = new Stack<string>();
            var visitedDirectories = new HashSet<string>(StringComparer.Ordinal);
            var xmlFiles = new List<RemoteXmlFile>();
            directories.Push(string.IsNullOrWhiteSpace(rootDirectory) ? "." : rootDirectory);

            while (directories.Count > 0)
            {
                var directory = directories.Pop();
                if (!visitedDirectories.Add(directory)) continue;

                foreach (var entry in client.ListDirectory(directory))
                {
                    if (entry.IsRegularFile && entry.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        xmlFiles.Add(new RemoteXmlFile(entry.Name, entry.FullName, entry.LastWriteTimeUtc));
                    }
                    else if (entry.IsDirectory && entry.Name != "." && entry.Name != "..")
                    {
                        directories.Push(entry.FullName);
                    }
                }
            }

            return xmlFiles
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static void ConfigureHostKeyValidation(SftpClient client, string expectedFingerprint)
        {
            if (string.IsNullOrWhiteSpace(expectedFingerprint)) return;

            client.HostKeyReceived += (_, eventArgs) =>
            {
                var actualFingerprint = Convert.ToBase64String(SHA256.HashData(eventArgs.HostKey)).TrimEnd('=');
                eventArgs.CanTrust = string.Equals(actualFingerprint, expectedFingerprint, StringComparison.Ordinal);
            };
        }

        private static IReadOnlyList<XElement> FindRecordElements(XDocument document)
        {
            var root = document.Root
                ?? throw new InvalidOperationException("The XML document has no root element.");

            var preferred = root.Descendants()
                .Where(element =>
                    element.Name.LocalName.Equals("product", StringComparison.OrdinalIgnoreCase) ||
                    element.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (preferred.Count > 0)
            {
                var first = preferred[0];
                return first.Parent?.Elements(first.Name).ToList() ?? preferred;
            }

            var repeatedComplexElements = root
                .DescendantsAndSelf()
                .SelectMany(parent => parent.Elements()
                    .Where(child => child.HasElements || child.HasAttributes)
                    .GroupBy(child => child.Name)
                    .Where(group => group.Count() > 1))
                .OrderByDescending(group => group.Count())
                .FirstOrDefault();
            if (repeatedComplexElements is not null)
            {
                return repeatedComplexElements.ToList();
            }

            var complexChildren = root.Elements()
                .Where(element => element.HasElements || element.HasAttributes)
                .ToList();
            return complexChildren.Count > 0 ? complexChildren : new List<XElement> { root };
        }

        private static Dictionary<string, string> FlattenRecord(XElement record)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddElementValues(record, string.Empty, values);
            return values;
        }

        private static void AddElementValues(XElement element, string path, IDictionary<string, string> values)
        {
            foreach (var attribute in element.Attributes())
            {
                var key = string.IsNullOrEmpty(path)
                    ? $"@{attribute.Name.LocalName}"
                    : $"{path}.@{attribute.Name.LocalName}";
                AddUniqueValue(values, key, attribute.Value);
            }

            var children = element.Elements().ToList();
            if (children.Count == 0)
            {
                AddUniqueValue(values, string.IsNullOrEmpty(path) ? "Value" : path, element.Value);
                return;
            }

            foreach (var group in children.GroupBy(child => child.Name.LocalName, StringComparer.OrdinalIgnoreCase))
            {
                var childPath = string.IsNullOrEmpty(path) ? group.Key : $"{path}.{group.Key}";
                var groupedChildren = group.ToList();
                if (groupedChildren.Count > 1 && groupedChildren.All(child => !child.HasElements && !child.HasAttributes))
                {
                    AddUniqueValue(values, childPath, string.Join(" | ", groupedChildren.Select(child => child.Value)));
                    continue;
                }

                for (var index = 0; index < groupedChildren.Count; index++)
                {
                    var indexedPath = groupedChildren.Count == 1 ? childPath : $"{childPath}[{index + 1}]";
                    AddElementValues(groupedChildren[index], indexedPath, values);
                }
            }
        }

        private static void AddUniqueValue(IDictionary<string, string> values, string key, string value)
        {
            var candidate = key;
            for (var suffix = 2; values.ContainsKey(candidate); suffix++)
            {
                candidate = $"{key}_{suffix}";
            }

            values[candidate] = value;
        }

        private sealed class RemoteXmlFile
        {
            public RemoteXmlFile(string name, string fullName, DateTime lastWriteTimeUtc)
            {
                Name = name;
                FullName = fullName;
                LastWriteTimeUtc = lastWriteTimeUtc;
            }

            public string Name { get; }
            public string FullName { get; }
            public DateTime LastWriteTimeUtc { get; }
        }
    }
}
