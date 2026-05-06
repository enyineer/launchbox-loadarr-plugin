using System;
using System.Collections.Generic;

namespace Loadarr.Services
{
    /// <summary>
    /// Translates between LaunchBox platform names (free-form, user-set) and provider-specific codes.
    /// LaunchBox doesn't enforce a vocabulary — users sometimes write "Super Nintendo Entertainment System",
    /// sometimes "SNES" — so we match on the trimmed, case-insensitive form.
    ///
    /// Vimm's Lair codes correspond to the URL segment after /vault/ on vimm.net.
    /// One LaunchBox platform may map to multiple Vimm codes when Vimm subdivides
    /// a console's library (e.g. Xbox 360 disc + digital).
    /// </summary>
    internal static class PlatformMapper
    {
        // Reverse lookup: Vimm system code -> canonical LaunchBox platform name.
        // Used when importing a Vimm result so the game lands on the platform
        // LaunchBox's metadata DB recognizes (which carries category, default
        // emulator paths, scraping config, etc.).
        private static readonly Dictionary<string, string> VimmToLaunchBox =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["NES"] = "Nintendo Entertainment System",
                ["SNES"] = "Super Nintendo Entertainment System",
                ["N64"] = "Nintendo 64",
                ["GameCube"] = "Nintendo GameCube",
                ["Wii"] = "Nintendo Wii",
                ["WiiWare"] = "Nintendo WiiWare",
                ["GB"] = "Nintendo Game Boy",
                ["GBC"] = "Nintendo Game Boy Color",
                ["GBA"] = "Nintendo Game Boy Advance",
                ["DS"] = "Nintendo DS",
                ["3DS"] = "Nintendo 3DS",
                ["VB"] = "Nintendo Virtual Boy",
                ["Genesis"] = "Sega Genesis",
                ["SMS"] = "Sega Master System",
                ["GG"] = "Sega Game Gear",
                ["SegaCD"] = "Sega CD",
                ["32X"] = "Sega 32X",
                ["Saturn"] = "Sega Saturn",
                ["Dreamcast"] = "Sega Dreamcast",
                ["PS1"] = "Sony Playstation",
                ["PS2"] = "Sony Playstation 2",
                ["PS3"] = "Sony Playstation 3",
                ["PSP"] = "Sony PSP",
                ["Xbox"] = "Microsoft Xbox",
                ["Xbox360"] = "Microsoft Xbox 360",
                ["X360-D"] = "Microsoft Xbox 360",
                ["Atari2600"] = "Atari 2600",
                ["Atari5200"] = "Atari 5200",
                ["Atari7800"] = "Atari 7800",
                ["Lynx"] = "Atari Lynx",
                ["Jaguar"] = "Atari Jaguar",
                ["JaguarCD"] = "Atari Jaguar CD",
                ["TG16"] = "NEC TurboGrafx-16",
                ["TGCD"] = "NEC TurboGrafx-CD",
                ["CDi"] = "Philips CD-i",
            };

        public static string ToLaunchBoxName(string vimmCode)
        {
            if (string.IsNullOrWhiteSpace(vimmCode)) return null;
            return VimmToLaunchBox.TryGetValue(vimmCode.Trim(), out var name) ? name : null;
        }

        // LaunchBox-style name -> Vimm's Lair "system" path segment(s).
        private static readonly Dictionary<string, string[]> VimmCanonical =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                // ---- Nintendo (consoles) ----
                ["nintendo entertainment system"] = new[] { "NES" },
                ["nes"] = new[] { "NES" },
                ["famicom"] = new[] { "NES" },

                ["super nintendo entertainment system"] = new[] { "SNES" },
                ["super nintendo"] = new[] { "SNES" },
                ["snes"] = new[] { "SNES" },
                ["super famicom"] = new[] { "SNES" },

                ["nintendo 64"] = new[] { "N64" },
                ["n64"] = new[] { "N64" },

                ["nintendo gamecube"] = new[] { "GameCube" },
                ["gamecube"] = new[] { "GameCube" },

                ["nintendo wii"] = new[] { "Wii", "WiiWare" },
                ["wii"] = new[] { "Wii", "WiiWare" },
                ["nintendo wiiware"] = new[] { "WiiWare" },
                ["wiiware"] = new[] { "WiiWare" },

                // ---- Nintendo (handhelds) ----
                ["nintendo game boy"] = new[] { "GB" },
                ["game boy"] = new[] { "GB" },
                ["gb"] = new[] { "GB" },

