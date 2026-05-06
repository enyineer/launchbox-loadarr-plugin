using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Loadarr.Sources.VimmsLair.Models;
using Loadarr.Sources.VimmsLair.Tests.Fixtures;
using Xunit;

namespace Loadarr.Sources.VimmsLair.Tests
{
    public class VimmsLairClientTests
    {
        // ---- url composition (pure logic, no I/O) ----

        [Fact]
        public void BuildDownloadUrl_AppendsMediaId()
        {
            var url = VimmsLairClient.BuildDownloadUrl("https://dl3.vimm.net/", 4008);
            Assert.Equal("https://dl3.vimm.net/?mediaId=4008", url.ToString());
        }

        [Fact]
        public void BuildDownloadUrl_AppendsMediaIdWithExistingQuery()
        {
            var url = VimmsLairClient.BuildDownloadUrl("https://dl3.vimm.net/?foo=1", 99);
            Assert.Equal("https://dl3.vimm.net/?foo=1&mediaId=99", url.ToString());
        }

        [Fact]
        public void BuildDownloadUrl_RejectsBadInputs()
        {
            Assert.Throws<ArgumentException>(() =>
                VimmsLairClient.BuildDownloadUrl("", 1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                VimmsLairClient.BuildDownloadUrl("https://x", 0));
        }

        // ---- Content-Disposition handling ----

        [Fact]
        public void TryGetDispositionFileName_PrefersFileNameStar()
        {
            var cd = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "fallback.bin",
                FileNameStar = "real.zip"
            };
            Assert.Equal("real.zip", VimmsLairClient.TryGetDispositionFileName(cd));
        }

        [Fact]
        public void TryGetDispositionFileName_StripsQuotes()
        {
            var cd = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "\"Classic NES Series - The Legend of Zelda (USA, Europe).zip\""
            };
            Assert.Equal(
                "Classic NES Series - The Legend of Zelda (USA, Europe).zip",
                VimmsLairClient.TryGetDispositionFileName(cd));
        }

        [Fact]
        public void TryGetDispositionFileName_NullSafe()
        {
            Assert.Null(VimmsLairClient.TryGetDispositionFileName(null));
        }

        // ---- end-to-end client flow against a stub HttpMessageHandler ----

        [Fact]
        public async Task SearchAsync_HitsListEndpoint_AndReturnsRows()
        {
            var stub = new StubHandler();
            stub.OnRequest = req =>
            {
                Assert.Equal("vimm.net", req.RequestUri.Host);
                Assert.Equal("/vault/", req.RequestUri.AbsolutePath);
                Assert.Contains("p=list", req.RequestUri.Query);
                Assert.Contains("q=zelda", req.RequestUri.Query);
                return Reply(FixtureLoader.Load("search_zelda.html"));
            };

            var client = new VimmsLairClient(new HttpClient(stub));
            var rows = await client.SearchAsync("zelda");

            Assert.NotEmpty(rows);
        }

        [Fact]
        public async Task ListSystemAsync_BuildsLetterPath()
        {
            var stub = new StubHandler
            {
                OnRequest = req =>
                {
                    Assert.Equal("/vault/SNES/A", req.RequestUri.AbsolutePath);
                    return Reply(FixtureLoader.Load("list_snes_a.html"));
                }
            };
            var client = new VimmsLairClient(new HttpClient(stub));
            var rows = await client.ListSystemAsync("SNES", letter: 'A');
            Assert.NotEmpty(rows);
            // Backfilled from the systemCode argument.
            Assert.All(rows, r => Assert.Equal("SNES", r.SystemName));
        }

        [Fact]
        public async Task GetItemAsync_ParsesSingleDiscPage()
        {
            var stub = new StubHandler
            {
                OnRequest = _ => Reply(FixtureLoader.Load("detail_zelda_gba.html"))
            };
            var client = new VimmsLairClient(new HttpClient(stub));

            var item = await client.GetItemAsync(5173);

            Assert.Single(item.Media);
            Assert.Equal(4008, item.Media[0].Id);
            Assert.Equal("https://vimm.net/vault/5173", item.DetailUrl);
            Assert.NotNull(item.DownloadEndpoint);
        }

        [Fact]
        public async Task GetItemAsync_ParsesMultiDiscPage()
        {
            var stub = new StubHandler
            {
                OnRequest = _ => Reply(FixtureLoader.Load("detail_ff7_ps1_3disc.html"))
            };
            var client = new VimmsLairClient(new HttpClient(stub));

            var item = await client.GetItemAsync(2826);
            Assert.Equal(3, item.Media.Count);
        }

        [Fact]
        public async Task OpenDownloadAsync_SendsRefererAndMediaId()
        {
            HttpRequestMessage captured = null;
            var stub = new StubHandler
            {
                OnRequest = req =>
                {
                    captured = req;
                    var resp = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
                    };
                    resp.Content.Headers.ContentDisposition =
                        ContentDispositionHeaderValue.Parse("attachment; filename=\"foo.zip\"");
                    return resp;
                }
            };
            var client = new VimmsLairClient(new HttpClient(stub));
            var item = new VaultItem
            {
                Id = 5173,
                DetailUrl = "https://vimm.net/vault/5173",
                DownloadEndpoint = "https://dl3.vimm.net/"
            };

            using var resp = await client.OpenDownloadAsync(item, 4008);

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.NotNull(captured);
            Assert.Equal("https://dl3.vimm.net/?mediaId=4008", captured.RequestUri.ToString());
            Assert.Equal("https://vimm.net/vault/5173", captured.Headers.Referrer.ToString());
        }

        // ---- helpers ----

        private static HttpResponseMessage Reply(string body) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };

        private sealed class StubHandler : HttpMessageHandler
        {
            public Func<HttpRequestMessage, HttpResponseMessage> OnRequest { get; set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(OnRequest(request));
        }
    }
}
