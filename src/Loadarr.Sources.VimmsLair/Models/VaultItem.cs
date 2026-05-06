using System.Collections.Generic;

namespace Loadarr.Sources.VimmsLair.Models
{
    /// <summary>
    /// A parsed Vault detail page. Contains one MediaEntry per downloadable
    /// file (one for single-disc games, multiple for multi-disc games), plus
    /// the resolved download form action URL.
    /// </summary>
    public sealed class VaultItem
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string SystemName { get; set; }
        public IReadOnlyList<string> Regions { get; set; } = new List<string>();
        public string Year { get; set; }
        public IReadOnlyList<MediaEntry> Media { get; set; } = new List<MediaEntry>();

        /// <summary>
        /// Absolute URL the download form posts/gets to (e.g. https://dl3.vimm.net/).
        /// vimm.net rotates download hosts, so this MUST come from the page,
        /// not be hardcoded.
        /// </summary>
        public string DownloadEndpoint { get; set; }

        /// <summary>
        /// Detail page URL — used as the Referer when downloading.
        /// </summary>
        public string DetailUrl { get; set; }
    }

    public sealed class MediaEntry
    {
        public int Id { get; set; }
        public int SortOrder { get; set; }   // disc # (1-based) for multi-disc games
        public string Version { get; set; }  // e.g. "1.0", "Rev A"
        public string FileName { get; set; } // decoded GoodTitle, e.g. "FFVII (Disc 1).bin"
        public long ZippedBytes { get; set; }
        public string Crc { get; set; }
        public string Md5 { get; set; }
        public string Sha1 { get; set; }
    }
}
