using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PlutoArena
{
    internal sealed class ArenaException : Exception
    {
        public ArenaException(string message) : base(message) { }
    }

    internal sealed class GameSettings
    {
        public string HumanRace { get; init; } = "Terran";
        public string PlutoRace { get; init; } = "Terran";
        public MapEntry Map { get; init; } = null!;
        public string Difficulty { get; init; } = "Original";
    }

    /// <summary>
    /// Positions of SC:R menu elements in virtual 640x480 coordinates
    /// (measured on SC:R 1.23.10, English client).
    /// </summary>
    internal static class Menu
    {
        // Select Connection
        public const double LanX = 120.7, LanY = 121.3;

        // Registry (profile list): first row, distance between rows
        public const double ProfileX = 85.6, ProfileY0 = 110.2, ProfileStep = 19.3;

        // Big "Ok" button bottom right (Registry, Games, Create, Lobby)
        public const double OkX = 516.3, OkY = 393.3;

        // Games list
        public const double FirstGameX = 98.5, FirstGameY = 102.6;
        public const double CreateGameX = 199.3, CreateGameY = 341.1;

        // Create screen
        public const double GameTypeX = 264.9, GameTypeY = 269.0;
        public const double MeleeX = 198.7, MeleeY = 299.5;

        // Lobby: race dropdown of player row 0 and distance between rows
        public const double RaceX = 224.5, RaceY0 = 53.9, RowStep = 17.0;

        // Race list below an opened dropdown: offset of first item, item height
        public const double RaceOptionX = 205.0, RaceOptionOffset = 13.5, RaceOptionStep = 8.9;
    }

    /// <summary>
    /// Starts Pluto + a second SC:R instance and drives both through the
    /// menus until both players sit in the same Melee lobby.
    /// Runs on a background thread.
    /// </summary>
    internal sealed class ArenaLauncher
    {
        private readonly ArenaConfig config;
        private readonly IProgress<string> status;
        private readonly CancellationToken ct;

        public int PlutoPid { get; private set; }

        /// <summary>Stops the thread that keeps Pluto's StarCraft muted.</summary>
        private CancellationTokenSource? soundStop;
        public int HumanPid { get; private set; }

        private IntPtr plutoHwnd;
        private IntPtr humanHwnd;

        public ArenaLauncher(ArenaConfig config, IProgress<string> status, CancellationToken ct)
        {
            this.config = config;
            this.status = status;
            this.ct = ct;
        }

        private string BridgeLog =>
            Path.Combine(config.NativeFolder, "runtime-multiplayer", "bridge.log");

        // ===============================================================
        // Main flow
        // ===============================================================

        public void Run(GameSettings settings)
        {
            try { File.WriteAllText(LogPath, ""); } catch { }

            bridgeLogPos = 0;
            RunningPlutoVersion = config.PlutoVersion;

            ScrOcr.Initialize();
            ScrOcr.Logger = Log;
            ScrOcr.SaveDebugImages = config.DebugImages;
            ScrOcr.ResetDebug();
            Log($"PLAY  map={settings.Map?.FileName} (index {settings.Map?.Index})  human={settings.HumanRace}  pluto={settings.PlutoRace}  difficulty={settings.Difficulty} (rank {Rank(settings.Difficulty)})  pluto=v{config.PlutoVersion}");
            Log($"OCR available: {ScrOcr.Available}  language={ScrOcr.LanguageTag}");

            if (ScrOcr.Available && !ScrOcr.IsEnglish)
                throw new ArenaException(
                    $"Windows text recognition has no English language (only {ScrOcr.LanguageTag}), " +
                    "so Pluto Arena cannot read the StarCraft menus.\n\nFix: " + ScrOcr.EnglishHelp +
                    "\n\nThen restart Pluto Arena.");

            ValidateFiles(settings);

            status.Report("Activating difficulty...");
            difficulty = settings.Difficulty;
            ActivateDifficulty(settings.Difficulty);
            Log($"DIFFICULTY {settings.Difficulty}: {Path.GetFileName(DifficultyDll(settings.Difficulty))}" +
                (DifficultyDll(settings.Difficulty) == TunedDll
                    ? $"  reaction={Tuning(settings.Difficulty).ReactionMs}ms apm={Tuning(settings.Difficulty).Apm}"
                    : ""));

            status.Report("Starting Pluto...");
            PlutoPid = StartPlutoBridge();

            if (config.MutePlutoSound)
            {
                soundStop?.Cancel();
                soundStop = new CancellationTokenSource();
                ScrAudio.StartGuard(() => PlutoPid, () => HumanPid, soundStop.Token);
            }

            Retry(() => ScrMultiInstance.UnlockExistingInstance(PlutoPid), 20, 250,
                $"Could not unlock the Pluto StarCraft process (PID {PlutoPid}).");

            status.Report("Starting your StarCraft...");

            if (!ScrMultiInstance.LaunchSecondInstance(config.StarCraftExe, out string err, out int humanPid))
                throw new ArenaException("Could not start your StarCraft:\n\n" + err);

            HumanPid = humanPid;

            plutoHwnd = ScrInput.WaitForMainWindow(PlutoPid, 30000, ct);
            humanHwnd = ScrInput.WaitForMainWindow(HumanPid, 30000, ct);

            Log("WINDOW pluto: " + ScrInput.DescribeWindow(plutoHwnd));
            Log("WINDOW human: " + ScrInput.DescribeWindow(humanHwnd));

            if (plutoHwnd == IntPtr.Zero)
                throw new ArenaException("Pluto StarCraft window did not appear.");

            if (humanHwnd == IntPtr.Zero)
                throw new ArenaException("Your StarCraft window did not appear.");

            // ---- Human: create the game -----------------------------------
            status.Report("Creating the game...");
            // Only the StarCraft window being driven stays on screen, so the
            // screen reading never sees the other one.
            ScrInput.Minimize(plutoHwnd);

            WaitForMainMenu(humanHwnd);
            GoToLanGames(humanHwnd, config.HumanProfile, config.HumanProfileIndex);
            CreateGame(humanHwnd, settings.Map);
            SetRace(humanHwnd, 0, settings.HumanRace);

            // ---- Pluto: join the game ------------------------------------
            status.Report("Pluto is joining...");
            ScrOcr.ResetDebug(); // debug pictures for the Pluto part
            Log("PLUTO PART  " + ScrInput.DescribeWindow(plutoHwnd));

            ScrInput.Minimize(humanHwnd);
            ScrInput.Activate(plutoHwnd); // restores it from the taskbar
            Delay(800);

            WaitForMainMenu(plutoHwnd);
            GoToLanGames(plutoHwnd, config.PlutoProfile, config.PlutoProfileIndex);
            JoinGame(plutoHwnd, config.HumanProfile);
            SetRace(plutoHwnd, FindOwnRow(plutoHwnd, config.PlutoProfile, 1), settings.PlutoRace);

            // ---- Back to the human --------------------------------------
            ScrInput.Activate(humanHwnd); // restores your window
            Delay(800);
            ScrInput.Activate(humanHwnd);
            ScrInput.MoveCursor(humanHwnd, Menu.OkX, Menu.OkY);

            status.Report("Ready - click OK in StarCraft to start!");
        }

        /// <summary>Raised (on a worker thread) with what Pluto wrote in chat.</summary>
        public event Action<string>? PlutoSaid;

        /// <summary>Result of the last game (Known = false if StarCraft was closed mid-game).</summary>
        public GameOutcome Outcome { get; private set; } = GameOutcome.Unknown;

        /// <summary>True when the game itself started (not only the lobby).</summary>
        public bool GameStarted { get; private set; }

        /// <summary>When the game started (UTC), for Pluto 2.0's game log.</summary>
        public DateTime GameStartUtc { get; private set; }

        /// <summary>Screen of your StarCraft (for the chat pop-ups).</summary>
        public Rectangle HumanScreen
        {
            get
            {
                try
                {
                    return humanHwnd != IntPtr.Zero
                        ? Screen.FromHandle(humanHwnd).Bounds
                        : Screen.PrimaryScreen?.Bounds ?? Rectangle.Empty;
                }
                catch
                {
                    return Rectangle.Empty;
                }
            }
        }

        /// <summary>
        /// After Run(): waits for the game to start, optionally hides the
        /// Pluto window, passes on Pluto's chat, reads the result and closes
        /// Pluto when your StarCraft is closed.
        /// </summary>
        /// <summary>Read position in bridge.log, kept across rematches (one log per Pluto process).</summary>
        private long bridgeLogPos;

        /// <summary>True when Supervise stopped at your score screen (both StarCraft still open).</summary>
        public bool AtScoreScreen { get; private set; }

        /// <summary>The Pluto version of the running Pluto (a rematch keeps it).</summary>
        public string RunningPlutoVersion { get; private set; } = "1.0";

        private static readonly string[] ScoreTexts = { "Total Score", "Save Replay", "Watch Replay", "Elapsed Time" };

        /// <summary>
        /// Supervises one game. Returns when
        ///  - your StarCraft shows the score screen (Victory!/Defeat!): AtScoreScreen = true,
        ///    both StarCraft stay open for a rematch, or
        ///  - your StarCraft was closed: Pluto is closed too.
        /// With <paramref name="watchScore"/> = false it only waits for your StarCraft to close.
        /// </summary>
        public void Supervise(bool watchScore = true)
        {
            bool started = false;
            DateTime startUtc = DateTime.UtcNow;
            GameOutcome outcome = GameOutcome.Unknown;
            Stopwatch afterExit = new Stopwatch();
            Stopwatch sinceScoreCheck = Stopwatch.StartNew();
            AtScoreScreen = false;

            while (!ct.IsCancellationRequested)
            {
                bool humanRunning = IsRunning(HumanPid);
                List<string> chat = new();

                foreach (string line in ReadNewLogLines(ref bridgeLogPos))
                {
                    if (!started && line.Contains("bot_on_start_complete"))
                    {
                        started = true;
                        startUtc = DateTime.UtcNow;
                        GameStartUtc = startUtc;
                        status.Report("Game in progress - good luck!");

                        if (config.HidePlutoWindow)
                        {
                            Thread.Sleep(1500);
                            ScrInput.Hide(plutoHwnd);
                            ScrInput.Activate(humanHwnd);
                        }
                    }
                    else if (line.StartsWith("bot_text:"))
                    {
                        chat.Add(line.Substring("bot_text:".Length).Trim());
                    }
                    else if (started && line.Contains("\"stage\":\"match_end\""))
                    {
                        outcome = ParseMatchEnd(line, startUtc);
                        Log($"GAME END  pluto won={!outcome.HumanWon}  frames={outcome.Frames}");
                    }
                }

                if (config.ShowPlutoChat && started)
                {
                    foreach (string message in JoinChat(chat))
                        PlutoSaid?.Invoke(message);
                }

                if (!humanRunning)
                {
                    // Pluto writes the result a moment after you leave: wait for it briefly.
                    if (outcome.Known || !started)
                        break;

                    afterExit.Start();

                    if (afterExit.ElapsedMilliseconds > 4000)
                        break;
                }
                else if (watchScore && started && sinceScoreCheck.ElapsedMilliseconds > 4000 &&
                         ScrInput.IsForeground(humanHwnd))
                {
                    // Your score screen: "Victory!" / "Defeat!" + Total Score, Save/Watch Replay.
                    // Read without touching the focus (your StarCraft is in front anyway).
                    sinceScoreCheck.Restart();
                    // No "want" texts here: the extra high-contrast reading would cost CPU
                    // every few seconds during the game.
                    List<ScreenText>? lines = ScrOcr.Read(humanHwnd, null, activate: false);

                    if (lines != null && ScoreTexts.Count(t => ScrOcr.Find(lines, t) != null) >= 2)
                    {
                        bool victory = ScrOcr.Find(lines, "Victory") != null;
                        bool defeat = ScrOcr.Find(lines, "Defeat") != null;
                        int frames = (int)((DateTime.UtcNow - startUtc).TotalSeconds * 23.81);

                        if (victory != defeat)
                            outcome = new GameOutcome { Known = true, HumanWon = victory, Frames = frames, StartedUtc = startUtc };

                        Log($"SCORE SCREEN  victory={victory} defeat={defeat}: " + Describe(lines));
                        AtScoreScreen = true;
                        break;
                    }
                }

                Thread.Sleep(500);
            }

            Outcome = outcome;
            GameStarted = started;

            if (AtScoreScreen)
            {
                status.Report("Game over");
                return; // both StarCraft stay open (rematch)
            }

            // Your StarCraft is closed (or PlutoArena is closing): end Pluto too,
            // so the next PLAY can start a fresh Pluto process.
            StopPluto();

            // When you leave, Pluto's game only ends while its StarCraft closes:
            // the result is written to the log just now (leaving = Pluto wins).
            if (!outcome.Known && started)
            {
                foreach (string line in ReadNewLogLines(ref bridgeLogPos))
                {
                    if (line.Contains("\"stage\":\"match_end\""))
                    {
                        outcome = ParseMatchEnd(line, startUtc);
                        Log($"GAME END (on close)  pluto won={!outcome.HumanWon}  frames={outcome.Frames}");
                    }
                }
            }

            if (!outcome.Known && started)
                Log("GAME END  no result in the bridge log - not counted");

            Outcome = outcome;
            status.Report("READY");
        }

        /// <summary>
        /// After your score screen: takes Pluto's StarCraft out of its game and
        /// back to the LAN games list (score screen -> Ok, or F10 -> End Mission
        /// -> Quit when Pluto is still in the game). Pluto 2.0 writes its game
        /// log (win estimate) at that moment.
        /// </summary>
        public void PlutoLeaveGame()
        {
            Log("PLUTO LEAVE  " + ScrInput.DescribeWindow(plutoHwnd));
            ScrInput.Show(plutoHwnd);
            ScrInput.Minimize(humanHwnd);
            ScrInput.Activate(plutoHwnd);
            Delay(800);

            try
            {
                for (int attempt = 0; attempt < 14; attempt++)
                {
                    ct.ThrowIfCancellationRequested();

                    List<ScreenText>? lines = ScrOcr.Read(plutoHwnd);
                    Log($"  pluto screen ({attempt}): " + Describe(lines));

                    if (lines == null)
                    {
                        Delay(1000);
                        continue;
                    }

                    if (ScrOcr.Find(lines, "Create Game", "Game Title", "Map Size") != null)
                        return; // games list - done

                    if (ScoreTexts.Count(t => ScrOcr.Find(lines, t) != null) >= 2)
                        ScrInput.Key(plutoHwnd, Keys.Enter); // score screen -> Ok
                    else if (!ClickLine(plutoHwnd, lines, "Quit Mission") &&
                             !ClickLine(plutoHwnd, lines, "Surrender") &&
                             !ClickLine(plutoHwnd, lines, "End Mission"))
                        ScrInput.Key(plutoHwnd, Keys.F10); // still in the game: open the game menu

                    Delay(1500);
                }

                throw new ArenaException("Pluto's StarCraft did not leave the game. Close both StarCraft and press PLAY.");
            }
            finally
            {
                ScrInput.Activate(humanHwnd);
            }
        }

        /// <summary>Clicks the centre of the first line that contains the text (any size).</summary>
        private bool ClickLine(IntPtr hwnd, List<ScreenText> lines, string text)
        {
            ScreenText? hit = ScrOcr.Find(lines, text);

            if (hit == null)
                return false;

            Log($"  click '{text}' at {hit.Center.X:0},{hit.Center.Y:0}");
            ScrInput.Click(hwnd, hit.Center.X, hit.Center.Y);
            return true;
        }

        /// <summary>
        /// Rematch: both StarCraft are open, yours on the score screen and
        /// Pluto's on the games list. Creates the next game with the current
        /// settings; the difficulty goes to bridge.ini (read by the bridge at
        /// the start of every game).
        /// </summary>
        public void Rematch(GameSettings settings)
        {
            Log($"REMATCH  map={settings.Map?.FileName}  human={settings.HumanRace}  pluto={settings.PlutoRace}  difficulty={settings.Difficulty} (rank {Rank(settings.Difficulty)})");
            difficulty = settings.Difficulty;
            WriteTuning(settings.Difficulty);

            // ---- You: score screen -> games list -> new game -------------
            status.Report("Creating the game...");
            ScrInput.Minimize(plutoHwnd);
            ScrInput.Activate(humanHwnd);
            Delay(600);

            Step(humanHwnd, "leave the score screen",
                () => ScrInput.Key(humanHwnd, Keys.Enter),
                2500,
                expected: new[] { "Create Game", "Game Title", "Map Size", "Game Speed" },
                current: ScoreTexts);

            CreateGame(humanHwnd, settings.Map!);
            SetRace(humanHwnd, 0, settings.HumanRace);

            // ---- Pluto: games list -> join -------------------------------
            status.Report("Pluto is joining...");
            ScrInput.Minimize(humanHwnd);
            ScrInput.Show(plutoHwnd);
            ScrInput.Activate(plutoHwnd);
            Delay(800);

            JoinGame(plutoHwnd, config.HumanProfile);
            SetRace(plutoHwnd, FindOwnRow(plutoHwnd, config.PlutoProfile, 1), settings.PlutoRace);

            // ---- Back to you ---------------------------------------------
            ScrInput.Activate(humanHwnd);
            Delay(800);
            ScrInput.Activate(humanHwnd);
            ScrInput.MoveCursor(humanHwnd, Menu.OkX, Menu.OkY);

            status.Report("Ready - click OK in StarCraft to start!");
        }

        /// <summary>
        /// Writes reaction_ms / apm for the next game into bridge.ini (UTF-16,
        /// written by start-pluto.ps1). The bridge reads it when a game starts.
        /// </summary>
        private void WriteTuning(string difficulty)
        {
            var (reactionMs, apm) = Tuning(difficulty);
            string ini = Path.Combine(config.NativeFolder, "runtime-multiplayer", "bridge.ini");

            try
            {
                List<string> lines = File.Exists(ini)
                    ? File.ReadAllLines(ini).Where(l =>
                        !l.TrimStart().StartsWith("reaction_ms=", StringComparison.OrdinalIgnoreCase) &&
                        !l.TrimStart().StartsWith("apm=", StringComparison.OrdinalIgnoreCase)).ToList()
                    : new List<string> { "[pluto]" };

                lines.Add($"reaction_ms={reactionMs}");
                lines.Add($"apm={apm}");
                File.WriteAllLines(ini, lines, Encoding.Unicode);
                Log($"TUNING  bridge.ini reaction_ms={reactionMs} apm={apm}");
            }
            catch (Exception ex)
            {
                Log("TUNING  could not write bridge.ini: " + ex.Message);
            }
        }

        /// <summary>
        /// The bridge prints Pluto's chat in pieces of one screen line. A new
        /// message starts with "Pluto" or after a line that ends a sentence.
        /// Technical notes (latency) are not shown.
        /// </summary>
        private static IEnumerable<string> JoinChat(List<string> lines)
        {
            List<string> messages = new();
            StringBuilder current = new StringBuilder();

            foreach (string raw in lines)
            {
                string line = raw.Trim();

                if (line.Length == 0)
                    continue;

                bool newMessage = current.Length == 0 ||
                                  line.StartsWith("Pluto", StringComparison.OrdinalIgnoreCase) ||
                                  ".!?".Contains(current[current.Length - 1]);

                if (newMessage && current.Length > 0)
                {
                    messages.Add(current.ToString());
                    current.Clear();
                }

                if (current.Length > 0)
                    current.Append(' ');

                current.Append(line);
            }

            if (current.Length > 0)
                messages.Add(current.ToString());

            foreach (string m in messages)
            {
                if (m.Contains("latency", StringComparison.OrdinalIgnoreCase))
                    continue;

                yield return m.StartsWith("Pluto:", StringComparison.OrdinalIgnoreCase) ? m.Substring(6).Trim() : m;
            }
        }

        private static GameOutcome ParseMatchEnd(string line, DateTime startUtc)
        {
            Match winner = Regex.Match(line, "\"winner\":(true|false)");
            Match frame = Regex.Match(line, "\"frame\":(\\d+)");

            if (!winner.Success)
                return GameOutcome.Unknown;

            return new GameOutcome
            {
                Known = true,
                HumanWon = winner.Groups[1].Value == "false", // "winner" is Pluto's result
                Frames = frame.Success ? int.Parse(frame.Groups[1].Value) : 0,
                StartedUtc = startUtc
            };
        }

        /// <summary>Lines added to bridge.log since <paramref name="position"/>.</summary>
        private List<string> ReadNewLogLines(ref long position)
        {
            List<string> lines = new();

            try
            {
                if (!File.Exists(BridgeLog))
                    return lines;

                using FileStream fs = new FileStream(BridgeLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                if (fs.Length < position)
                    position = 0; // new log file

                fs.Seek(position, SeekOrigin.Begin);

                using StreamReader reader = new StreamReader(fs, Encoding.UTF8);
                string rest = reader.ReadToEnd();

                // Keep an unfinished last line for the next round.
                int lastNewline = rest.LastIndexOf('\n');

                if (lastNewline < 0)
                    return lines;

                string complete = rest.Substring(0, lastNewline + 1);
                position += Encoding.UTF8.GetByteCount(complete);

                foreach (string line in complete.Split('\n'))
                {
                    string l = line.TrimEnd('\r');

                    if (l.Length > 0)
                        lines.Add(l);
                }
            }
            catch
            {
            }

            return lines;
        }

        public void StopPluto()
        {
            try
            {
                // Windows remembers the mixer setting per program (StarCraft.exe):
                // switch Pluto's sound back on before it closes, so your next
                // StarCraft does not start muted.
                soundStop?.Cancel();

                if (PlutoPid == 0 || !IsRunning(PlutoPid))
                    return;

                ScrAudio.SetMute(PlutoPid, false);

                IntPtr hwnd = ScrInput.FindMainWindow(PlutoPid, includeHidden: true);
                ScrInput.Show(hwnd);

                using Process p = Process.GetProcessById(PlutoPid);
                p.CloseMainWindow();

                if (!p.WaitForExit(5000))
                    p.Kill();
            }
            catch
            {
            }
        }

        // ===============================================================
        // Preparation
        // ===============================================================

        private void ValidateFiles(GameSettings settings)
        {
            void Need(bool ok, string message)
            {
                if (!ok) throw new ArenaException(message);
            }

            Need(File.Exists(config.StarCraftExe), $"StarCraft.exe not found:\n{config.StarCraftExe}");
            Need(SetupCheck.IsSupportedStarCraft(config.StarCraftExe),
                $"Your StarCraft is not version {SetupCheck.SupportedStarCraftVersion}.\n\n" +
                "Blizzard has probably released a new StarCraft patch. The Pluto bridge only works with " +
                $"version {SetupCheck.SupportedStarCraftVersion}, so Pluto cannot be started until the bridge is updated for the new version.");
            Need(File.Exists(config.ActivePlutoDll),
                $"pluto.dll for Pluto {config.PlutoVersion} not found:\n{config.ActivePlutoDll}\n\nSet it in SETUP.");
            Need(Directory.Exists(config.NativeFolder), $"Native folder not found:\n{config.NativeFolder}");
            Need(File.Exists(StartScript), $"start-pluto.ps1 not found:\n{StartScript}");
            Need(File.Exists(DifficultyDll(settings.Difficulty)), $"Difficulty DLL not found:\n{DifficultyDll(settings.Difficulty)}");
            Need(Directory.Exists(config.MapsFolder), $"Pluto Arena maps folder not found:\n{config.MapsFolder}");
            Need(settings.Map != null, "No map selected.");
        }

        private string StartScript =>
            Path.Combine(config.NativeFolder, "scripts", "start-pluto.ps1");

        /// <summary>
        /// New difficulty bridge: one DLL, tuned with a reaction delay and an
        /// APM limit (commands wait instead of being dropped). Used when
        /// modes\pluto-scr-tuned.dll exists; otherwise the old per-level DLLs.
        /// </summary>
        private string TunedDll => Path.Combine(config.ModesFolder, "pluto-scr-tuned.dll");

        /// <summary>
        /// Difficulty ranks like the SC:R ladder. The rank is the first letter
        /// of the list entry ("S - Original", "A", ... "F - Beginner").
        /// </summary>
        public static readonly string[] Ranks =
        {
            "S  -  Original (full strength)", "A", "B", "C", "D", "E", "F  -  Beginner"
        };

        public static string Rank(string difficulty)
        {
            string d = (difficulty ?? "").Trim();

            // Older names (Original, Medium, Easy ...) map to the nearest rank.
            return d switch
            {
                "" or "Original" => "S",
                "Medium" => "B",
                "Easy" => "D",
                "Easy+" => "E",
                "Very Easy" => "F",
                _ => d.Substring(0, 1).ToUpperInvariant()
            };
        }

        /// <summary>
        /// Reaction delay (ms) and APM limit (0 = no limit) per rank.
        /// Upper ranks differ only in reaction time (fine tuning); the APM
        /// limit starts at C (cuts fight bursts) and slows the economy from D.
        /// </summary>
        public static (int ReactionMs, int Apm) Tuning(string difficulty) => Rank(difficulty) switch
        {
            "A" => (80, 0),
            "B" => (170, 0),
            "C" => (250, 220),
            "D" => (400, 170),
            "E" => (600, 130),
            "F" => (800, 100),
            _ => (0, 0) // S: Original, no limits
        };

        private string DifficultyDll(string difficulty)
        {
            string rank = Rank(difficulty);

            // The original bridge only knows Pluto 1.0; Pluto 2.0 always runs
            // through the tuned bridge (rank S = no delay, no APM limit).
            // The tuned bridge also plays rank S (no delay, no APM limit) and reads
            // the rank again for every game, which a rematch needs.
            if (File.Exists(TunedDll))
                return TunedDll;

            // Fallback: the old per-level DLLs (Pluto Arena 1.0 difficulty pack).
            return Path.Combine(config.ModesFolder, rank switch
            {
                "A" or "B" or "C" => "pluto-scr-medium.dll",
                "D" => "pluto-scr-easy.dll",
                "E" => "pluto-scr-easyplus.dll",
                "F" => "pluto-scr-veryeasy.dll",
                _ => "pluto-scr-original.dll"
            });
        }

        private string difficulty = "Original";

        private void ActivateDifficulty(string difficulty)
        {
            // start-pluto.ps1 copies native\bin\pluto-scr.dll into
            // runtime-multiplayer and injects it, so this is the file to replace.
            string target = Path.Combine(config.NativeFolder, "bin", "pluto-scr.dll");

            try
            {
                File.Copy(DifficultyDll(difficulty), target, true);
            }
            catch (Exception ex)
            {
                throw new ArenaException(
                    "Could not activate difficulty (is a StarCraft still running?):\n\n" + ex.Message);
            }
        }

        private int StartPlutoBridge()
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                WorkingDirectory = config.NativeFolder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (string arg in new[]
            {
                "-NoProfile", "-ExecutionPolicy", "Bypass",
                "-File", StartScript,
                "-Mode", "Multiplayer",
                "-StarCraft", config.StarCraftExe,
                "-Pluto", config.ActivePlutoDll,
                "-SpeedMs", "0",
                "-DrawGraph"
            })
            {
                psi.ArgumentList.Add(arg);
            }

            psi.Environment["BWRL_FRAME_BUDGET_MS"] = "10";

            // Read by pluto-scr-tuned.dll inside Pluto's StarCraft
            // (PowerShell and StarCraft inherit this environment).
            var (reactionMs, apm) = Tuning(difficulty);
            psi.Environment["PLUTO_REACTION_MS"] = reactionMs.ToString();
            psi.Environment["PLUTO_APM"] = apm.ToString();

            using Process? bridge = Process.Start(psi);

            if (bridge == null)
                throw new ArenaException("Could not start powershell.exe.");

            StringBuilder output = new StringBuilder();
            bridge.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            bridge.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
            bridge.BeginOutputReadLine();
            bridge.BeginErrorReadLine();

            Stopwatch sw = Stopwatch.StartNew();

            while (!bridge.WaitForExit(250))
            {
                if (ct.IsCancellationRequested || sw.ElapsedMilliseconds > 60000)
                {
                    try { bridge.Kill(true); } catch { }
                    throw new ArenaException("Pluto bridge did not initialize within 60 seconds.");
                }
            }

            bridge.WaitForExit();

            string text;
            lock (output) text = output.ToString().Trim();

            if (bridge.ExitCode != 0)
                throw new ArenaException($"Pluto bridge failed (exit code {bridge.ExitCode}):\n\n{Shorten(text)}");

            // "...bridge ready in StarCraft process 14536. Choose Melee ..."
            Match m = Regex.Match(text, @"StarCraft process (\d+)");

            if (m.Success && int.TryParse(m.Groups[1].Value, out int pid) && IsRunning(pid))
                return pid;

            // Fallback: the only StarCraft running now is Pluto's.
            Process[] sc = Process.GetProcessesByName("StarCraft");

            if (sc.Length == 1)
                return sc[0].Id;

            throw new ArenaException("Pluto StarCraft process was not found.\n\n" + Shorten(text));
        }

        // ===============================================================
        // Menu steps
        // ===============================================================

        private void WaitForMainMenu(IntPtr hwnd)
        {
            ScrInput.Activate(hwnd);

            bool? seen = ScrOcr.WaitFor(hwnd, 45000, ct, "Single Player", "Multiplayer");
            Log($"MAIN MENU seen={seen?.ToString() ?? "no OCR"}  capture={ScrOcr.CaptureMethod}  captureFailed={ScrOcr.CaptureFailed}");

            if (ScrOcr.CaptureFailed)
            {
                // Without reading the screen the map folder cannot be checked,
                // so stop instead of clicking blindly.
                throw new ArenaException(
                    "Pluto Arena cannot see the StarCraft screen.\n\n" +
                    "In StarCraft go to Options > Graphics and set\n" +
                    "Display Mode = Windowed (Fullscreen), then press PLAY again.");
            }

            if (seen == null)
                Delay(6000); // no OCR: give SC:R time to finish loading
            else if (seen == false)
                throw new ArenaException("StarCraft main menu did not appear.");
            else
                Delay(SettleMs);
        }

        private void GoToLanGames(IntPtr hwnd, string profile, int profileIndex)
        {
            // Main menu -> Multiplayer
            Step(hwnd, "Multiplayer",
                () => ScrInput.Key(hwnd, Keys.M),
                2000,
                expected: new[] { "Select Game Type", "Expansion" },
                current: new[] { "Single Player" });

            // Brood War expansion. Select Connection has a bright moon picture
            // that OCR cannot read on some PCs: once "Select Game Type" is
            // gone, continue anyway.
            Step(hwnd, "Brood War",
                () => ScrInput.Key(hwnd, Keys.E),
                2500,
                expected: new[] { "Select Connection", "Local Area Network" },
                current: new[] { "Select Game Type" },
                assumeAfterMs: 6000);

            // Local Area Network
            int lanAttempt = 0;

            Step(hwnd, "Local Area Network",
                () =>
                {
                    // 2nd try: double-click the LAN row if it can be read.
                    if (lanAttempt++ % 2 == 1 && TryClickText(hwnd, "Local Area Network", leftListOnly: true, doubleClick: true))
                        return;

                    // The list has two rows (Battle.net, LAN): DOWN twice
                    // always ends on LAN, whatever was selected before.
                    ScrInput.Key(hwnd, Keys.Down, 2);
                    Delay(250);
                    ScrInput.Key(hwnd, Keys.Enter);
                },
                2500,
                expected: new[] { "Registry", "New ID" },
                current: new[] { "Select Connection" },
                retryAfterMs: 8000);

            // Create the profile if this PC does not have it yet.
            EnsureProfile(hwnd, profile);

            // Profile -> Ok
            Step(hwnd, "profile " + profile,
                () =>
                {
                    if (!TryClickText(hwnd, profile, leftListOnly: true))
                    {
                        // Keyboard: first profile, then down to its position.
                        ScrInput.Key(hwnd, Keys.Home);
                        Delay(120);

                        if (profileIndex > 0)
                            ScrInput.Key(hwnd, Keys.Down, profileIndex);
                    }

                    Delay(250);
                    ScrInput.Click(hwnd, Menu.OkX, Menu.OkY);
                },
                3000,
                // Games screen: list on the left, game details on the right.
                expected: new[] { "Create Game", "Game Title", "Map Size", "Game Speed" },
                current: new[] { "Registry", "New ID" },
                budgetMs: 60000); // the 2nd StarCraft can take long to start its LAN connection
        }

        private void CreateGame(IntPtr hwnd, MapEntry map)
        {
            int attempt = 0;

            // "Game Type" is also on the Games screen, so wait for texts
            // that exist only on the Create screen.
            Step(hwnd, "Create Game",
                () =>
                {
                    // G is the hotkey of "Create Game"; click as a second try.
                    if (attempt++ % 2 == 0)
                        ScrInput.Key(hwnd, Keys.G);
                    else
                        ClickText(hwnd, Menu.CreateGameX, Menu.CreateGameY, "Create Game");
                },
                2500,
                expected: new[] { "Suggested Players", "One Level", "Tileset", "Teams:", "[Replays]", "[scenario]", "Suggested" },
                current: new[] { "Create Game" },
                // The games list is gone but the new screen is misread: go on,
                // EnterMapsFolder checks the screen again anyway.
                assumeAfterMs: 6000);

            EnterMapsFolder(hwnd);

            // [Up One Level] is the first row, so map N is N+1 rows below it.
            ScrInput.Key(hwnd, Keys.Home);
            Delay(150);
            ScrInput.Key(hwnd, Keys.Down, map.Index + 1);
            Delay(500);

            SelectMelee(hwnd);

            Step(hwnd, "create the game",
                () => ScrInput.Click(hwnd, Menu.OkX, Menu.OkY),
                4000,
                expected: LobbyTexts,
                current: new[] { "Game Type" });
        }

        /// <summary>Sets Game Type = Melee and checks it (Pluto supports Melee only).</summary>
        private void SelectMelee(IntPtr hwnd)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                ScrInput.Click(hwnd, Menu.GameTypeX, Menu.GameTypeY);
                Delay(500);
                ScrInput.Click(hwnd, Menu.MeleeX, Menu.MeleeY);
                Delay(500);

                List<ScreenText>? lines = ScrOcr.Read(hwnd);

                if (lines == null)
                    return; // cannot check

                // The selected game type is shown right of "Game Type" (y ~ 269).
                ScreenText? shown = lines.FirstOrDefault(l =>
                    l.Center.X > 150 && l.Center.X < 330 &&
                    Math.Abs(l.Center.Y - Menu.GameTypeY) < 9);

                Log($"GAME TYPE shown: {shown?.Text ?? "(not read)"}");

                if (shown != null && ScrOcr.Normalize(shown.Text).Contains("melee"))
                    return;

                if (shown == null && ScrOcr.Find(lines, "Top vs Bottom", "Teams:") == null)
                    return; // nothing contradicts Melee
            }

            throw new ArenaException("Could not set Game Type to Melee in StarCraft.");
        }

        /// <summary>
        /// SC:R opens the folder used last time. Make sure we are in
        /// Maps\000_PlutoArena before picking the map.
        ///
        /// Only HOME + ENTER is used to move between folders: HOME always
        /// selects a folder ([Up One Level] in a subfolder, 000_PlutoArena -
        /// the first folder - in the root), so ENTER opens it and never
        /// starts the game by accident.
        /// </summary>
        private void EnterMapsFolder(IntPtr hwnd)
        {
            string folder = config.MapsFolderName;
            List<string> mapNames = config.LoadMaps()
                .Select(m => m.DisplayName)
                .Where(n => ScrOcr.Normalize(n).Length >= 5)
                .ToList();

            for (int attempt = 0; attempt < 8; attempt++)
            {
                List<ScreenText>? lines = ScrOcr.Read(hwnd);

                if (lines == null)
                    return; // No OCR: SC:R normally reopens 000_PlutoArena.

                Log("MAPS FOLDER screen: " + Describe(lines));

                if (ScrOcr.Find(lines, "Select Race", "Observers") != null)
                    throw new ArenaException("StarCraft created the game too early (before the map was chosen). Press PLAY again.");

                // Header line (folder name) sits between "Create" and the list.
                bool headerIsArena = lines.Any(l =>
                    l.Center.X < 330 && l.Center.Y > 95 && l.Center.Y < 130 &&
                    ScrOcr.Matches(l.Text, StripPrefix(folder)));

                // Or: several of our map files are listed (small misreadings allowed).
                int ownMaps = mapNames.Count(n =>
                    lines.Any(l => ScrOcr.Matches(l.Text, n)));

                bool inSubfolder = ScrOcr.Find(lines, "One Level", "Up One") != null;

                Log($"  header={headerIsArena} ownMaps={ownMaps} subfolder={inSubfolder}");

                if (headerIsArena || ownMaps >= 2)
                    return;

                // Other subfolder -> HOME selects [Up One Level], ENTER goes up.
                // Root folder     -> HOME selects 000_PlutoArena, ENTER opens it.
                ScrInput.Key(hwnd, Keys.Home);
                Delay(200);
                ScrInput.Key(hwnd, Keys.Enter);
                Delay(900);
            }

            throw new ArenaException($"Could not open the Maps\\{folder} folder in StarCraft.");
        }

        /// <summary>Texts of the game lobby: player list (left) or game details (right).</summary>
        private static readonly string[] LobbyTexts =
            { "Select Race", "Observers", "Turn Rate", "Unit Limits", "Custom Hotkeys", "Game Name" };

        // New ID button on the Registry screen
        private const double NewIdX = 100.8, NewIdY = 344.6;

        /// <summary>
        /// Makes sure the LAN profile exists in the Registry list. On a PC
        /// where it is missing (e.g. a friend's PC without "PlutoAi") it is
        /// created with New ID -> type name -> Enter.
        /// </summary>
        private void EnsureProfile(IntPtr hwnd, string profile)
        {
            List<ScreenText>? lines = ScrOcr.Read(hwnd);

            if (lines == null)
                return; // cannot check without OCR; index fallback is used

            if (HasProfile(lines, profile))
                return;

            // Registry may still be animating in - look once more.
            Delay(600);
            lines = ScrOcr.Read(hwnd);

            if (lines == null || HasProfile(lines, profile))
                return;

            Log($"PROFILE '{profile}' not found ({Describe(lines)}) - creating it with New ID");

            ClickText(hwnd, NewIdX, NewIdY, "New ID");
            Delay(900);

            ScrInput.TypeText(hwnd, profile);
            Delay(300);
            ScrInput.Key(hwnd, Keys.Enter);
            Delay(1200);

            lines = ScrOcr.Read(hwnd);
            Log("  after New ID: " + Describe(lines));

            if (lines != null && !HasProfile(lines, profile))
            {
                throw new ArenaException(
                    $"Could not create the LAN profile \"{profile}\" automatically.\n\n" +
                    "Create it once by hand in StarCraft:\n" +
                    "Multiplayer > Expansion > Local Area Network > New ID,\n" +
                    "then press PLAY again.");
            }
        }

        private static bool HasProfile(List<ScreenText> lines, string profile)
        {
            string wanted = ScrOcr.Normalize(profile);

            return lines.Any(l =>
                l.Center.X < 320 && l.Center.Y > 90 && l.Center.Y < 330 &&
                ScrOcr.Normalize(l.Text) is string n &&
                n.Contains(wanted) && n.Length <= wanted.Length + 6);
        }

        private void JoinGame(IntPtr hwnd, string gameName)
        {
            // LAN games can take a moment to show up in the list.
            bool? seen = ScrOcr.WaitFor(hwnd, 15000, ct, gameName);
            Log($"JOIN game '{gameName}' visible={seen?.ToString() ?? "no OCR"}");

            if (seen == null)
                Delay(2500);
            else if (seen == false)
                Log("  game name not read - joining the first game in the list");

            Step(hwnd, "join the game (is your game in Pluto's LAN list?)",
                () =>
                {
                    // The game name is also shown in the right panel - click the list entry.
                    ClickText(hwnd, Menu.FirstGameX, Menu.FirstGameY, gameName, leftListOnly: true);
                    Delay(300);
                    ScrInput.Click(hwnd, Menu.OkX, Menu.OkY);
                },
                5000,
                expected: LobbyTexts,
                current: new[] { "Create Game", "Game Title" });
        }

        /// <summary>Which player row (0..7) of the lobby belongs to this profile.</summary>
        private int FindOwnRow(IntPtr hwnd, string profile, int fallback)
        {
            List<ScreenText>? lines = ScrOcr.Read(hwnd);

            ScreenText? hit = lines?
                .Where(l => l.Center.X < 300 && l.Center.Y < 230)
                .FirstOrDefault(l => ScrOcr.Normalize(l.Text).Contains(ScrOcr.Normalize(profile)));

            if (hit == null)
                return fallback;

            int row = (int)Math.Round((hit.Center.Y - Menu.RaceY0) / Menu.RowStep);
            return Math.Clamp(row, 0, 7);
        }

        private void SetRace(IntPtr hwnd, int row, string race)
        {
            int option = race switch
            {
                "Zerg" => 0,
                "Terran" => 1,
                "Protoss" => 2,
                _ => 3 // Random
            };

            double y = Menu.RaceY0 + Menu.RowStep * row;
            Log($"RACE row {row} -> {race}");

            ScrInput.Click(hwnd, Menu.RaceX, y);
            Delay(400);
            ScrInput.Click(hwnd, Menu.RaceOptionX, y + Menu.RaceOptionOffset + Menu.RaceOptionStep * option);
            Delay(300);
        }

        // ===============================================================
        // Helpers
        // ===============================================================

        /// <summary>Clicks a text found by OCR, or the fixed position if not found.</summary>
        private void ClickText(IntPtr hwnd, double vx, double vy, string text, bool leftListOnly = false)
        {
            if (!TryClickText(hwnd, text, leftListOnly))
                ScrInput.Click(hwnd, vx, vy);
        }

        /// <summary>
        /// Clicks a line of text found by OCR - but only a clean match:
        /// one row high and without much other text in it. OCR sometimes
        /// merges two list rows into one line; clicking the middle of such
        /// a line would hit the wrong row (e.g. Battle.net instead of LAN).
        /// </summary>
        private bool TryClickText(IntPtr hwnd, string text, bool leftListOnly = false, bool doubleClick = false)
        {
            ScrInput.Activate(hwnd);

            List<ScreenText>? lines = ScrOcr.Read(hwnd, new[] { text });

            if (lines == null)
                return false;

            string wanted = ScrOcr.Normalize(text);

            ScreenText? hit = lines.FirstOrDefault(l =>
            {
                if (leftListOnly && l.Center.X >= 320)
                    return false;

                string n = ScrOcr.Normalize(l.Text);

                return n.Contains(wanted)
                    && n.Length <= wanted.Length + 6
                    && l.Bounds.Height < 14;
            });

            Log($"  click text '{text}': {(hit == null ? "not found cleanly" : $"'{hit.Text}' at {hit.Center.X:0},{hit.Center.Y:0}")}");

            if (hit == null)
                return false;

            ScrInput.Click(hwnd, hit.Center.X, hit.Center.Y);

            if (doubleClick)
            {
                Thread.Sleep(90);
                ScrInput.Click(hwnd, hit.Center.X, hit.Center.Y);
            }

            return true;
        }

        /// <summary>
        /// Does one menu action and checks that the expected screen appears.
        /// SC:R ignores input while a screen is still sliding in, so the
        /// action is repeated (up to 3 times) if nothing changed.
        /// Without OCR the action runs once followed by a fixed wait.
        /// </summary>
        /// <param name="assumeAfterMs">When &gt; 0: if the old screen is gone for this
        /// long but the new one cannot be read (bright background, OCR garbage),
        /// continue as if it had been seen.</param>
        /// <param name="retryAfterMs">When &gt; 0: repeat the action after this long
        /// even if the old screen cannot be recognised.</param>
        private void Step(IntPtr hwnd, string name, Action action, int fallbackMs,
            string[] expected, string[] current, int budgetMs = 25000,
            int assumeAfterMs = 0, int retryAfterMs = 0)
        {
            int actions = 0;
            Stopwatch total = Stopwatch.StartNew();

            while (actions < 3 && total.ElapsedMilliseconds < budgetMs)
            {
                ct.ThrowIfCancellationRequested();

                actions++;
                Log($"STEP {name} (attempt {actions}) -> expecting: {string.Join(" / ", expected)}");
                action();
                Stopwatch sinceAction = Stopwatch.StartNew();

                // Wait for the next screen. While the screen is in between
                // (blank, "connecting...") keep waiting; repeat the action
                // only if we are clearly still on the old screen.
                while (total.ElapsedMilliseconds < budgetMs)
                {
                    bool? seen = ScrOcr.WaitFor(hwnd, 2500, ct, expected);

                    if (seen == null)
                    {
                        Log("  no OCR, fixed wait");
                        Delay(fallbackMs);
                        return;
                    }

                    if (seen == true)
                    {
                        Log("  ok");
                        Delay(SettleMs);
                        return;
                    }

                    List<ScreenText>? lines = ScrOcr.Read(hwnd, expected);

                    // The screen may have changed just now.
                    if (ScrOcr.Find(lines, expected) != null)
                    {
                        Log("  ok (late)");
                        Delay(SettleMs);
                        return;
                    }

                    // "Creating game...", "Connecting..." overlays sit on top of the
                    // old screen: that is progress, not a missed click.
                    bool busy = ScrOcr.Find(lines, "Creating", "Connecting", "Joining", "Please wait") != null;
                    bool stillOld = !busy && ScrOcr.Find(lines, current) != null;

                    Log($"  not yet ({(stillOld ? "still on old screen" : "in between")}). Screen: " + Describe(lines));
                    Log("    " + ScrInput.DescribeWindow(hwnd));

                    if (stillOld)
                        break; // repeat the action

                    if (assumeAfterMs > 0 && lines != null && lines.Count > 0 &&
                        sinceAction.ElapsedMilliseconds >= assumeAfterMs)
                    {
                        Log("  old screen is gone, new one not readable - assuming it is there");
                        Delay(SettleMs);
                        return;
                    }

                    if (retryAfterMs > 0 && sinceAction.ElapsedMilliseconds >= retryAfterMs)
                        break; // repeat the action (screen not recognisable)
                }
            }

            throw new ArenaException(
                $"StarCraft did not react to the step \"{name}\".\n\n" +
                "Try PLAY again. If it keeps failing at the same step, send\n" +
                "PlutoArena.log (next to PlutoArena.exe) - set DebugImages=1 in\n" +
                "PlutoArena.ini first to include screen pictures in the debug folder.");
        }

        /// <summary>Time a freshly shown SC:R screen needs to finish its slide-in animation.</summary>
        private const int SettleMs = 700;

        /// <summary>Waits for a screen; without OCR waits the given time.</summary>
        private void WaitScreen(IntPtr hwnd, int fallbackMs, params string[] texts)
        {
            bool? seen = ScrOcr.WaitFor(hwnd, 6000, ct, texts);

            if (seen == true)
                Delay(SettleMs); // let the screen finish its animation
            else
                Delay(fallbackMs); // OCR unavailable or text not recognised: carry on
        }

        private static readonly object logLock = new();

        public static string LogPath =>
            Path.Combine(AppContext.BaseDirectory, "PlutoArena.log");

        public static void Log(string message)
        {
            try
            {
                lock (logLock)
                    File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        private static string Describe(List<ScreenText>? lines) =>
            lines == null
                ? "(OCR unavailable)"
                : string.Join(" | ", lines.Take(14).Select(l => l.Text));

        private void Delay(int ms)
        {
            int total = (int)(ms * config.DelayFactor);

            if (ct.WaitHandle.WaitOne(total))
                ct.ThrowIfCancellationRequested();
        }

        private void Retry(Func<bool> action, int attempts, int delayMs, string error)
        {
            for (int i = 0; i < attempts; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (action())
                    return;

                Thread.Sleep(delayMs);
            }

            throw new ArenaException(error);
        }

        private bool LogContains(string text)
        {
            try
            {
                if (!File.Exists(BridgeLog))
                    return false;

                using FileStream fs = new FileStream(BridgeLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using StreamReader reader = new StreamReader(fs);

                // The interesting lines are at the top; don't read megabytes.
                char[] buffer = new char[16384];
                int read = reader.Read(buffer, 0, buffer.Length);

                return new string(buffer, 0, read).Contains(text);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsRunning(int pid)
        {
            try
            {
                using Process p = Process.GetProcessById(pid);
                return !p.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static string StripPrefix(string folder)
        {
            int i = folder.IndexOf('_');
            return i >= 0 ? folder[(i + 1)..] : folder;
        }

        private static string Shorten(string text) =>
            text.Length > 1500 ? text[^1500..] : text;
    }
}
