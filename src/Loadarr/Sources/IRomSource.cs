using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Loadarr.Sources
{
    /// <summary>
    /// A pluggable ROM source. Implement this to add a new site.
    /// </summary>
    public interface IRomSource
    {
        string Name { get; }

        Task<IReadOnlyList<RomSearchResult>> SearchAsync(
            string query,
            string platformHint,
            CancellationToken ct);

        Task<ResolvedDownload> GetDownloadAsync(
            RomSearchResult result,
            CancellationToken ct);
    }
}
