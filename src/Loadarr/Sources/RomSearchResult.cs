using System.Collections.Generic;

namespace Loadarr.Sources
{
    public sealed class RomSearchResult
    {
        public string SourceName { get; set; }
        public string Title { get; set; }
        public string Platform { get; set; }
        public string Region { get; set; }
        public string Version { get; set; }
        public long? SizeBytes { get; set; }
        public string DetailsUrl { get; set; }

        // Provider-private payload used by GetDownloadAsync to resolve the actual file URL.
        public IDictionary<string, string> ProviderTag { get; set; } = new Dictionary<string, string>();

        public string DisplaySize =>
            SizeBytes.HasValue ? FormatSize(SizeBytes.Value) : "—";

        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return $"{v:0.#} {units[u]}";
        }
    }

    public sealed class ResolvedDownload
    {
        public string Url { get; set; }
        public string FileName { get; set; }
        public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    }
}
