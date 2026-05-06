using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Loadarr.Services
{
    public sealed class DownloadProgress
    {
        public long BytesDownloaded { get; set; }
        public long? TotalBytes { get; set; }
        public double? Percent =>
            TotalBytes.HasValue && TotalBytes.Value > 0
                ? (double)BytesDownloaded / TotalBytes.Value * 100.0
                : (double?)null;
    }

    internal sealed class DownloadService
    {
        private readonly HttpClient _http;

        public DownloadService(HttpClient http) => _http = http;

        public async Task<string> DownloadAsync(
            string url,
            string fileName,
            string targetDir,
            IDictionary<string, string> headers,
            IProgress<DownloadProgress> progress,
            CancellationToken ct)
        {
            Directory.CreateDirectory(targetDir);
            var safe = StringNormalize.SafeFileName(fileName);
            var dest = Path.Combine(targetDir, safe);

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (headers != null)
                foreach (var kv in headers)
                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

            using var resp = await _http
                .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength;
            using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long read = 0;
            int n;
            var report = new DownloadProgress { TotalBytes = total };
            var lastReport = DateTime.UtcNow;

            while ((n = await src.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer, 0, n, ct).ConfigureAwait(false);
                read += n;
                if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 200)
                {
                    report.BytesDownloaded = read;
                    progress?.Report(report);
                    lastReport = DateTime.UtcNow;
                }
            }
            report.BytesDownloaded = read;
            progress?.Report(report);
            return dest;
        }
    }
}
