using System.Linq;
using Loadarr.Sources.VimmsLair.Parsing;
using Loadarr.Sources.VimmsLair.Tests.Fixtures;
using Xunit;

namespace Loadarr.Sources.VimmsLair.Tests
{
    public class VaultListingParserTests
    {
        private readonly VaultListingParser _parser = new();

        [Fact]
        public void Parse_ZeldaSearch_ReturnsManyResults()
        {
            var html = FixtureLoader.Load("search_zelda.html");
            var rows = _parser.Parse(html);

            Assert.NotEmpty(rows);
            // The fixture has roughly 100+ Zelda hits across systems.
            Assert.True(rows.Count > 50, $"expected >50 rows, got {rows.Count}");
        }

        [Fact]
        public void Parse_ZeldaSearch_AllRowsHavePositiveId()
        {
            var html = FixtureLoader.Load("search_zelda.html");
            var rows = _parser.Parse(html);

            Assert.All(rows, r => Assert.True(r.Id > 0, $"row '{r.Title}' has Id={r.Id}"));
        }

        [Fact]
        public void Parse_ZeldaSearch_RowsAreUniqueById()
        {
            var html = FixtureLoader.Load("search_zelda.html");
            var rows = _parser.Parse(html);
            Assert.Equal(rows.Count, rows.Select(r => r.Id).Distinct().Count());
        }

        [Fact]
        public void Parse_ZeldaSearch_KnownRowFound()
        {
            // The fixture contains "Classic NES Series: The Legend of Zelda" on GBA, id=5173.
            var html = FixtureLoader.Load("search_zelda.html");
            var rows = _parser.Parse(html);

            var classicNes = rows.FirstOrDefault(r => r.Id == 5173);
            Assert.NotNull(classicNes);
            Assert.Contains("Legend of Zelda", classicNes.Title);
            Assert.Equal("GBA", classicNes.SystemName);
            Assert.Contains("USA", classicNes.Regions);
            Assert.Contains("Europe", classicNes.Regions);
        }

        [Fact]
        public void Parse_ZeldaSearch_DetailUrlIsAbsolute()
        {
            var html = FixtureLoader.Load("search_zelda.html");
            var rows = _parser.Parse(html);
            Assert.All(rows, r => Assert.StartsWith("https://vimm.net/vault/", r.DetailUrl));
        }

        [Fact]
        public void Parse_ZeldaSearch_DetectsUnlicensedFlag()
        {
            // The "Action Replay Ultimate Codes for ... Twilight Princess" row is marked Unlicensed (id=39288).
            var html = FixtureLoader.Load("search_zelda.html");
            var rows = _parser.Parse(html);
            var unlic = rows.FirstOrDefault(r => r.Id == 39288);
            Assert.NotNull(unlic);
            Assert.True(unlic.IsUnlicensed);
        }

        [Fact]
        public void Parse_SnesLetterPage_ReturnsRows()
        {
            var html = FixtureLoader.Load("list_snes_a.html");
            var rows = _parser.Parse(html);

            Assert.NotEmpty(rows);
            // Per-system listing pages don't repeat the system name in each row.
            // VimmsLairClient.ListSystemAsync backfills it from the requested
            // systemCode; the parser itself leaves it null.
            Assert.All(rows, r => Assert.Null(r.SystemName));
        }

        [Fact]
        public void Parse_SnesLetterPage_AnchorsUseAbsoluteForm()
        {
            // First row on /SNES/A is "Aaahh!!! Real Monsters" with id=41064.
            var html = FixtureLoader.Load("list_snes_a.html");
            var rows = _parser.Parse(html);
            var row = rows.FirstOrDefault(r => r.Id == 41064);
            Assert.NotNull(row);
            Assert.Contains("Real Monsters", row.Title);
            Assert.Contains("Europe", row.Regions);
        }

        [Fact]
        public void Parse_HomePage_ReturnsNoGameRows()
        {
            // Vault home has "Atari 2600", "Nintendo", … as system links — those
            // are not numeric-id /vault/<digits> hrefs and should be filtered out.
            var html = FixtureLoader.Load("home.html");
            var rows = _parser.Parse(html);

            Assert.Empty(rows);
        }

        [Fact]
        public void Parse_NullOrEmpty_ReturnsEmpty()
        {
            Assert.Empty(_parser.Parse(null));
            Assert.Empty(_parser.Parse(""));
        }
    }
}
