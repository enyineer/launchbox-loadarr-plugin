using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Loadarr.Sources.VimmsLair.Models;

namespace Loadarr.Sources.VimmsLair.Parsing
{
    /// <summary>
    /// Parses Vault search-result and per-system listing pages into <see cref="VaultRow"/>s.
    ///
    /// Two layouts in the wild (May 2026):
    ///
    ///   Search results (multiple systems):
    ///     [System][Title link][Regions][Version][Languages]
    ///
    ///   Per-system listing (e.g. /vault/SNES/A):
    ///     [Title link][Regions][Version][Languages][Rating]
    ///
    /// We don't hardcode column indices — we locate the cell that contains the
    /// game's title anchor (href = "/vault/&lt;digits&gt;") and read the cells
    /// before/after it relative to that. The "rating" column (which is itself
    /// a link to /vault/?p=rating&amp;id=N) is ignored because its href has a
    /// query string and our regex only accepts the bare numeric form.
    /// </summary>
    public sealed class VaultListingParser
    {
        // Anchor href must be exactly "/vault/<digits>" (optional trailing slash).
        // Rating links of form "/vault/?p=rating&id=N" are deliberately excluded.
        private static readonly Regex VaultIdHref =
            new Regex(@"^/vault/(?<id>\d+)/?$", RegexOptions.Compiled);

        private const string BaseUrl = "https://vimm.net";

        public IReadOnlyList<VaultRow> Parse(string html)
        {
            if (string.IsNullOrEmpty(html)) return Array.Empty<VaultRow>();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // All <tr> elements anywhere; we filter to ones that actually contain
            // a game title anchor.
            var trs = doc.DocumentNode.SelectNodes("//tr");
            if (trs == null) return Array.Empty<VaultRow>();

            var results = new List<VaultRow>();
            foreach (var tr in trs)
            {
                var row = TryParseRow(tr);
                if (row != null) results.Add(row);
            }

            // The same row sometimes appears twice in nested tables — dedupe by id.
            return results
                .GroupBy(r => r.Id)
                .Select(g => g.First())
                .ToList();
        }

        private static VaultRow TryParseRow(HtmlNode tr)
        {
            var cells = tr.SelectNodes("./td");
            if (cells == null || cells.Count == 0) return null;

            // Locate the cell containing the title anchor.
            int titleIdx = -1;
            HtmlNode titleAnchor = null;
            int id = 0;

            for (int i = 0; i < cells.Count; i++)
            {
                foreach (var a in cells[i].SelectNodes(".//a[@href]") ?? Enumerable.Empty<HtmlNode>())
                {
                    var match = VaultIdHref.Match(a.GetAttributeValue("href", string.Empty).Trim());
                    if (!match.Success) continue;
                    titleIdx = i;
                    titleAnchor = a;
                    id = int.Parse(match.Groups["id"].Value);
                    break;
                }
                if (titleAnchor != null) break;
            }

            if (titleAnchor == null) return null;

            var title = HtmlEntity.DeEntitize(titleAnchor.InnerText).Trim();
            var unlicensed = cells[titleIdx].SelectSingleNode(".//*[contains(@title,'Unlicensed')]") != null;

            // System name is the cell BEFORE title, if any. Listing pages don't have one.
            string systemName = null;
            if (titleIdx > 0)
                systemName = HtmlEntity.DeEntitize(cells[titleIdx - 1].InnerText).Trim();

            // Cells after title: [Regions][Version][Languages][Rating?]
            var regions = ExtractRegions(SafeCell(cells, titleIdx + 1));
            var version = NormalizeBlank(SafeText(cells, titleIdx + 2));
            var languages = NormalizeBlank(SafeText(cells, titleIdx + 3));

            return new VaultRow
            {
                Id = id,
                SystemName = NormalizeBlank(systemName),
                Title = title,
                Regions = regions,
                Version = version,
                Languages = languages,
                IsUnlicensed = unlicensed,
                DetailUrl = BaseUrl + "/vault/" + id,
            };
        }

        private static IReadOnlyList<string> ExtractRegions(HtmlNode cell)
        {
            if (cell == null) return Array.Empty<string>();
            var imgs = cell.SelectNodes(".//img[@title]");
            if (imgs == null) return Array.Empty<string>();
            return imgs.Select(i => i.GetAttributeValue("title", string.Empty))
                       .Where(t => !string.IsNullOrEmpty(t))
                       .ToList();
        }

        private static HtmlNode SafeCell(HtmlNodeCollection cells, int i) =>
            (i >= 0 && i < cells.Count) ? cells[i] : null;

        private static string SafeText(HtmlNodeCollection cells, int i)
        {
            var c = SafeCell(cells, i);
            return c == null ? null : HtmlEntity.DeEntitize(c.InnerText).Trim();
        }

        private static string NormalizeBlank(string s) =>
            string.IsNullOrWhiteSpace(s) || s == "-" ? null : s;
    }
}
