using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PlutoArena
{
    /// <summary>Result of one game, read from the bridge log after the game.</summary>
    internal sealed class GameOutcome
    {
        public bool Known { get; init; }
        public bool HumanWon { get; init; }
        public int Frames { get; init; }
        public DateTime StartedUtc { get; init; }

        public static readonly GameOutcome Unknown = new GameOutcome { Known = false };

        /// <summary>Game time in minutes (Fastest: ~23.8 frames per second).</summary>
        public double Minutes => Frames / 23.81 / 60.0;
    }

    internal sealed class GameRecord
    {
        public DateTime Date { get; init; }
        public string Version { get; init; } = "1.0";
        public string Rank { get; init; } = "S";
        public string Map { get; init; } = "";
        public string HumanRace { get; init; } = "";
        public string PlutoRace { get; init; } = "";
        public bool Won { get; init; }
        public int Frames { get; init; }
    }

    /// <summary>
    /// Your results against Pluto, per Pluto version and rank, stored in
    /// PlutoArena-stats.txt next to PlutoArena.exe (one game per line).
    /// Also decides the ladder steps: 2 wins in a row on a rank = one rank
    /// up, 2 losses in a row = one rank down.
    /// </summary>
    internal sealed class ArenaStats
    {
        public static readonly string[] RankOrder = { "S", "A", "B", "C", "D", "E", "F" };

        private readonly List<GameRecord> games = new();

        public static string FilePath => Path.Combine(AppContext.BaseDirectory, "PlutoArena-stats.txt");

        public IReadOnlyList<GameRecord> Games => games;

        public static ArenaStats Load()
        {
            ArenaStats stats = new ArenaStats();

            try
            {
                if (!File.Exists(FilePath))
                    return stats;

                foreach (string line in File.ReadAllLines(FilePath))
                {
                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;

                    string[] p = line.Split(';');

                    if (p.Length < 8)
                        continue;

                    if (!DateTime.TryParse(p[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime date))
                        continue;

                    stats.games.Add(new GameRecord
                    {
                        Date = date,
                        Version = p[1],
                        Rank = p[2],
                        Map = p[3],
                        HumanRace = p[4],
                        PlutoRace = p[5],
                        Won = p[6] == "W",
                        Frames = int.TryParse(p[7], out int f) ? f : 0
                    });
                }
            }
            catch
            {
                // Broken file: start fresh, the file is rewritten on the next game.
            }

            return stats;
        }

        public void Add(GameRecord r)
        {
            games.Add(r);

            try
            {
                bool header = !File.Exists(FilePath);
                StringBuilder sb = new StringBuilder();

                if (header)
                    sb.AppendLine("# date;pluto version;rank;map;your race;pluto race;W/L (you);game frames");

                sb.AppendLine(string.Join(";",
                    r.Date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    r.Version, r.Rank, Clean(r.Map), r.HumanRace, r.PlutoRace,
                    r.Won ? "W" : "L", r.Frames.ToString(CultureInfo.InvariantCulture)));

                File.AppendAllText(FilePath, sb.ToString());
            }
            catch
            {
                // Statistics are not essential.
            }
        }

        private static string Clean(string s) => s.Replace(';', ',');

        public (int Wins, int Losses) Record(string version, string rank)
        {
            var list = games.Where(g => g.Version == version && g.Rank == rank).ToList();
            return (list.Count(g => g.Won), list.Count(g => !g.Won));
        }

        /// <summary>
        /// Wins and losses grouped by <paramref name="key"/> (Pluto's race,
        /// your race, map ...) for one Pluto version; most played first.
        /// </summary>
        public List<(string Key, int Wins, int Losses)> By(string version, Func<GameRecord, string> key) =>
            games.Where(g => g.Version == version)
                .GroupBy(g => key(g).Length > 0 ? key(g) : "?")
                .Select(grp => (grp.Key, grp.Count(g => g.Won), grp.Count(g => !g.Won)))
                .OrderByDescending(t => t.Item2 + t.Item3)
                .ThenBy(t => t.Key)
                .ToList();

        /// <summary>Last results on this version/rank, newest first ("WWL").</summary>
        public string Streak(string version, string rank, int max = 5)
        {
            var sb = new StringBuilder();

            foreach (GameRecord g in games.Where(g => g.Version == version).Reverse())
            {
                if (g.Rank != rank || sb.Length >= max)
                    break;

                sb.Append(g.Won ? 'W' : 'L');
            }

            return sb.ToString();
        }

        /// <summary>
        /// Ladder: the rank to play next. 2 wins in a row on the current rank
        /// move you one rank up (towards S), 2 losses one rank down (towards F).
        /// </summary>
        public string NextLadderRank(string version, string rank)
        {
            string streak = Streak(version, rank, 2);
            int i = Array.IndexOf(RankOrder, rank);

            if (i < 0)
                return rank;

            if (streak == "WW" && i > 0)
                return RankOrder[i - 1];

            if (streak == "LL" && i < RankOrder.Length - 1)
                return RankOrder[i + 1];

            return rank;
        }
    }

    /// <summary>
    /// Pluto's own files in StarCraft\x86\bwapi-data\write:
    /// pluto_bandit_&lt;opponent&gt;.txt = opening statistics against you and,
    /// for Pluto 2.0, one JSON line per game (result, opening, win estimate).
    /// </summary>
    internal static class PlutoMemory
    {
        public static IEnumerable<string> Folders(ArenaConfig c)
        {
            string? x86 = Path.GetDirectoryName(c.StarCraftExe);

            if (x86 == null)
                yield break;

            foreach (string sub in new[] { "write", "read" })
            {
                yield return Path.Combine(x86, "bwapi-data", sub);

                // Windows may redirect writes to Program Files into the VirtualStore.
                string root = Path.GetPathRoot(x86) ?? "";
                string relative = x86.Substring(root.Length);
                yield return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VirtualStore", relative, "bwapi-data", sub);
            }
        }

        /// <summary>Pluto replaces characters that are not letters/digits in the name.</summary>
        public static string FileName(string opponent)
        {
            var sb = new StringBuilder();

            foreach (char ch in opponent)
                sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');

            return $"pluto_bandit_{sb}.txt";
        }

        public static List<string> Files(ArenaConfig c, string opponent) =>
            Folders(c)
                .Select(f => Path.Combine(f, FileName(opponent)))
                .Where(File.Exists)
                .ToList();

        /// <summary>Deletes what Pluto remembers about this opponent. Returns the number of files deleted.</summary>
        public static int Reset(ArenaConfig c, string opponent, out string? error)
        {
            error = null;
            int deleted = 0;

            foreach (string file in Files(c, opponent))
            {
                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }

            return deleted;
        }

        public sealed class GameLog
        {
            public string Result { get; init; } = "";
            public string Opening { get; init; } = "";
            public string Model { get; init; } = "";
            public int Frames { get; init; }
            public double InferAvgMs { get; init; }
            public List<double> WinTrajectory { get; init; } = new();
        }

        /// <summary>
        /// The last game Pluto 2.0 logged against this opponent that ended
        /// after <paramref name="sinceUtc"/>, or null (Pluto 1.0, or no end record).
        /// </summary>
        public static GameLog? LastGame(ArenaConfig c, string opponent, DateTime sinceUtc)
        {
            try
            {
                string? file = Files(c, opponent)
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                    .FirstOrDefault();

                if (file == null)
                    return null;

                Dictionary<long, (string Bo, string Model)> starts = new();
                GameLog? last = null;

                using FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using StreamReader reader = new StreamReader(fs);

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!line.StartsWith("{"))
                        continue;

                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(line);
                        JsonElement e = doc.RootElement;

                        string t = Str(e, "t");
                        long id = e.TryGetProperty("id", out JsonElement idEl) && idEl.TryGetInt64(out long v) ? v : -1;

                        if (t == "start")
                        {
                            starts[id] = (Str(e, "bo"), Str(e, "model"));
                        }
                        else if (t == "end")
                        {
                            if (!DateTime.TryParse(Str(e, "date"), CultureInfo.InvariantCulture,
                                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime date) ||
                                date < sinceUtc)
                                continue;

                            List<double> traj = new();

                            if (e.TryGetProperty("win_traj", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
                            {
                                foreach (JsonElement x in arr.EnumerateArray())
                                    if (x.TryGetDouble(out double d))
                                        traj.Add(d);
                            }

                            starts.TryGetValue(id, out var start);

                            last = new GameLog
                            {
                                Result = Str(e, "result"),
                                Opening = start.Bo ?? "",
                                Model = start.Model ?? "",
                                Frames = e.TryGetProperty("frames", out JsonElement fr) && fr.TryGetInt32(out int frameCount) ? frameCount : 0,
                                InferAvgMs = e.TryGetProperty("infer_avg_ms", out JsonElement ms) && ms.TryGetDouble(out double m) ? m : 0,
                                WinTrajectory = traj
                            };
                        }
                    }
                    catch
                    {
                        // Unreadable line - skip it.
                    }
                }

                return last;
            }
            catch
            {
                return null;
            }
        }

        private static string Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }
}
