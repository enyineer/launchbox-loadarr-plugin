using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Loadarr.Sources.VimmsLair.Models;

namespace Loadarr.Sources.VimmsLair.Parsing
{
    /// <summary>
    /// Parses a single Vault detail page.
    ///
    /// The page exposes everything we need in two places:
    ///
    /// 1. A &lt;form id="dl_form" action="//dl3.vimm.net/" method="POST"&gt; with a
    ///    hidden mediaId input. submitDL() flips the method to GET on submit, so
    ///    in practice it is a GET to {action}?mediaId=N.
    ///
    /// 2. A JS literal `let media = [...];` whose entries hold the canonical
    ///    file metadata (mediaId, base64-encoded GoodTitle filename, size in
    ///    bytes, GoodHash CRC32, GoodMd5, GoodSha1, SortOrder for disc number,
    ///    Version). Multi-disc games have one entry per disc.
    ///
    /// We prefer the JS array because it's structured JSON; the form action
    /// gives us the (possibly-rotating) download host.
    /// </summary>
    public sealed class DetailPageParser
    {
        private static readonly Regex MediaArrayPattern = new Regex(
            @"(?:^|[\s;{(])(?:var|let|const)?\s*media\s*=\s*(?<arr>\[[\s\S]*?\])\s*;",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly JsonSerializerOptions JsonOpts =
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public VaultItem Parse(string html, int detailId, string detailUrl = null)
        {
            if (string.IsNullOrEmpty(html))
                throw new ArgumentException("Empty HTML.", nameof(html));

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var item = new VaultItem
            {
                Id = detailId,
                DetailUrl = detailUrl ?? $"https://vimm.net/vault/{detailId}",
                Title = ExtractTitle(doc),
                SystemName = ExtractSystemName(doc),
                Regions = ExtractRegions(doc),
                Year = ExtractYear(doc),
                Media = ExtractMedia(html),
                DownloadEndpoint = ExtractDownloadEndpoint(doc)
            };

            return item;
        }

        // ---- title --------------------------------------------------------

        private static string ExtractTitle(HtmlDocument doc)
        {
            // Page <title> is "Vimm's Lair: <title>"
            var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText;
            if (!string.IsNullOrEmpty(title))
            {
                title = HtmlEntity.DeEntitize(title);
                var idx = title.IndexOf(": ", StringComparison.Ordinal);
                if (idx >= 0) title = title.Substring(idx + 2);
                return title.Trim();
            }
            // fall back to first h1
            var h1 = doc.DocumentNode.SelectSingleNode("//h1");
            return h1 != null ? HtmlEntity.DeEntitize(h1.InnerText).Trim() : null;
        }

        private static string ExtractSystemName(HtmlDocument doc)
        {
            // Breadcrumb-style: <a href="/vault/SNES">Super Nintendo</a>
            var node = doc.DocumentNode.SelectSingleNode(
                "//a[starts-with(@href,'/vault/') and not(contains(@href,'?'))" +
                " and not(translate(substring(@href,8,1),'0123456789','')='')]");
            return node != null ? HtmlEntity.DeEntitize(node.InnerText).Trim() : null;
        }

        private static IReadOnlyList<string> ExtractRegions(HtmlDocument doc)
        {
            // The first <table class="rounded ..."> on the page is the metadata
            // panel; the Region row contains flag <img title="USA"> tags.
            var imgs = doc.DocumentNode.SelectNodes(
                "//table[contains(@class,'rounded')][1]//tr[td[1]/text()[contains(.,'Region')]]//img[@title]");
            if (imgs == null) return Array.Empty<string>();
            return imgs.Select(i => i.GetAttributeValue("title", string.Empty))
                       .Where(t => !string.IsNullOrEmpty(t))
                       .ToList();
        }

        private static string ExtractYear(HtmlDocument doc)
        {
            var td = doc.DocumentNode.SelectSingleNode(
                "//table[contains(@class,'rounded')][1]//tr[td[1][normalize-space()='Year']]/td[3]");
            return td != null ? HtmlEntity.DeEntitize(td.InnerText).Trim() : null;
        }

        // ---- media[] ------------------------------------------------------

        private static IReadOnlyList<MediaEntry> ExtractMedia(string rawHtml)
        {
            var match = MediaArrayPattern.Match(rawHtml);
            if (!match.Success)
                return Array.Empty<MediaEntry>();

            var json = match.Groups["arr"].Value;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var list = new List<MediaEntry>();
                foreach (var el in doc.RootElement.EnumerateArray())
                    list.Add(MediaFromJson(el));
                return list
                    .OrderBy(m => m.SortOrder)
                    .ThenBy(m => m.Id)
                    .ToList();
            }
            catch (JsonException)
            {
                return Array.Empty<MediaEntry>();
            }
        }

        private static MediaEntry MediaFromJson(JsonElement el)
        {
            return new MediaEntry
            {
                Id = ReadInt(el, "ID"),
                SortOrder = ReadInt(el, "SortOrder"),
                Version = ReadString(el, "Version") ?? ReadString(el, "VersionString"),
                FileName = DecodeBase64Title(ReadString(el, "GoodTitle")),
                ZippedBytes = ReadLongFromStringOrNumber(el, "Zipped"),
                Crc = NormalizeHash(ReadString(el, "GoodHash")),
                Md5 = NormalizeHash(ReadString(el, "GoodMd5")),
                Sha1 = NormalizeHash(ReadString(el, "GoodSha1")),
            };
        }

        private static int ReadInt(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var v)) return 0;
            return v.ValueKind switch
            {
                JsonValueKind.Number => v.TryGetInt32(out var i) ? i : 0,
                JsonValueKind.String => int.TryParse(v.GetString(), out var p) ? p : 0,
                _ => 0,
            };
        }

        private static long ReadLongFromStringOrNumber(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var v)) return 0;
            return v.ValueKind switch
            {
                JsonValueKind.Number => v.TryGetInt64(out var i) ? i : 0,
                JsonValueKind.String => long.TryParse(v.GetString(), out var p) ? p : 0,
                _ => 0,
            };
        }

        private static string ReadString(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var v)) return null;
            return v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        private static string DecodeBase64Title(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(base64)); }
            catch (FormatException) { return null; }
        }

        private static string NormalizeHash(string s) =>
            string.IsNullOrEmpty(s) ? null : s.Trim().ToLowerInvariant();

        // ---- download endpoint -------------------------------------------

        private static string ExtractDownloadEndpoint(HtmlDocument doc)
        {
            var form = doc.DocumentNode.SelectSingleNode("//form[@id='dl_form']")
                    ?? doc.DocumentNode.SelectSingleNode(
                           "//form[contains(@action,'dl') and .//input[@name='mediaId']]");
            if (form == null) return null;

            var action = form.GetAttributeValue("action", null);
            if (string.IsNullOrEmpty(action)) return null;

            // Protocol-relative: //dl3.vimm.net/ -> https://dl3.vimm.net/
            if (action.StartsWith("//", StringComparison.Ordinal))
                return "https:" + action;
            // Absolute
            if (action.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                action.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return action;
            // Relative (rare)
            return "https://vimm.net" + (action.StartsWith("/") ? action : "/" + action);
        }
    }
}
