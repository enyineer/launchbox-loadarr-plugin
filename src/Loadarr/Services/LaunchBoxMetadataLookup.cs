using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Loadarr.Services
{
    /// <summary>
    /// Reads LaunchBox's metadata SQLite database (Metadata\LaunchBox.Metadata.db,
    /// introduced in modern LaunchBox versions; replaces the older metadata.xml)
    /// and looks up canonical game metadata by (Title, Platform).
    ///
    /// Same DB the in-app database picker queries — so a matched game carries the
    /// real DatabaseID, ReleaseDate, Developer, Publisher, Genres, etc. as if a
    /// user had selected the entry from LaunchBox's GUI.
    ///
    /// Schema (Games table): DatabaseID, Name, CompareName, Platform, ReleaseDate,
    /// ReleaseYear, Overview, MaxPlayers, ReleaseType, VideoURL, CommunityRating,
    /// WikipediaURL, ESRB, Genres, Developer, Publisher, …
    ///
    /// Lookup is three-pass, per import (no preload):
    ///   1. Exact Name match on the requested platform (NOCASE), decorations stripped.
    ///   2. GameAlternateTitles match on the requested platform — handles regional
    ///      variants like "Mario Story" → Paper Mario (Japan).
    ///   3. Prefix LIKE on the requested platform, shortest match wins.
    /// </summary>
    internal sealed class LaunchBoxMetadataLookup
    {
        public sealed class Match
        {
            public int? DatabaseId;
            public string Name;
            public string Platform;
            public string Developer;
            public string Publisher;
            public string Genres;
            public string Overview;
            public string WikipediaUrl;
            public DateTime? ReleaseDate;
            public int? ReleaseYear;
            public float? CommunityRating;
        }

        public sealed class GameImage
        {
            public string FileName;
            public string Type;
            public string Region;
        }

        public System.Collections.Generic.IReadOnlyList<GameImage> FindImages(int databaseId)
        {
            EnsureResolved();
            if (_dbPath == null) return Array.Empty<GameImage>();

            try
            {
                using (var conn = new SqliteConnection("Data Source=" + _dbPath + ";Mode=ReadOnly"))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT FileName, Type, Region FROM GameImages WHERE DatabaseId = $id";
                        cmd.Parameters.AddWithValue("$id", databaseId);
                        var list = new System.Collections.Generic.List<GameImage>();
                        using (var r = cmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                list.Add(new GameImage
                                {
                                    FileName = r.GetString(0),
                                    Type = r.GetString(1),
                                    Region = r.IsDBNull(2) ? null : r.GetString(2),
                                });
                            }
                        }
                        Log.Info("FindImages(" + databaseId + "): " + list.Count + " image rows.");
                        return list;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("FindImages query failed", ex);
                return Array.Empty<GameImage>();
            }
        }

        private static readonly Regex DecorationRx =
            new Regex(@"\([^)]*\)|\[[^\]]*\]", RegexOptions.Compiled);

        private const string SelectColumns =
            "DatabaseID, Name, Platform, Developer, Publisher, Genres, Overview, " +
            "WikipediaURL, ReleaseDate, ReleaseYear, CommunityRating";

        private string _dbPath;
        private bool _resolved;
        private readonly object _lock = new object();

        public Match Find(string title, string platform)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                Log.Warn("MetadataLookup.Find called with empty title.");
                return null;
            }

            EnsureResolved();
            if (_dbPath == null) return null;

            var clean = StripDecorations(title);
            Log.Info("MetadataLookup.Find: title=\"" + title + "\" (clean=\"" + clean
                + "\") platform=\"" + platform + "\"");

            try
            {
                using (var conn = new SqliteConnection("Data Source=" + _dbPath + ";Mode=ReadOnly"))
                {
                    conn.Open();

                    var match = TryExactName(conn, clean, platform);
                    if (match != null)
                    {
                        Log.Info("  exact name match: \"" + match.Name + "\" (DbId=" + match.DatabaseId + ")");
                        return match;
                    }

                    match = TryAlternateTitle(conn, clean, platform);
                    if (match != null)
                    {
                        Log.Info("  alt-title match: \"" + match.Name + "\" (DbId=" + match.DatabaseId + ")");
                        return match;
                    }

                    match = TryPrefixLike(conn, clean, platform);
                    if (match != null)
                    {
                        Log.Info("  prefix-like match: \"" + match.Name + "\" (DbId=" + match.DatabaseId + ")");
                        return match;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("MetadataLookup.Find query failed", ex);
                return null;
            }

            Log.Info("  no match found.");
            return null;
        }

        private static Match TryExactName(SqliteConnection conn, string title, string platform)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT " + SelectColumns + " FROM Games "
                    + "WHERE Platform = $platform COLLATE NOCASE AND Name = $name COLLATE NOCASE LIMIT 1";
                cmd.Parameters.AddWithValue("$platform", platform ?? string.Empty);
                cmd.Parameters.AddWithValue("$name", title);
                return ReadFirst(cmd);
            }
        }

        private static Match TryAlternateTitle(SqliteConnection conn, string title, string platform)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT " + string.Join(", ", new[] {
                        "g.DatabaseID","g.Name","g.Platform","g.Developer","g.Publisher",
                        "g.Genres","g.Overview","g.WikipediaURL","g.ReleaseDate","g.ReleaseYear","g.CommunityRating"
                    }) + " FROM Games g "
                    + "JOIN GameAlternateTitles a ON a.DatabaseID = g.DatabaseID "
                    + "WHERE a.AlternateName = $name COLLATE NOCASE "
                    + "  AND g.Platform = $platform COLLATE NOCASE LIMIT 1";
                cmd.Parameters.AddWithValue("$platform", platform ?? string.Empty);
                cmd.Parameters.AddWithValue("$name", title);
                return ReadFirst(cmd);
            }
        }

        private static Match TryPrefixLike(SqliteConnection conn, string title, string platform)
        {
            // LIKE pattern: escape % and _ in the input, then append % for prefix match.
            var safe = title.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT " + SelectColumns + " FROM Games "
                    + "WHERE Platform = $platform COLLATE NOCASE "
                    + "  AND Name LIKE $like ESCAPE '\\' COLLATE NOCASE "
                    + "ORDER BY LENGTH(Name) ASC LIMIT 1";
                cmd.Parameters.AddWithValue("$platform", platform ?? string.Empty);
                cmd.Parameters.AddWithValue("$like", safe + "%");
                return ReadFirst(cmd);
            }
        }

        private static Match ReadFirst(SqliteCommand cmd)
        {
            using (var reader = cmd.ExecuteReader())
            {
                if (!reader.Read()) return null;
                return new Match
                {
                    DatabaseId      = ReadIntN(reader, 0),
                    Name            = ReadStringN(reader, 1),
                    Platform        = ReadStringN(reader, 2),
                    Developer       = ReadStringN(reader, 3),
                    Publisher       = ReadStringN(reader, 4),
                    Genres          = ReadStringN(reader, 5),
                    Overview        = ReadStringN(reader, 6),
                    WikipediaUrl    = ReadStringN(reader, 7),
                    ReleaseDate     = ParseDate(ReadStringN(reader, 8)),
                    ReleaseYear     = ReadIntN(reader, 9),
                    CommunityRating = ReadFloatN(reader, 10),
                };
            }
        }

        private static string ReadStringN(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
        private static int? ReadIntN(SqliteDataReader r, int i) => r.IsDBNull(i) ? (int?)null : r.GetInt32(i);
        private static float? ReadFloatN(SqliteDataReader r, int i) => r.IsDBNull(i) ? (float?)null : (float)r.GetDouble(i);

        private static DateTime? ParseDate(string s) =>
            string.IsNullOrEmpty(s) ? (DateTime?)null
            : DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : (DateTime?)null;

        private static string StripDecorations(string s) =>
            string.IsNullOrEmpty(s) ? s : DecorationRx.Replace(s, " ").Trim();

        private void EnsureResolved()
        {
            if (_resolved) return;
            lock (_lock)
            {
                if (_resolved) return;
                _resolved = true;
                _dbPath = ResolveDbPath();
                if (_dbPath != null)
                {
                    Log.Info("MetadataLookup: using DB at \"" + _dbPath + "\".");
                    DiscoverSchema(_dbPath);
                }
            }
        }

        // Plugin lives at <LaunchBox>\Plugins\Loadarr\Loadarr.dll; the DB is two
        // directories up at <LaunchBox>\Metadata\LaunchBox.Metadata.db.
        private static string ResolveDbPath()
        {
            try
            {
                var asmPath = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(asmPath))
                {
                    Log.Warn("ResolveDbPath: assembly location empty.");
                    return null;
                }
                var pluginDir = Path.GetDirectoryName(asmPath);
                var pluginsDir = Path.GetDirectoryName(pluginDir);
                var lbRoot = Path.GetDirectoryName(pluginsDir);
                if (string.IsNullOrEmpty(lbRoot))
                {
                    Log.Warn("ResolveDbPath: could not derive LaunchBox root from \"" + asmPath + "\".");
                    return null;
                }
                var candidate = Path.Combine(lbRoot, "Metadata", "LaunchBox.Metadata.db");
                if (File.Exists(candidate)) return candidate;

                Log.Warn("ResolveDbPath: \"" + candidate + "\" does not exist.");
                var metaDir = Path.Combine(lbRoot, "Metadata");
                if (Directory.Exists(metaDir))
                {
                    var entries = Directory.GetFileSystemEntries(metaDir).Select(Path.GetFileName).OrderBy(n => n).ToArray();
                    Log.Warn("Metadata dir contents (" + entries.Length + "): " + string.Join(", ", entries));
                }
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("ResolveDbPath threw", ex);
                return null;
            }
        }

        private static void DiscoverSchema(string path)
        {
            try
            {
                using (var conn = new SqliteConnection("Data Source=" + path + ";Mode=ReadOnly"))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM Games";
                        var count = (long)cmd.ExecuteScalar();
                        Log.Info("DiscoverSchema: Games table has " + count + " rows.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("DiscoverSchema failed", ex);
            }
        }
    }
}
