using System.Collections.Generic;

namespace Loadarr.Sources.VimmsLair.Models
{
    /// <summary>
    /// One row from a Vault search/listing page. Each row corresponds to a game
    /// (which on the detail page may resolve to one or many discs).
    /// </summary>
    public sealed class VaultRow
    {
        public int Id { get; set; }
        public string SystemName { get; set; }
        public string Title { get; set; }
        public IReadOnlyList<string> Regions { get; set; } = new List<string>();
        public string Version { get; set; }
        public string Languages { get; set; }
        public bool IsUnlicensed { get; set; }
        public string DetailUrl { get; set; }
    }
}
