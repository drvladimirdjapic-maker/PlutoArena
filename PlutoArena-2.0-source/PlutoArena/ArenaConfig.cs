using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PlutoArena
{
    /// <summary>
    /// Settings stored in PlutoArena.ini next to PlutoArena.exe.
    /// The file is created with default values on first start and can be
    /// edited with Notepad (paths, profile names ...).
    /// </summary>
    internal sealed class ArenaConfig
    {
        public string StarCraftExe { get; set; } =
            @"C:\Program Files (x86)\StarCraft\x86\StarCraft.exe";

        public string PlutoDll { get; set; } =
            @"C:\Program Files (x86)\Starcraft116\bwapi-data\AI\pluto.dll";

        /// <summary>Pluto v2.0 (optional): pluto.dll with its own pluto\ folder.</summary>
        public string PlutoDll2 { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "pluto-v2.0", "pluto.dll");

        /// <summary>Pluto version used for PLAY: "1.0" (cog2026-2578600) or "2.0".</summary>
        public string PlutoVersion { get; set; } = "1.0";

        /// <summary>pluto.dll of the selected version.</summary>
        public string ActivePlutoDll => PlutoVersion == "2.0" ? PlutoDll2 : PlutoDll;

        public string NativeFolder { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PlutoRemasteredControlCenter", "native");

        public string ModesFolder { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PlutoDifficultyPack", "modes");

        /// <summary>Folder inside StarCraft\Maps with the arena maps.</summary>
        public string MapsFolderName { get; set; } = "000_PlutoArena";

        /// <summary>LAN profile (Registry) used by the human player.</summary>
        public string HumanProfile { get; set; } = "Player";

        /// <summary>LAN profile (Registry) used by the Pluto window.</summary>
        public string PlutoProfile { get; set; } = "PlutoAi";

        /// <summary>
        /// Position of the profiles in the Registry list (0 = first).
        /// Only used when the profile name cannot be read from the screen.
        /// </summary>
        public int HumanProfileIndex { get; set; } = 0;
        public int PlutoProfileIndex { get; set; } = 1;

        /// <summary>Ignore the real mouse/keyboard while the menus are automated (Esc cancels).</summary>
        public bool LockInput { get; set; } = true;

        /// <summary>Save screen captures that OCR could not read (for troubleshooting).</summary>
        public bool DebugImages { get; set; } = false;

        /// <summary>Hide the Pluto StarCraft window after the game starts.</summary>
        public bool HidePlutoWindow { get; set; } = false;

        /// <summary>Mute Pluto's StarCraft in the Windows volume mixer (your StarCraft keeps its sound).</summary>
        public bool MutePlutoSound { get; set; } = true;

        /// <summary>Show what Pluto writes in chat on top of your StarCraft.</summary>
        public bool ShowPlutoChat { get; set; } = true;

        /// <summary>Ladder mode: the rank follows your results (2 wins up, 2 losses down).</summary>
        public bool LadderMode { get; set; } = false;

        /// <summary>Last selected rank (S ... F).</summary>
        public string Rank { get; set; } = "C";

        /// <summary>Show the summary window after each game.</summary>
        public bool ShowSummary { get; set; } = true;

        /// <summary>Short start-up animation of the console.</summary>
        public bool ShowIntro { get; set; } = true;

        /// <summary>Hover / click sounds of the Pluto Arena window.</summary>
        public bool UiSounds { get; set; } = true;

        /// <summary>Races selected last time (Terran, Protoss, Zerg, Random).</summary>
        public string HumanRace { get; set; } = "Terran";
        public string PlutoRace { get; set; } = "Terran";

        /// <summary>
        /// Multiplies all fixed waits. Increase (e.g. 1.5) on a slow PC
        /// if a menu step is skipped.
        /// </summary>
        public double DelayFactor { get; set; } = 1.0;

        /// <summary>True when PlutoArena.ini did not exist yet (first start).</summary>
        public bool WasCreated { get; private set; }

        private static readonly string[] Races = { "Terran", "Protoss", "Zerg", "Random" };

        private static bool IsRace(string value) => Races.Any(r => r.Equals(value, StringComparison.OrdinalIgnoreCase));

        private static string Race(string value) => Races.First(r => r.Equals(value, StringComparison.OrdinalIgnoreCase));

        public static string FilePath =>
            Path.Combine(AppContext.BaseDirectory, "PlutoArena.ini");

        public string MapsFolder
        {
            get
            {
                // ...\StarCraft\x86\StarCraft.exe -> ...\StarCraft\Maps\<folder>
                string? x86 = Path.GetDirectoryName(StarCraftExe);
                string root = x86 == null
                    ? ""
                    : Directory.GetParent(x86)?.FullName ?? x86;

                return Path.Combine(root, "Maps", MapsFolderName);
            }
        }

        public static ArenaConfig Load()
        {
            ArenaConfig config = new ArenaConfig();

            try
            {
                if (!File.Exists(FilePath))
                {
                    // First start: find StarCraft / Pluto automatically.
                    config.WasCreated = true;
                    SetupCheck.Detect(config);
                    config.Save();
                    return config;
                }

                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();

                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("["))
                        continue;

                    int eq = line.IndexOf('=');

                    if (eq <= 0)
                        continue;

                    string key = line[..eq].Trim();
                    string value = line[(eq + 1)..].Trim();

                    switch (key.ToLowerInvariant())
                    {
                        case "starcraftexe": config.StarCraftExe = value; break;
                        case "plutodll": config.PlutoDll = value; break;
                        case "plutodll2": config.PlutoDll2 = value; break;
                        case "plutoversion": config.PlutoVersion = value == "2.0" ? "2.0" : "1.0"; break;
                        case "nativefolder": config.NativeFolder = value; break;
                        case "modesfolder": config.ModesFolder = value; break;
                        case "mapsfoldername": config.MapsFolderName = value; break;
                        case "humanprofile": config.HumanProfile = value; break;
                        case "plutoprofile": config.PlutoProfile = value; break;
                        case "humanprofileindex": if (int.TryParse(value, out int h)) config.HumanProfileIndex = h; break;
                        case "plutoprofileindex": if (int.TryParse(value, out int p)) config.PlutoProfileIndex = p; break;
                        case "debugimages": config.DebugImages = value is "1" or "true" or "True" or "yes"; break;
                        case "lockinput": config.LockInput = value is "1" or "true" or "True" or "yes"; break;
                        case "hideplutowindow": config.HidePlutoWindow = value is "1" or "true" or "True" or "yes"; break;
                        case "muteplutosound": config.MutePlutoSound = value is "1" or "true" or "True" or "yes"; break;
                        case "showplutochat": config.ShowPlutoChat = value is "1" or "true" or "True" or "yes"; break;
                        case "laddermode": config.LadderMode = value is "1" or "true" or "True" or "yes"; break;
                        case "showsummary": config.ShowSummary = value is "1" or "true" or "True" or "yes"; break;
                        case "showintro": config.ShowIntro = value is "1" or "true" or "True" or "yes"; break;
                        case "uisounds": config.UiSounds = value is "1" or "true" or "True" or "yes"; break;
                        case "humanrace": if (IsRace(value)) config.HumanRace = Race(value); break;
                        case "plutorace": if (IsRace(value)) config.PlutoRace = Race(value); break;
                        case "rank": if (ArenaStats.RankOrder.Contains(value.ToUpperInvariant())) config.Rank = value.ToUpperInvariant(); break;
                        case "delayfactor":
                            if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out double d) && d > 0)
                                config.DelayFactor = d;
                            break;
                    }
                }
            }
            catch
            {
                // Broken file -> defaults.
            }

            // Paths that no longer exist (e.g. the bridge folder was moved)
            // fall back to the files shipped with the installer. Existing
            // paths are never changed.
            try
            {
                SetupCheck.Detect(config);
            }
            catch
            {
            }

            return config;
        }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[PlutoArena]");
                sb.AppendLine("; StarCraft Remastered executable");
                sb.AppendLine($"StarCraftExe={StarCraftExe}");
                sb.AppendLine("; Pluto AI dll (StarCraft 1.16.1 / BWAPI folder)");
                sb.AppendLine($"PlutoDll={PlutoDll}");
                sb.AppendLine("; Pluto v2.0 dll (optional, its own folder with the pluto subfolder)");
                sb.AppendLine($"PlutoDll2={PlutoDll2}");
                sb.AppendLine("; Pluto version for PLAY: 1.0 or 2.0");
                sb.AppendLine($"PlutoVersion={PlutoVersion}");
                sb.AppendLine("; Pluto Remastered bridge (native folder, included in the installer)");
                sb.AppendLine($"NativeFolder={NativeFolder}");
                sb.AppendLine("; PlutoDifficultyPack\\modes folder");
                sb.AppendLine($"ModesFolder={ModesFolder}");
                sb.AppendLine("; Folder inside StarCraft\\Maps with the arena maps");
                sb.AppendLine($"MapsFolderName={MapsFolderName}");
                sb.AppendLine("; LAN profiles (Registry screen)");
                sb.AppendLine($"HumanProfile={HumanProfile}");
                sb.AppendLine($"PlutoProfile={PlutoProfile}");
                sb.AppendLine("; Position in the Registry list (0 = first), used if the name cannot be read");
                sb.AppendLine($"HumanProfileIndex={HumanProfileIndex}");
                sb.AppendLine($"PlutoProfileIndex={PlutoProfileIndex}");
                sb.AppendLine("; Ignore the real mouse and keyboard during PLAY until both players are in the lobby (0/1). Esc cancels.");
                sb.AppendLine($"LockInput={(LockInput ? 1 : 0)}");
                sb.AppendLine("; Hide Pluto's StarCraft window after the game starts (0/1)");
                sb.AppendLine($"HidePlutoWindow={(HidePlutoWindow ? 1 : 0)}");
                sb.AppendLine("; Mute Pluto's StarCraft sound, yours stays on (0/1)");
                sb.AppendLine($"MutePlutoSound={(MutePlutoSound ? 1 : 0)}");
                sb.AppendLine("; Show Pluto's chat messages on top of your StarCraft (0/1)");
                sb.AppendLine($"ShowPlutoChat={(ShowPlutoChat ? 1 : 0)}");
                sb.AppendLine("; Ladder mode: rank follows your results (0/1)");
                sb.AppendLine($"LadderMode={(LadderMode ? 1 : 0)}");
                sb.AppendLine("; Last selected rank (S, A, B, C, D, E, F)");
                sb.AppendLine($"Rank={Rank}");
                sb.AppendLine("; Summary window after each game (0/1)");
                sb.AppendLine($"ShowSummary={(ShowSummary ? 1 : 0)}");
                sb.AppendLine("; Start-up animation of the console (0/1, a click skips it)");
                sb.AppendLine($"ShowIntro={(ShowIntro ? 1 : 0)}");
                sb.AppendLine("; Hover and click sounds of the Pluto Arena window (0/1)");
                sb.AppendLine($"UiSounds={(UiSounds ? 1 : 0)}");
                sb.AppendLine("; Races selected last time");
                sb.AppendLine($"HumanRace={HumanRace}");
                sb.AppendLine($"PlutoRace={PlutoRace}");
                sb.AppendLine("; Save unreadable screen captures to the debug folder (0/1, troubleshooting only)");
                sb.AppendLine($"DebugImages={(DebugImages ? 1 : 0)}");
                sb.AppendLine("; Multiplies all waits; use 1.5 or 2 on a slow PC");
                sb.AppendLine($"DelayFactor={DelayFactor.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

                File.WriteAllText(FilePath, sb.ToString());
            }
            catch
            {
                // Not critical.
            }
        }

        /// <summary>
        /// Maps in StarCraft\Maps\000_PlutoArena, in the order SC:R lists them.
        /// </summary>
        public List<MapEntry> LoadMaps()
        {
            List<MapEntry> maps = new();

            try
            {
                if (!Directory.Exists(MapsFolder))
                    return maps;

                List<string> files = Directory
                    .GetFiles(MapsFolder)
                    .Where(f =>
                        f.EndsWith(".scx", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".scm", StringComparison.OrdinalIgnoreCase))
                    .Select(f => Path.GetFileName(f))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (int i = 0; i < files.Count; i++)
                    maps.Add(new MapEntry(files[i], i));
            }
            catch
            {
            }

            return maps;
        }
    }

    internal sealed class MapEntry
    {
        public MapEntry(string fileName, int index)
        {
            FileName = fileName;
            Index = index;

            // "01_Fighting Spirit.scx" -> "Fighting Spirit"
            string name = Path.GetFileNameWithoutExtension(fileName);
            int underscore = name.IndexOf('_');

            if (underscore > 0 && underscore <= 3 && name[..underscore].All(char.IsDigit))
                name = name[(underscore + 1)..];

            DisplayName = name;
        }

        public string FileName { get; }
        public string DisplayName { get; }

        /// <summary>Position in the SC:R file list (0 = first map).</summary>
        public int Index { get; }

        public override string ToString() => DisplayName;
    }
}