                ["nintendo game boy color"] = new[] { "GBC" },
                ["game boy color"] = new[] { "GBC" },
                ["gbc"] = new[] { "GBC" },

                ["nintendo game boy advance"] = new[] { "GBA" },
                ["game boy advance"] = new[] { "GBA" },
                ["gba"] = new[] { "GBA" },

                ["nintendo ds"] = new[] { "DS" },
                ["nds"] = new[] { "DS" },

                ["nintendo 3ds"] = new[] { "3DS" },
                ["3ds"] = new[] { "3DS" },

                ["nintendo virtual boy"] = new[] { "VB" },
                ["virtual boy"] = new[] { "VB" },

                // ---- Sega ----
                ["sega genesis"] = new[] { "Genesis" },
                ["sega mega drive"] = new[] { "Genesis" },
                ["genesis"] = new[] { "Genesis" },
                ["mega drive"] = new[] { "Genesis" },

                ["sega master system"] = new[] { "SMS" },
                ["master system"] = new[] { "SMS" },

                ["sega game gear"] = new[] { "GG" },
                ["game gear"] = new[] { "GG" },

                ["sega cd"] = new[] { "SegaCD" },
                ["mega-cd"] = new[] { "SegaCD" },
                ["mega cd"] = new[] { "SegaCD" },

                ["sega 32x"] = new[] { "32X" },
                ["32x"] = new[] { "32X" },

                ["sega saturn"] = new[] { "Saturn" },
                ["saturn"] = new[] { "Saturn" },

                ["sega dreamcast"] = new[] { "Dreamcast" },
                ["dreamcast"] = new[] { "Dreamcast" },

                // ---- Sony ----
                ["sony playstation"] = new[] { "PS1" },
                ["playstation"] = new[] { "PS1" },
                ["psx"] = new[] { "PS1" },
                ["ps1"] = new[] { "PS1" },

                ["sony playstation 2"] = new[] { "PS2" },
                ["playstation 2"] = new[] { "PS2" },
                ["ps2"] = new[] { "PS2" },

                ["sony playstation 3"] = new[] { "PS3" },
                ["playstation 3"] = new[] { "PS3" },
                ["ps3"] = new[] { "PS3" },

                ["sony psp"] = new[] { "PSP" },
                ["playstation portable"] = new[] { "PSP" },
                ["psp"] = new[] { "PSP" },

                // ---- Microsoft ----
                ["microsoft xbox"] = new[] { "Xbox" },
                ["xbox"] = new[] { "Xbox" },

                ["microsoft xbox 360"] = new[] { "Xbox360", "X360-D" },
                ["xbox 360"] = new[] { "Xbox360", "X360-D" },

                // ---- Atari ----
                ["atari 2600"] = new[] { "Atari2600" },
                ["atari 5200"] = new[] { "Atari5200" },
                ["atari 7800"] = new[] { "Atari7800" },
                ["atari lynx"] = new[] { "Lynx" },
                ["lynx"] = new[] { "Lynx" },
                ["atari jaguar"] = new[] { "Jaguar" },
                ["jaguar"] = new[] { "Jaguar" },
                ["atari jaguar cd"] = new[] { "JaguarCD" },
                ["jaguar cd"] = new[] { "JaguarCD" },

                // ---- NEC / Hudson ----
                ["nec turbografx-16"] = new[] { "TG16" },
                ["nec turbografx 16"] = new[] { "TG16" },
                ["turbografx-16"] = new[] { "TG16" },
                ["turbografx 16"] = new[] { "TG16" },
                ["pc engine"] = new[] { "TG16" },
                ["pc-engine"] = new[] { "TG16" },

                ["nec turbografx-cd"] = new[] { "TGCD" },
                ["turbografx-cd"] = new[] { "TGCD" },
                ["turbografx cd"] = new[] { "TGCD" },
                ["pc engine cd"] = new[] { "TGCD" },
                ["pc-engine cd"] = new[] { "TGCD" },

                // ---- Other ----
                ["philips cd-i"] = new[] { "CDi" },
                ["cd-i"] = new[] { "CDi" },
                ["cdi"] = new[] { "CDi" },
            };

        public static List<string> ToVimmSystemCodes(string launchboxPlatform)
        {
            if (string.IsNullOrWhiteSpace(launchboxPlatform)) return new List<string>();
            return VimmCanonical.TryGetValue(launchboxPlatform.Trim(), out var codes)
                ? new List<string>(codes)
                : new List<string>();
        }
    }
}
