using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Loadarr.Sources.VimmsLair.Tests
{
    /// <summary>
    /// Hits the real vimm.net. Disabled by default to avoid hammering the site.
    /// Run with:
    ///   LOADARR_LIVE=1 dotnet test --filter "Category=Live"
    /// </summary>
    [Trait("Category", "Live")]
    public class LiveSiteTests : IClassFixture<LiveSiteTests.LiveFixture>
    {
        private readonly LiveFixture _fx;
        public LiveSiteTests(LiveFixture fx) => _fx = fx;

        public sealed class LiveFixture
        {
            public bool Enabled { get; }
            public VimmsLairClient Client { get; }

            public LiveFixture()
            {
                Enabled = Environment.GetEnvironmentVariable("LOADARR_LIVE") == "1";
                if (Enabled)
                {
                    var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd(VimmsLairClient.DefaultUserAgent);
                    Client = new VimmsLairClient(http);
                }
            }
        }

        private void SkipIfDisabled()
        {
            if (!_fx.Enabled)
                throw new Skipped("Set LOADARR_LIVE=1 to run live-site tests.");
        }

        [Fact]
        public async Task Search_Zelda_ReturnsRealResults()
        {
            SkipIfDisabled();
            var rows = await _fx.Client.SearchAsync("zelda");
            Assert.True(rows.Count > 20, $"got {rows.Count} rows");
            Assert.All(rows, r => Assert.True(r.Id > 0));
        }

        [Fact]
        public async Task GetItem_ZeldaGba_HasDownloadableMedia()
        {
            SkipIfDisabled();
            var item = await _fx.Client.GetItemAsync(5173);
            Assert.NotEmpty(item.Media);
            Assert.True(item.Media[0].Id > 0);
            Assert.NotNull(item.DownloadEndpoint);
        }

        [Fact]
        public async Task Download_ZeldaGba_ReturnsZipWithExpectedName()
        {
            SkipIfDisabled();

            var item = await _fx.Client.GetItemAsync(5173);
            var media = item.Media[0];

            using var resp = await _fx.Client.OpenDownloadAsync(item, media.Id);
            Assert.True(resp.IsSuccessStatusCode, $"download HTTP {(int)resp.StatusCode}");

            var cd = resp.Content.Headers.ContentDisposition;
            Assert.NotNull(cd);

            var filename = VimmsLairClient.TryGetDispositionFileName(cd);
            Assert.NotNull(filename);
            Assert.EndsWith(".zip", filename, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Zelda", filename, StringComparison.OrdinalIgnoreCase);

            // Sanity-check the first few bytes look like a ZIP (PK\x03\x04).
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            Assert.True(bytes.Length > 1000, $"only got {bytes.Length} bytes");
            Assert.Equal((byte)'P', bytes[0]);
            Assert.Equal((byte)'K', bytes[1]);
        }

        [Fact]
        public async Task Download_AndExtract_RoundTrip()
        {
            SkipIfDisabled();

            var item = await _fx.Client.GetItemAsync(5173);
            var media = item.Media[0];

            var tmp = Path.Combine(Path.GetTempPath(), "loadarr-vimm-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var path = await _fx.Client.DownloadAsync(item, media, tmp);
                var info = new FileInfo(path);
                Assert.True(info.Exists);
                Assert.True(info.Length > 100_000, $"file is only {info.Length} bytes");

                using var fs = File.OpenRead(path);
                using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Read);
                Assert.NotEmpty(zip.Entries);
                // Vimm zips contain a single .gba/.smc/etc. file matching the title.
                Assert.Contains(zip.Entries, e => e.Name.EndsWith(".gba", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
            }
        }

        // ---- Skipped exception (xunit doesn't ship one in 2.x; tests using SkipIfDisabled
        //      raise this and it's treated as a test failure unless LOADARR_LIVE=1).
        //      We keep it explicit so accidental CI runs surface the gate clearly.

        private sealed class Skipped : Exception
        {
            public Skipped(string message) : base("[skipped] " + message) { }
        }
    }
}
