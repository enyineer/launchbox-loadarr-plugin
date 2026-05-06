using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Loadarr.Sources.VimmsLair;
using VL = Loadarr.Sources.VimmsLair;

namespace Loadarr.Sources
{
    /// <summary>
    /// LaunchBox-side adapter for vimm.net. Real scraping logic lives in the
    /// netstandard2.0 Loadarr.Sources.VimmsLair library so it can be unit-tested
    /// independently of the LaunchBox plugin host.
    ///
    /// Multi-disc games (e.g. PS1 Final Fantasy VII has 3 discs) are exposed as
    /// one search result per disc. Each result resolves to one download.
    /// </summary>
    internal sealed class VimmsLairSource : IRomSource
    {
        private const string KeyDetailId = "vault.detailId";
        private const string KeyMediaId = "vault.mediaId";

        private readonly VimmsLairClient _client;

        public VimmsLairSource(HttpClient http)
        {
            _client = new VimmsLairClient(http);
        }

        public string Name => "Vimm's Lair";

        public async Task<IReadOnlyList<RomSearchResult>> SearchAsync(
            string query, string platformHint, CancellationToken ct)
        {
            var rows = await _client.SearchAsync(query, ct).ConfigureAwait(false);

            // For each row, optionally restrict to a platform hint by mapping
            // LaunchBox's free-form platform name → vimm system code(s).
            var allowedSystems = Services.PlatformMapper.ToVimmSystemCodes(platformHint);

            // We don't know multi-disc up front from the search row — that
            // requires a detail fetch. Fetching every detail page would be
            // wasteful, so we surface one result per row here and only expand
            // to per-disc on selection in GetDownloadAsync.
            var results = new List<RomSearchResult>(rows.Count);
            foreach (var r in rows)
            {
                if (allowedSystems.Count > 0 &&
                    !allowedSystems.Contains(r.SystemName, StringComparer.OrdinalIgnoreCase))
                    continue;

                results.Add(new RomSearchResult
                {
                    SourceName = Name,
                    Title = r.Title + (r.IsUnlicensed ? " (Unlicensed)" : ""),
                    Platform = Services.PlatformMapper.ToLaunchBoxName(r.SystemName) ?? r.SystemName,
                    Region = r.Regions.Count == 0 ? null : string.Join(", ", r.Regions),
                    Version = r.Version,
                    DetailsUrl = r.DetailUrl,
                    ProviderTag = new Dictionary<string, string>
                    {
                        [KeyDetailId] = r.Id.ToString(),
                    }
                });
            }
            return results;
        }

        public async Task<ResolvedDownload> GetDownloadAsync(
            RomSearchResult result, CancellationToken ct)
        {
            if (!result.ProviderTag.TryGetValue(KeyDetailId, out var idStr) ||
                !int.TryParse(idStr, out var detailId))
                throw new InvalidOperationException("Result is missing vimm detail id.");

            var item = await _client.GetItemAsync(detailId, ct).ConfigureAwait(false);
            if (item.Media.Count == 0)
                throw new InvalidOperationException("Vimm detail page had no downloadable media.");

            // Multi-disc: take the first disc by SortOrder. The UI flow can
            // be extended later to download all discs; for the v0.1 single-
            // ResolvedDownload contract, returning disc 1 is the safe default.
            var media = item.Media.OrderBy(m => m.SortOrder).First();
            var url = VL.VimmsLairClient.BuildDownloadUrl(item.DownloadEndpoint, media.Id);

            // Vimm always serves the ROM wrapped in a .zip (alongside a Vimm's
            // Lair.txt readme). The detail page shows the INNER filename
            // (e.g. "Paper Mario (Europe).z64"), but the bytes we receive are
            // a zip. Force the .zip extension here so the downstream extractor
            // recognises it.
            var baseName = string.IsNullOrEmpty(media.FileName)
                ? "vimm-" + media.Id
                : Path.GetFileNameWithoutExtension(media.FileName);

            return new ResolvedDownload
            {
                Url = url.ToString(),
                FileName = baseName + ".zip",
                Headers = new Dictionary<string, string>
                {
                    ["Referer"] = item.DetailUrl,
                    ["User-Agent"] = VL.VimmsLairClient.DefaultUserAgent,
                }
            };
        }
    }
}
