using System.Linq;
using Loadarr.Sources.VimmsLair.Parsing;
using Loadarr.Sources.VimmsLair.Tests.Fixtures;
using Xunit;

namespace Loadarr.Sources.VimmsLair.Tests
{
    public class DetailPageParserTests
    {
        private readonly DetailPageParser _parser = new();

        // ---- single-disc fixture (Zelda GBA, id=5173) -------------------

        [Fact]
        public void Parse_ZeldaGba_HasOneMediaEntry()
        {
            var html = FixtureLoader.Load("detail_zelda_gba.html");
            var item = _parser.Parse(html, 5173);
            Assert.Single(item.Media);
        }

        [Fact]
        public void Parse_ZeldaGba_MediaIdIs4008()
        {
            var html = FixtureLoader.Load("detail_zelda_gba.html");
            var item = _parser.Parse(html, 5173);
            Assert.Equal(4008, item.Media[0].Id);
        }

        [Fact]
        public void Parse_ZeldaGba_FilenameDecoded()
        {
            // GoodTitle base64 decodes to:
            //   "Classic NES Series - The Legend of Zelda (USA, Europe).gba"
            var html = FixtureLoader.Load("detail_zelda_gba.html");
            var item = _parser.Parse(html, 5173);

            Assert.Equal(
                "Classic NES Series - The Legend of Zelda (USA, Europe).gba",
                item.Media[0].FileName);
        }

        [Fact]
        public void Parse_ZeldaGba_HashesPopulated()
        {
            var html = FixtureLoader.Load("detail_zelda_gba.html");
            var item = _parser.Parse(html, 5173);
            var m = item.Media[0];

            Assert.Equal("6d49cabf", m.Crc);
            Assert.Equal("df78967cea5f519d6477fe68edbfbb77", m.Md5);
            Assert.Equal("28aac26365bf41ba84e67f97e98d15c4678cb99d", m.Sha1);
        }

        [Fact]
        public void Parse_ZeldaGba_ZippedSizeIs1013()
        {
            var html = FixtureLoader.Load("detail_zelda_gba.html");
            var item = _parser.Parse(html, 5173);
            Assert.Equal(1013L, item.Media[0].ZippedBytes);
        }

        [Fact]
        public void Parse_ZeldaGba_DownloadEndpointResolved()
        {
            // Fixture has action="//dl3.vimm.net/" — should be promoted to https.
            var html = FixtureLoader.Load("detail_zelda_gba.html");
            var item = _parser.Parse(html, 5173);

            Assert.NotNull(item.DownloadEndpoint);
            Assert.StartsWith("https://", item.DownloadEndpoint);
            Assert.Contains("vimm.net", item.DownloadEndpoint);
        }

        [Fact]
        public void Parse_ZeldaGba_RegionsContainUsaAndEurope()
        {
            var html = FixtureLoader.Load("detail_zelda_gba.html");
            var item = _parser.Parse(html, 5173);
            Assert.Contains("USA", item.Regions);
            Assert.Contains("Europe", item.Regions);
        }

        [Fact]
        public void Parse_ZeldaGba_TitleAndYear()
        {
            var html = FixtureLoader.Load("detail_zelda_gba.html");
            var item = _parser.Parse(html, 5173);

            Assert.Contains("Legend of Zelda", item.Title);
            Assert.Equal("2004", item.Year);
        }

        // ---- multi-disc fixture (Final Fantasy VII PS1, id=2826) --------

        [Fact]
        public void Parse_FF7_HasThreeDiscs()
        {
            var html = FixtureLoader.Load("detail_ff7_ps1_3disc.html");
            var item = _parser.Parse(html, 2826);
            Assert.Equal(3, item.Media.Count);
        }

        [Fact]
        public void Parse_FF7_DiscsOrderedBySortOrder()
        {
            var html = FixtureLoader.Load("detail_ff7_ps1_3disc.html");
            var item = _parser.Parse(html, 2826);
            Assert.Equal(new[] { 1, 2, 3 }, item.Media.Select(m => m.SortOrder).ToArray());
        }

        [Fact]
        public void Parse_FF7_DiscIdsAreDistinctAndPositive()
        {
            var html = FixtureLoader.Load("detail_ff7_ps1_3disc.html");
            var item = _parser.Parse(html, 2826);

            Assert.All(item.Media, m => Assert.True(m.Id > 0));
            Assert.Equal(item.Media.Count, item.Media.Select(m => m.Id).Distinct().Count());
        }

        // ---- error handling ---------------------------------------------

        [Fact]
        public void Parse_HtmlWithoutMediaArray_ReturnsEmptyMedia()
        {
            // Home page has no media[] declaration.
            var html = FixtureLoader.Load("home.html");
            var item = _parser.Parse(html, 1);
            Assert.Empty(item.Media);
        }
    }
}
