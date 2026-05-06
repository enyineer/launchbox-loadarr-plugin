using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Loadarr.Services
{
    /// <summary>
    /// Downloads canonical LaunchBox images (front box, clear logo, gameplay
    /// screenshot) for an imported game. Same mechanism LaunchBox uses for
    /// "Tools → Download Images": resolve image rows from the local
    /// LaunchBox.Metadata.db and pull the file from the LaunchBox CDN, where
    ///   https://images.launchbox-app.com/&lt;FileName&gt;
    /// is the byte-for-byte image stored locally as
    ///   &lt;LaunchBox&gt;\Images\&lt;Platform&gt;\&lt;Type&gt;\&lt;SafeTitle&gt;&lt;ext&gt;.
    /// </summary>
    internal sealed class LaunchBoxImageDownloader
    {
        private const string CdnBase = "https://images.launchbox-app.com/";

        private readonly HttpClient _http;

        public LaunchBoxImageDownloader(HttpClient http)
        {
            _http = http;
        }

        /// <summary>
        /// Download a user-selected list of images. Multiple images of the same
        /// type get suffixed with -01, -02 to coexist on disk under
        /// LaunchBox's per-type folder layout.
        /// </summary>
        public async Task DownloadSelectedAsync(string title, string platform,
            IReadOnlyList<LaunchBoxMetadataLookup.GameImage> selected, CancellationToken ct)
        {
            if (selected == null || selected.Count == 0)
            {
                Log.Info("ImageDownloader: nothing selected.");
                return;
            }

            var lbRoot = ResolveLaunchBoxRoot();
            if (lbRoot == null)
            {
                Log.Warn("ImageDownloader: cannot resolve LaunchBox root.");
                return;
            }

            var safeTitle = StringNormalize.SafeFileName(title ?? "Unknown");
            var safePlatform = StringNormalize.SafeFileName(string.IsNullOrEmpty(platform) ? "Unknown" : platform);

            // Group by Type so we can suffix duplicates within the same type.
            foreach (var typeGroup in selected.GroupBy(i => i.Type, StringComparer.OrdinalIgnoreCase))
            {
                var entries = typeGroup.ToList();
                int index = 0;

                foreach (var img in entries)
                {
                    ct.ThrowIfCancellationRequested();
                    index++;

                    var ext = Path.GetExtension(img.FileName);
                    if (string.IsNullOrEmpty(ext)) ext = ".jpg";

                    var typeDir = Path.Combine(lbRoot, "Images", safePlatform, typeGroup.Key);
                    var fileName = entries.Count == 1
                        ? safeTitle + ext
                        : safeTitle + "-" + index.ToString("D2") + ext;
                    var localPath = Path.Combine(typeDir, fileName);

                    if (File.Exists(localPath))
                    {
                        Log.Info("ImageDownloader: keeping existing \"" + localPath + "\".");
                        continue;
                    }

                    try
                    {
                        Directory.CreateDirectory(typeDir);
                        var url = CdnBase + Uri.EscapeUriString(img.FileName);
                        Log.Info("ImageDownloader: GET " + url + " -> " + localPath
                            + " (region=" + (img.Region ?? "<null>") + ")");

                        using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                        {
                            resp.EnsureSuccessStatusCode();
                            using (var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var fs = File.Create(localPath))
                            {
                                await stream.CopyToAsync(fs, 81920, ct).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("ImageDownloader: failed to download \"" + img.FileName + "\"", ex);
                        try { if (File.Exists(localPath)) File.Delete(localPath); } catch { }
                    }
                }
            }
        }

        private static string ResolveLaunchBoxRoot()
        {
            try
            {
                var asmPath = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(asmPath)) return null;
                var pluginDir = Path.GetDirectoryName(asmPath);
                var pluginsDir = Path.GetDirectoryName(pluginDir);
                return Path.GetDirectoryName(pluginsDir);
            }
            catch
            {
                return null;
            }
        }
    }
}
