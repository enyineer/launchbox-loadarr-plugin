using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Loadarr.Sources.VimmsLair.Models;
using Loadarr.Sources.VimmsLair.Parsing;

namespace Loadarr.Sources.VimmsLair
{
    /// <summary>
    /// HTTP-facing client for vimm.net (The Vault). Stateless apart from the
    /// caller-owned <see cref="HttpClient"/>.
    ///
    /// Public surface:
    ///   • <see cref="SearchAsync"/> — full-text search across all systems.
    ///   • <see cref="ListSystemAsync"/> — browse a single system (e.g. "SNES").
    ///   • <see cref="GetItemAsync"/> — load a detail page and return its media[].
    ///   • <see cref="BuildDownloadUrl"/> — compose the download URL for a mediaId.
    ///   • <see cref="OpenDownloadAsync"/> — start a streaming GET for a download.
    /// </summary>
    public sealed class VimmsLairClient
    {
        public const string DefaultUserAgent =
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_0) AppleWebKit/605.1.15 " +
            "(KHTML, like Gecko) Version/17.0 Safari/605.1.15 Loadarr/0.1";

        private static readonly Uri BaseUri = new Uri("https://vimm.net/");

        private readonly HttpClient _http;
        private readonly VaultListingParser _listing = new VaultListingParser();
        private readonly DetailPageParser _detail = new DetailPageParser();

        public VimmsLairClient(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            EnsureUserAgent(_http);
        }

        // ---- search / list ------------------------------------------------

        public async Task<IReadOnlyList<VaultRow>> SearchAsync(string query, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Query is required.", nameof(query));

            var url = $"/vault/?p=list&q={Uri.EscapeDataString(query.Trim())}";
            var html = await GetStringAsync(url, ct).ConfigureAwait(false);
            return _listing.Parse(html);
        }

        public async Task<IReadOnlyList<VaultRow>> ListSystemAsync(
            string systemCode,
            char? letter = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(systemCode))
                throw new ArgumentException("systemCode is required.", nameof(systemCode));

            var path = letter.HasValue
                ? $"/vault/{systemCode}/{char.ToUpperInvariant(letter.Value)}"
                : $"/vault/{systemCode}";

            var html = await GetStringAsync(path, ct).ConfigureAwait(false);
            var rows = _listing.Parse(html);

            // Per-system listings don't repeat the system name in each row;
            // fill it in from the caller's systemCode so consumers get a
            // uniform shape regardless of which endpoint was used.
            foreach (var r in rows)
                if (string.IsNullOrEmpty(r.SystemName)) r.SystemName = systemCode;
            return rows;
        }

        // ---- item detail --------------------------------------------------

        public async Task<VaultItem> GetItemAsync(int detailId, CancellationToken ct = default)
        {
            if (detailId <= 0) throw new ArgumentOutOfRangeException(nameof(detailId));

            var url = $"/vault/{detailId}";
            var html = await GetStringAsync(url, ct).ConfigureAwait(false);
            return _detail.Parse(html, detailId, new Uri(BaseUri, url).ToString());
        }

        // ---- download -----------------------------------------------------

        /// <summary>
        /// Composes the download URL for a given mediaId. Vimm rotates download
        /// hosts (dl3, download2, …); pass <see cref="VaultItem.DownloadEndpoint"/>
        /// for the host as it appeared on the detail page.
        /// </summary>
        public static Uri BuildDownloadUrl(string downloadEndpoint, int mediaId)
        {
            if (string.IsNullOrEmpty(downloadEndpoint))
                throw new ArgumentException("downloadEndpoint is required.", nameof(downloadEndpoint));
            if (mediaId <= 0) throw new ArgumentOutOfRangeException(nameof(mediaId));

            var sep = downloadEndpoint.Contains("?") ? "&" : "?";
            return new Uri(downloadEndpoint + sep + "mediaId=" + mediaId);
        }

        /// <summary>
        /// Opens a streaming GET for the download. Caller is responsible for
        /// disposing the response and writing the body to disk. The response's
        /// Content-Disposition header is the authoritative filename.
        /// </summary>
        public async Task<HttpResponseMessage> OpenDownloadAsync(
            VaultItem item,
            int mediaId,
            CancellationToken ct = default)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (string.IsNullOrEmpty(item.DownloadEndpoint))
                throw new InvalidOperationException("Item has no DownloadEndpoint.");

            var url = BuildDownloadUrl(item.DownloadEndpoint, mediaId);

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Referrer = new Uri(item.DetailUrl);
            req.Headers.AcceptEncoding.Clear();
            req.Headers.Accept.ParseAdd("*/*");

            return await _http
                .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Convenience wrapper: opens the download, follows it to disk, and returns
        /// the saved path. Filename is taken from Content-Disposition when present
        /// and falls back to <see cref="MediaEntry.FileName"/>.
        /// </summary>
        public async Task<string> DownloadAsync(
            VaultItem item,
            MediaEntry media,
            string targetDirectory,
            IProgress<long> bytesRead = null,
            CancellationToken ct = default)
        {
            if (media == null) throw new ArgumentNullException(nameof(media));
            Directory.CreateDirectory(targetDirectory);

            using var resp = await OpenDownloadAsync(item, media.Id, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var fileName = TryGetDispositionFileName(resp.Content.Headers.ContentDisposition)
                           ?? SafeFileName(media.FileName)
                           ?? $"vimm-{media.Id}.zip";
            fileName = SafeFileName(fileName);

            var path = Path.Combine(targetDirectory, fileName);

            using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                                           81920, useAsync: true);
            var buf = new byte[81920];
            long total = 0;
            int n;
            while ((n = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
                total += n;
                bytesRead?.Report(total);
            }
            return path;
        }

        // ---- helpers ------------------------------------------------------

        private async Task<string> GetStringAsync(string relative, CancellationToken ct)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUri, relative));
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        private static void EnsureUserAgent(HttpClient http)
        {
            if (http.DefaultRequestHeaders.UserAgent.Count == 0)
                http.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
        }

        internal static string TryGetDispositionFileName(ContentDispositionHeaderValue cd)
        {
            if (cd == null) return null;
            // FileNameStar is RFC 5987 (UTF-8), preferred when present.
            var raw = cd.FileNameStar ?? cd.FileName;
            if (string.IsNullOrEmpty(raw)) return null;
            return raw.Trim('"');
        }

        private static string SafeFileName(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var invalid = Path.GetInvalidFileNameChars();
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            return new string(chars).Trim();
        }
    }
}
