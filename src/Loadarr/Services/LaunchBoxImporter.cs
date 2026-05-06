using System;
using System.IO;
using System.Linq;
using Unbroken.LaunchBox.Plugins;
using Unbroken.LaunchBox.Plugins.Data;

namespace Loadarr.Services
{
    /// <summary>
    /// Adds a downloaded ROM to the LaunchBox database via the plugin API.
    ///
    /// LaunchBox's data model: IDataManager owns games, platforms, emulators.
    /// AddNewGame returns an IGame; we set Title/Platform/ApplicationPath/EmulatorId,
    /// then call Save(true) ONCE so changes are persisted to LaunchBox's XML.
    /// </summary>
    internal sealed class LaunchBoxImporter
    {
        public static readonly LaunchBoxMetadataLookup MetadataLookup = new LaunchBoxMetadataLookup();

        public static void RefreshUi() => PluginHelper.LaunchBoxMainViewModel?.RefreshData();

        public sealed class ImportRequest
        {
            public string Title { get; set; }
            public string PlatformName { get; set; }
            public string RomFilePath { get; set; }
            public string Source { get; set; }
            public string Region { get; set; }
            public string Version { get; set; }
            public string Notes { get; set; }
        }

        public IGame Import(ImportRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.RomFilePath) || !File.Exists(req.RomFilePath))
                throw new FileNotFoundException("ROM file not found", req.RomFilePath);

            var dm = PluginHelper.DataManager
                ?? throw new InvalidOperationException(
                    "PluginHelper.DataManager is null — Loadarr must run inside LaunchBox.");

            var platformName = string.IsNullOrWhiteSpace(req.PlatformName) ? "Unknown" : req.PlatformName.Trim();
            var platform = dm.GetPlatformByName(platformName) ?? dm.AddNewPlatform(platformName);

            var title = string.IsNullOrWhiteSpace(req.Title)
                ? Path.GetFileNameWithoutExtension(req.RomFilePath)
                : req.Title.Trim();

            Log.Info("Importer: title=\"" + title + "\" platform=\"" + platform.Name + "\"");
            Log.Info("Importer: ApplicationPath = \"" + req.RomFilePath + "\"");
            Log.Info("Importer: file exists = " + File.Exists(req.RomFilePath)
                + ", size = " + (File.Exists(req.RomFilePath) ? new FileInfo(req.RomFilePath).Length.ToString("n0") : "n/a") + " bytes");

            var game = dm.AddNewGame(title);
            game.Title = title;
            game.Platform = platform.Name;
            game.ApplicationPath = req.RomFilePath;
            game.Source = req.Source;
            game.Region = req.Region;
            game.Version = req.Version;
            game.DateAdded = DateTime.Now;

            // Enrich from LaunchBox's local metadata.xml so the entry carries the
            // same DatabaseID / dates / credits as a GUI-selected database match.
            Log.Info("Importer: looking up metadata for \"" + title + "\" / \"" + platform.Name + "\"");
            var meta = MetadataLookup.Find(title, platform.Name);
            if (meta != null)
            {
                var applied = new System.Collections.Generic.List<string>();
                if (meta.DatabaseId.HasValue)         { game.LaunchBoxDbId = meta.DatabaseId;         applied.Add("DbId=" + meta.DatabaseId); }
                if (meta.ReleaseDate.HasValue)        { game.ReleaseDate = meta.ReleaseDate;          applied.Add("ReleaseDate"); }
                if (meta.ReleaseYear.HasValue)        { game.ReleaseYear = meta.ReleaseYear;          applied.Add("ReleaseYear=" + meta.ReleaseYear); }
                if (meta.CommunityRating.HasValue)    { game.CommunityStarRating = meta.CommunityRating.Value; applied.Add("Rating=" + meta.CommunityRating); }
                if (!string.IsNullOrEmpty(meta.Developer))    { game.Developer = meta.Developer;     applied.Add("Developer"); }
                if (!string.IsNullOrEmpty(meta.Publisher))    { game.Publisher = meta.Publisher;     applied.Add("Publisher"); }
                if (!string.IsNullOrEmpty(meta.Genres))       { game.GenresString = meta.Genres;     applied.Add("Genres"); }
                if (!string.IsNullOrEmpty(meta.WikipediaUrl)) { game.WikipediaUrl = meta.WikipediaUrl; applied.Add("WikipediaUrl"); }
                Log.Info("Importer: applied [" + string.Join(", ", applied) + "]");
            }
            else
            {
                Log.Info("Importer: no metadata match — leaving fields blank.");
            }

            game.Notes = string.IsNullOrWhiteSpace(meta?.Overview)
                ? req.Notes
                : req.Notes + "\n\n" + meta.Overview;

            // Best-effort: attach the first emulator that supports this platform, if any.
            var allEmulators = dm.GetAllEmulators() ?? Array.Empty<IEmulator>();
            Log.Info("Importer: scanning " + allEmulators.Length + " configured emulator(s) for platform \"" + platform.Name + "\"");
            IEmulator emulator = null;
            foreach (var e in allEmulators)
            {
                var supported = e.GetAllEmulatorPlatforms() ?? Array.Empty<IEmulatorPlatform>();
                var platforms = string.Join(", ", supported.Select(ep => ep.Platform));
                var matches = supported.Any(ep => string.Equals(ep.Platform, platform.Name, StringComparison.OrdinalIgnoreCase));
                Log.Info("  emulator Id=" + e.Id + " supports [" + platforms + "]" + (matches ? " — MATCH" : ""));
                if (matches && emulator == null) emulator = e;
            }
            if (emulator != null)
            {
                game.EmulatorId = emulator.Id;
                Log.Info("Importer: linked emulator Id=" + emulator.Id);
            }
            else
            {
                Log.Warn("Importer: NO emulator matched platform \"" + platform.Name + "\" — game will be unlaunchable until you assign one in LaunchBox.");
            }

            dm.Save(true);

            // Tell LaunchBox's UI to repopulate so the new platform/game appear
            // without requiring a restart. Null when running inside Big Box.
            PluginHelper.LaunchBoxMainViewModel?.RefreshData();

            return game;
        }
    }
}
