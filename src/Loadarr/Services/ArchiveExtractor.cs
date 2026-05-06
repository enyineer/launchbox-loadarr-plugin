using System;
using System.IO;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace Loadarr.Services
{
    /// <summary>
    /// Many ROM sites ship .zip / .7z / .rar archives that contain the actual
    /// ROM plus companion text files (e.g. Vimm's Lair always includes a
    /// "Vimm's Lair.txt" readme alongside the ROM). LaunchBox/emulators want
    /// the bare ROM, so we extract the primary ROM entry — the largest entry
    /// that isn't an obvious text/metadata file.
    /// </summary>
    internal static class ArchiveExtractor
    {
        private static readonly string[] ArchiveExt = { ".zip", ".7z", ".rar" };

        // Extensions we treat as readme / metadata noise inside ROM archives.
        // Note: ".md" is intentionally NOT here — Sega Mega Drive ROMs use it.
        private static readonly string[] JunkExtensions =
        {
            ".txt", ".nfo", ".url", ".sfv", ".diz", ".md5", ".sha1", ".sha256",
        };

        private static readonly string[] JunkFileNames =
        {
            "thumbs.db", "desktop.ini", ".ds_store",
        };

        // When any of these is present, the archive holds a multi-file disc
        // image: extract all non-junk entries together and point LaunchBox at
        // the indicator (the emulator opens the cue/gdi/etc., which references
        // the sibling .bin / .raw / .img files in the same directory).
        private static readonly string[] DiscIndicatorExtensions =
        {
            ".cue", ".gdi", ".ccd", ".toc", ".m3u",
        };

        // Platforms where the .zip itself IS the ROM — typically MAME-style
        // arcade romsets where the emulator reads individual chip dumps from
        // inside the zip. Extracting these would break loading.
        // Substring match (case-insensitive) so variants like "Capcom Arcade
        // Stadium" or "MAME 2003" all hit.
        private static readonly string[] ArcadeMarkers =
        {
            "Arcade",
            "MAME",
            "Capcom Play System",
            "Sega Model 2",
            "Sega Model 3",
            "Sega Naomi",
            "Sega ST-V",
            "Atomiswave",
            "Final Burn",
            "Neo Geo MVS",
            "Konami System 573",
            "Taito Type X",
        };

        /// <summary>
        /// Returns false for platforms whose distribution format is the .zip
        /// itself (arcade / MAME romsets). Callers should skip extraction in
        /// that case and pass the archive straight to the emulator.
        /// </summary>
        public static bool ShouldExtractFor(string platform)
        {
            if (string.IsNullOrEmpty(platform)) return true;
            foreach (var marker in ArcadeMarkers)
            {
                if (platform.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }
            return true;
        }

        public static bool IsArchive(string path)
        {
            if (ArchiveExt.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                return true;
            // Some sources hand us archive bytes saved with a ROM-shaped extension
            // (e.g. Vimm's served zip-wrapped ROMs named ".z64" historically).
            // Sniff the first few bytes so we don't silently skip extraction.
            return DetectArchiveByMagic(path);
        }

        private static bool DetectArchiveByMagic(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var head = new byte[6];
                    var read = fs.Read(head, 0, head.Length);
                    if (read >= 4 && head[0] == 0x50 && head[1] == 0x4B
                        && (head[2] == 0x03 || head[2] == 0x05 || head[2] == 0x07)) return true; // ZIP
                    if (read >= 6 && head[0] == 0x37 && head[1] == 0x7A && head[2] == 0xBC
                        && head[3] == 0xAF && head[4] == 0x27 && head[5] == 0x1C) return true; // 7z
                    if (read >= 4 && head[0] == 0x52 && head[1] == 0x61 && head[2] == 0x72
                        && head[3] == 0x21) return true; // RAR
                }
            }
            catch (Exception ex)
            {
                Log.Warn("ArchiveExtractor: magic-byte sniff failed: " + ex.Message);
            }
            return false;
        }

        /// <summary>
        /// Extracts the primary (largest non-junk) entry from the archive into
        /// <paramref name="targetDir"/> and returns its path. Falls back to the
        /// original archive path when the archive is empty, can't be opened, or
        /// extraction fails. <paramref name="progress"/> reports 0–100 percent
        /// of total uncompressed bytes processed; null disables reporting.
        /// </summary>
        public static string ExtractPrimary(
            string archivePath, string targetDir, IProgress<double> progress = null)
        {
            if (!IsArchive(archivePath))
            {
                Log.Info("ArchiveExtractor: \"" + archivePath + "\" is not a recognized archive — leaving as-is.");
                return archivePath;
            }

            try
            {
                using (var archive = ArchiveFactory.Open(archivePath))
                {
                    var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
                    if (entries.Count == 0)
                    {
                        Log.Warn("ArchiveExtractor: \"" + archivePath + "\" has no file entries.");
                        return archivePath;
                    }

                    // Prefer non-junk entries; fall back to all entries if the
                    // filter empties the list (e.g. archive is just a .nfo).
                    var nonJunk = entries.Where(e => !IsJunk(e.Key)).ToList();
                    var candidates = nonJunk.Count > 0 ? nonJunk : entries;

                    Directory.CreateDirectory(targetDir);

                    // Disc-image archives (.cue+.bin, .gdi+.raw, etc.) need ALL
                    // sidecar files extracted together. Detect via indicator.
                    var indicator = candidates.FirstOrDefault(e =>
                        Array.IndexOf(DiscIndicatorExtensions, GetExtLower(e.Key)) >= 0);

                    if (indicator != null)
                    {
                        Log.Info("ArchiveExtractor: disc image detected (indicator=\""
                            + indicator.Key + "\"); extracting " + candidates.Count + " entries.");
                        long total = candidates.Sum(c => Math.Max(0, c.Size));
                        long done = 0;
                        foreach (var entry in candidates)
                        {
                            ExtractEntry(entry, targetDir, written =>
                                ReportPct(progress, done + written, total));
                            done += Math.Max(0, entry.Size);
                        }
                        progress?.Report(100);
                        return Path.Combine(targetDir, Path.GetFileName(indicator.Key));
                    }

                    // Single-file ROM: extract the largest non-junk entry.
                    var primary = candidates.OrderByDescending(e => e.Size).First();
                    Log.Info("ArchiveExtractor: " + entries.Count + " entries; picking primary \""
                        + primary.Key + "\" (" + primary.Size.ToString("n0") + " bytes).");
                    long primaryTotal = Math.Max(0, primary.Size);
                    ExtractEntry(primary, targetDir, written =>
                        ReportPct(progress, written, primaryTotal));
                    progress?.Report(100);
                    return Path.Combine(targetDir, Path.GetFileName(primary.Key));
                }
            }
            catch (Exception ex)
            {
                Log.Error("ArchiveExtractor: failed to extract \"" + archivePath + "\"", ex);
                return archivePath;
            }
        }

        // Manual stream copy so we can report progress mid-entry. SharpCompress's
        // WriteToDirectory is convenient but doesn't surface byte counters.
        private static void ExtractEntry(IArchiveEntry entry, string targetDir, Action<long> onBytesWritten)
        {
            var fileName = Path.GetFileName(entry.Key);
            if (string.IsNullOrEmpty(fileName))
            {
                // Defensive: malformed entry with no leaf name. Skip rather
                // than write to a path we can't predict.
                Log.Warn("ArchiveExtractor: skipping entry with empty name (key=\"" + entry.Key + "\").");
                return;
            }
            var outPath = Path.Combine(targetDir, fileName);
            using (var input = entry.OpenEntryStream())
            using (var output = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024))
            {
                var buffer = new byte[64 * 1024];
                long written = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    written += read;
                    onBytesWritten?.Invoke(written);
                }
            }
        }

        private static void ReportPct(IProgress<double> progress, long done, long total)
        {
            if (progress == null || total <= 0) return;
            var pct = done * 100.0 / total;
            if (pct < 0) pct = 0; else if (pct > 100) pct = 100;
            progress.Report(pct);
        }

        private static bool IsJunk(string key)
        {
            var name = Path.GetFileName(key);
            if (string.IsNullOrEmpty(name)) return true;
            var lower = name.ToLowerInvariant();
            if (Array.IndexOf(JunkFileNames, lower) >= 0) return true;
            var ext = Path.GetExtension(lower);
            return Array.IndexOf(JunkExtensions, ext) >= 0;
        }

        private static string GetExtLower(string key) =>
            string.IsNullOrEmpty(key) ? string.Empty : Path.GetExtension(key).ToLowerInvariant();
    }
}
