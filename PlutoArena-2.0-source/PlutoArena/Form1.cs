using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PlutoArena
{
    public partial class Form1 : Form
    {
        private readonly ArenaConfig config = ArenaConfig.Load();

        private string humanRace = "Terran";
        private string plutoRace = "Terran";

        private readonly Dictionary<string, RaceButton> humanButtons = new();
        private readonly Dictionary<string, RaceButton> plutoButtons = new();

        private DarkSelect? mapSelect;
        private DarkSelect? difficultySelect;
        private DarkSelect? versionSelect;
        private GlowButton? playButton;
        private ArenaCheck? hideCheck;
        private ArenaCheck? muteCheck;
        private ArenaCheck? ladderCheck;

        /// <summary>Your results against Pluto (PlutoArena-stats.txt).</summary>
        private readonly ArenaStats stats = ArenaStats.Load();

        private string statusText = "READY";
        private bool statusBusy;

        private CancellationTokenSource? runCts;
        private ArenaLauncher? launcher;
        private bool running;

        /// <summary>Blinking status light while something runs.</summary>
        private readonly System.Windows.Forms.Timer blinkTimer = new System.Windows.Forms.Timer { Interval = 450 };
        private bool blinkOn = true;

        // Start-up animation (text typed on the console, a click skips it)
        private readonly System.Windows.Forms.Timer bootTimer = new System.Windows.Forms.Timer { Interval = 15 };
        private bool booting;
        private bool setupAfterBoot;
        private string[] bootLines = Array.Empty<string>();
        private int bootChars;
        private int bootLine = -1;
        private DateTime bootEnd;

        // ---- Layout (must match tools/make_skin.py) ----------------------
        // Rectangles are the visible bodies; sprite controls are Skin.Margin
        // larger on every side for their glow and shadow.

        private const int W = 1000;
        private const int H = 772;

        private const int HeaderBottom = 172;                 // drag the window here
        private static readonly Rectangle StatusRect = new Rectangle(672, 34, W - 28 - 672, 128);

        private const int HumanRowTop = 198;
        private const int PlutoRowTop = 316;
        private const int ControlsX = 300;
        private const int RaceWidth = 150, RaceHeight = 80, RaceStep = 160;

        private static readonly Rectangle MapField = Rectangle.FromLTRB(ControlsX, 452, W - 62, 508);
        private static readonly Rectangle RankField = Rectangle.FromLTRB(ControlsX, 554, W - 62 - 214, 610);
        private static readonly Rectangle VersionField = Rectangle.FromLTRB(W - 62 - 202, 554, W - 62, 610);

        private static readonly Rectangle SetupBody = Rectangle.FromLTRB(316, 660, 470, 752);
        private static readonly Rectangle PlayBody = Rectangle.FromLTRB(488, 662, 792, 750);
        private static readonly Rectangle ExitBody = Rectangle.FromLTRB(810, 660, W - 18, 752);

        /// <summary>Sprite control bounds around a body rectangle.</summary>
        private static Rectangle Sprite(Rectangle body) => Rectangle.Inflate(body, Skin.Margin, Skin.Margin);

        public Form1()
        {
            InitializeComponent();
            BuildInterface();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int CS_DROPSHADOW = 0x20000;
                const int WS_MINIMIZEBOX = 0x20000;

                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                cp.Style |= WS_MINIMIZEBOX; // taskbar click minimizes / restores
                return cp;
            }
        }

        private void BuildInterface()
        {
            SuspendLayout();
            Controls.Clear();

            Text = $"Pluto Arena {AppInfo.Version}";
            Sfx.Enabled = config.UiSounds;
            Icon = AppInfo.Icon;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(W, H);
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Ui(10f, FontStyle.Regular);
            DoubleBuffered = true;
            MaximizeBox = false;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

            BuildWindowButtons();
            BuildRaceRow(HumanRowTop, humanButtons, SelectHumanRace);
            BuildRaceRow(PlutoRowTop, plutoButtons, SelectPlutoRace);
            BuildMapRow();
            BuildDifficultyRow();
            BuildBottom();

            SelectHumanRace(config.HumanRace);
            SelectPlutoRace(config.PlutoRace);
            BuildStatsLink();

            KeyPreview = true;
            KeyDown += (_, e) =>
            {
                if (booting)
                {
                    FinishBoot();
                    e.Handled = true;
                }
            };

            if (config.ShowIntro)
                StartBoot();

            // First start, or something is missing: open the setup window
            // (after the start-up animation).
            Shown += (_, _) =>
            {
                if (config.WasCreated || !SetupCheck.AllOk(config))
                {
                    if (booting)
                        setupAfterBoot = true;
                    else
                        OpenSetup();
                }
            };

            FormClosing += (_, _) =>
            {
                blinkTimer.Stop();
                bootTimer.Stop();
                runCts?.Cancel();
                launcher?.StopPluto();
            };

            blinkTimer.Tick += (_, _) =>
            {
                blinkOn = !blinkOn;
                Invalidate(new Rectangle(StatusRect.Left + 10, StatusRect.Top + 8, 24, 24));
            };

            ResumeLayout(false);
        }

        // ---- Controls -----------------------------------------------------

        private void BuildWindowButtons()
        {
            // Top right corner of the status screen
            WindowButton close = new WindowButton
            {
                IsClose = true,
                Location = new Point(StatusRect.Right - 38, StatusRect.Top + 8),
                Size = new Size(30, 22),
                HoverSound = null
            };
            close.Click += (_, _) => Close();

            WindowButton minimize = new WindowButton
            {
                Location = new Point(StatusRect.Right - 72, StatusRect.Top + 8),
                Size = new Size(30, 22),
                HoverSound = null
            };
            minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;

            Controls.Add(close);
            Controls.Add(minimize);
        }

        private void BuildRaceRow(int rowTop, Dictionary<string, RaceButton> buttons, Action<string> select)
        {
            (string race, ArenaIcon icon)[] items =
            {
                ("Terran", ArenaIcon.Terran),
                ("Protoss", ArenaIcon.Protoss),
                ("Zerg", ArenaIcon.Zerg),
                ("Random", ArenaIcon.Random)
            };

            for (int i = 0; i < items.Length; i++)
            {
                var (race, icon) = items[i];
                Rectangle body = new Rectangle(ControlsX + i * RaceStep, rowTop + 14, RaceWidth, RaceHeight);

                RaceButton button = new RaceButton
                {
                    Text = race,
                    Icon = icon,
                    Wide = race == "Random",
                    SkinName = "race_" + Skin.Key(race),
                    Bounds = Sprite(body)
                };

                button.Click += (_, _) => select(race);
                buttons[race] = button;
                Controls.Add(button);
            }
        }

        private void BuildMapRow()
        {
            mapSelect = new DarkSelect
            {
                Crt = true,
                Bounds = MapField
            };

            // Maps are read from StarCraft\Maps\000_PlutoArena, in the
            // same order StarCraft lists them.
            foreach (MapEntry map in config.LoadMaps())
                mapSelect.Items.Add(map);

            if (mapSelect.Items.Count == 0)
                mapSelect.Items.Add("(no maps in " + config.MapsFolder + ")");

            mapSelect.SelectedIndex = 0;
            Controls.Add(mapSelect);
        }

        private void BuildDifficultyRow()
        {
            difficultySelect = new DarkSelect
            {
                Crt = true,
                Bounds = RankField
            };

            // Pluto version: 1.0 = release cog2026-2578600, 2.0 = Pluto v2.0 (CPU).
            versionSelect = new DarkSelect
            {
                Crt = true,
                Bounds = VersionField
            };

            versionSelect.Items.AddRange(new object[] { "Pluto 1.0", "Pluto 2.0" });
            versionSelect.SelectedIndex = config.PlutoVersion == "2.0" ? 1 : 0;
            versionSelect.SelectedIndexChanged += (_, _) =>
            {
                config.PlutoVersion = versionSelect.SelectedIndex == 1 ? "2.0" : "1.0";
                config.Save();
                InvalidateStatus();
            };

            Controls.Add(versionSelect);

            difficultySelect.Items.AddRange(ArenaLauncher.Ranks);
            difficultySelect.SelectedIndex = Math.Max(0, Array.IndexOf(ArenaStats.RankOrder, config.Rank));
            difficultySelect.SelectedIndexChanged += (_, _) =>
            {
                config.Rank = ArenaLauncher.Rank(difficultySelect.SelectedItem?.ToString() ?? "C");
                config.Save();
                InvalidateStatus(); // record on the status screen
            };

            Controls.Add(difficultySelect);
        }

        private void BuildBottom()
        {
            playButton = new GlowButton
            {
                Text = "PLAY",
                Primary = true,
                SkinName = "play_terran",
                ClickSound = "play",
                Bounds = Sprite(PlayBody)
            };
            playButton.Click += Play_Click;

            GlowButton exit = new GlowButton
            {
                Text = "EXIT",
                SkinName = "btn_exit",
                Bounds = Sprite(ExitBody)
            };
            exit.Click += (_, _) => Close();

            GlowButton setup = new GlowButton
            {
                Text = "SETUP",
                SkinName = "btn_setup",
                Bounds = Sprite(SetupBody)
            };
            setup.Click += (_, _) => OpenSetup();

            Controls.Add(playButton);
            Controls.Add(exit);
            Controls.Add(setup);

            // Switches: the body of the first one starts at (30, 668), 22 px apart.
            ArenaCheck Switch(int index, string text, bool value, Action<bool> save)
            {
                ArenaCheck check = new ArenaCheck
                {
                    Switch = true,
                    Text = text,
                    Location = new Point(30 - Skin.SwitchMargin, 666 + index * 22),
                    Size = new Size(270, 22),
                    Checked = value
                };

                check.CheckedChanged += (_, _) =>
                {
                    save(check.Checked);
                    config.Save();
                };

                Controls.Add(check);
                return check;
            }

            hideCheck = Switch(0, "HIDE PLUTO WINDOW", config.HidePlutoWindow, v => config.HidePlutoWindow = v);
            muteCheck = Switch(1, "MUTE PLUTO SOUND", config.MutePlutoSound, v => config.MutePlutoSound = v);
            ladderCheck = Switch(2, "LADDER MODE", config.LadderMode, v => { config.LadderMode = v; InvalidateStatus(); });
            Switch(3, "UI SOUNDS", config.UiSounds, v => { config.UiSounds = v; Sfx.Enabled = v; });
        }

        /// <summary>[ STATS ] on the status screen: results by race and map.</summary>
        private void BuildStatsLink()
        {
            CrtLink link = new CrtLink
            {
                Text = "STATS",
                Location = new Point(StatusRect.Left + 14, StatusRect.Top + 104),
                Size = new Size(96, 20)
            };

            link.Click += (_, _) =>
            {
                using StatsForm form = new StatsForm(stats, config.PlutoVersion);
                form.ShowDialog(this);
            };

            Controls.Add(link);
        }

        // ---- Start-up animation -------------------------------------------

        private void StartBoot()
        {
            int maps = mapSelect?.Items.OfType<MapEntry>().Count() ?? 0;
            var (w, l) = stats.Record(config.PlutoVersion, config.Rank);

            bootLines = new[]
            {
                $"PLUTO ARENA {AppInfo.Version}  //  COMMAND CONSOLE",
                "",
                "> CONSOLE ............... ONLINE",
                $"> PLUTO AI .............. VERSION {config.PlutoVersion}",
                $"> ARENA MAPS ............ {maps}",
                $"> THREAT LEVEL .......... RANK {config.Rank}" + (config.LadderMode ? "  (LADDER)" : ""),
                $"> COMMANDER ............. {humanRace.ToUpperInvariant()}",
                $"> RECORD ................ {w} W - {l} L",
                "",
                "> ALL SYSTEMS READY"
            };

            booting = true;
            bootChars = 0;
            bootLine = -1;
            bootEnd = DateTime.MaxValue;

            foreach (Control c in Controls)
                c.Visible = false;

            bootTimer.Tick -= BootTick;
            bootTimer.Tick += BootTick;
            bootTimer.Start();
        }

        private void BootTick(object? sender, EventArgs e)
        {
            int total = bootLines.Sum(x => x.Length);

            if (bootChars < total)
            {
                bootChars = Math.Min(total, bootChars + 3);

                // A tick sound for every new line
                int line = BootPosition().Line;
                if (line != bootLine)
                {
                    bootLine = line;

                    if (bootLines[line].Length > 0)
                        Sfx.Play("hover");
                }

                if (bootChars >= total)
                    bootEnd = DateTime.UtcNow.AddMilliseconds(500);
            }
            else if (DateTime.UtcNow >= bootEnd)
            {
                FinishBoot();
                return;
            }

            Invalidate();
        }

        /// <summary>Line and column of the typing cursor.</summary>
        private (int Line, int Column) BootPosition()
        {
            int left = bootChars;

            for (int i = 0; i < bootLines.Length; i++)
            {
                if (left <= bootLines[i].Length)
                    return (i, left);

                left -= bootLines[i].Length;
            }

            return (bootLines.Length - 1, bootLines[^1].Length);
        }

        private void FinishBoot()
        {
            if (!booting)
                return;

            booting = false;
            bootTimer.Stop();

            foreach (Control c in Controls)
                c.Visible = true;

            Invalidate(true);

            if (setupAfterBoot)
            {
                setupAfterBoot = false;
                BeginInvoke(new Action(OpenSetup));
            }
        }

        private void DrawBoot(Graphics g)
        {
            using (SolidBrush veil = new SolidBrush(Color.FromArgb(232, 4, 11, 7)))
                g.FillRectangle(veil, ClientRectangle);

            using (Pen scan = new Pen(Color.FromArgb(40, 0, 0, 0)))
                for (int sy = 0; sy < H; sy += 3)
                    g.DrawLine(scan, 0, sy, W, sy);

            var (curLine, curCol) = BootPosition();

            using Font title = Skin.Mono(16f, FontStyle.Bold);
            using Font font = Skin.Mono(14f);
            using StringFormat sf = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces };

            float x = 80, y = 150;

            for (int i = 0; i <= curLine && i < bootLines.Length; i++)
            {
                string text = i < curLine ? bootLines[i] : bootLines[i].Substring(0, Math.Min(curCol, bootLines[i].Length));
                Font f = i == 0 ? title : font;
                Color c = i == 0 ? Theme.Accent : i == bootLines.Length - 1 ? Theme.Lighten(Skin.Phosphor, 0.3f) : Skin.Phosphor;

                Skin.GlowText(g, text, f, c, new RectangleF(x, y, W - 160, 34), sf);

                // Block cursor after the last typed character
                if (i == curLine && (DateTime.UtcNow.Millisecond / 250) % 2 == 0)
                {
                    float cx = x + g.MeasureString(text, f, PointF.Empty, sf).Width + 3;
                    using SolidBrush cursor = new SolidBrush(c);
                    g.FillRectangle(cursor, cx, y + 4, 11, 22);
                }

                y += i == 0 ? 44 : 34;
            }

            // Progress bar
            int total = Math.Max(1, bootLines.Sum(t => t.Length));
            RectangleF bar = new RectangleF(80, H - 110, W - 160, 10);

            using (Pen edge = new Pen(Skin.PhosphorDim))
                g.DrawRectangle(edge, bar.X, bar.Y, bar.Width, bar.Height);

            using (SolidBrush fill = new SolidBrush(Theme.Accent))
                g.FillRectangle(fill, bar.X + 2, bar.Y + 2, (bar.Width - 4) * bootChars / total, bar.Height - 4);

            using Font small = Skin.Mono(9.5f);
            using StringFormat far = new StringFormat { Alignment = StringAlignment.Far };
            Skin.GlowText(g, "CLICK OR PRESS ANY KEY TO SKIP", small, Skin.PhosphorDim, new RectangleF(80, H - 92, W - 160, 20), far, false);
        }

        private void OpenSetup()
        {
            if (running)
                return;

            using SetupForm setup = new SetupForm(config);
            setup.ShowDialog(this);

            ReloadMaps();
        }

        private void ReloadMaps()
        {
            if (mapSelect == null)
                return;

            string? current = (mapSelect.SelectedItem as MapEntry)?.FileName;

            mapSelect.Items.Clear();

            foreach (MapEntry map in config.LoadMaps())
                mapSelect.Items.Add(map);

            if (mapSelect.Items.Count == 0)
                mapSelect.Items.Add("(no maps in " + config.MapsFolder + ")");

            int index = mapSelect.Items.FindIndex(i => i is MapEntry m && m.FileName == current);
            mapSelect.ResetSelection(index < 0 ? 0 : index);
        }

        private void SelectHumanRace(string race)
        {
            humanRace = race;

            foreach (var (name, button) in humanButtons)
                button.Selected = name == race;

            if (config.HumanRace != race)
            {
                config.HumanRace = race;
                config.Save();
            }

            // The window takes the color of your race.
            Theme.Accent = Skin.Accent(race);

            if (playButton != null)
                playButton.SkinName = "play_" + Skin.Key(race);

            Invalidate(true);
        }

        private void SelectPlutoRace(string race)
        {
            plutoRace = race;

            foreach (var (name, button) in plutoButtons)
                button.Selected = name == race;

            if (config.PlutoRace != race)
            {
                config.PlutoRace = race;
                config.Save();
            }
        }

        private void SetStatus(string text)
        {
            statusText = text.ToUpperInvariant();
            statusBusy = !text.EndsWith("READY");

            blinkOn = true;

            if (statusBusy)
                blinkTimer.Start();
            else
                blinkTimer.Stop();

            InvalidateStatus();
        }

        private void InvalidateStatus() => Invalidate(StatusRect);

        private void SetRunning(bool value)
        {
            running = value;

            if (playButton != null)
            {
                playButton.Enabled = !value;
                playButton.Text = value ? "RUNNING..." : "PLAY";
            }

            if (mapSelect != null) mapSelect.Enabled = !value;
            if (difficultySelect != null) difficultySelect.Enabled = !value;
            if (versionSelect != null) versionSelect.Enabled = !value;

            foreach (RaceButton b in humanButtons.Values.Concat(plutoButtons.Values))
                b.Enabled = !value;
        }

        // ---- Window dragging (borderless window) --------------------------

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (booting)
            {
                FinishBoot();
                return;
            }

            if (e.Button == MouseButtons.Left && e.Y < HeaderBottom)
            {
                const int WM_NCLBUTTONDOWN = 0xA1;
                const int HTCAPTION = 0x2;

                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
        }

        // ---- Painting -----------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            Bitmap? background = Skin.Get("bg_" + Skin.Key(humanRace));

            if (background != null)
                Skin.Blit(g, background, 0, 0);
            else
                g.Clear(Theme.Window);

            Theme.HighQuality(g);

            if (booting)
            {
                DrawBoot(g);
                return;
            }

            DrawStatus(g);
        }

        /// <summary>The green status screen in the header.</summary>
        private void DrawStatus(Graphics g)
        {
            int x = StatusRect.Left + 18;

            // Status light: green = ready, blinking amber = working
            Color light = statusBusy ? (blinkOn ? Skin.Amber : Color.FromArgb(90, 66, 24)) : Skin.Phosphor;
            float lx = x + 4, ly = StatusRect.Top + 20;

            if (!statusBusy || blinkOn)
            {
                using SolidBrush halo = new SolidBrush(Color.FromArgb(70, light));
                g.FillEllipse(halo, lx - 7, ly - 7, 14, 14);
            }

            using (SolidBrush led = new SolidBrush(light))
                g.FillEllipse(led, lx - 3.5f, ly - 3.5f, 7, 7);

            using StringFormat sf = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            using (Font big = Skin.Mono(11f, FontStyle.Bold))
                Skin.GlowText(g, statusText, big, statusBusy ? Skin.Amber : Skin.Phosphor,
                    new RectangleF(x + 16, StatusRect.Top + 8, StatusRect.Width - 18 - 16 - 78, 24), sf);

            string rank = config.Rank;
            var (w, l) = stats.Record(config.PlutoVersion, rank);
            string streak = stats.Streak(config.PlutoVersion, rank, 5);
            string record = w + l == 0 ? "no games yet" : $"{w} W - {l} L" + (streak.Length > 0 ? "  " + streak : "");

            (string Key, string Value)[] rows =
            {
                ("PLUTO", config.PlutoVersion == "2.0" ? "2.0  (CPU)" : "1.0"),
                ("RANK", rank + (config.LadderMode ? "  (ladder)" : "")),
                ("RECORD", record)
            };

            using Font small = Skin.Mono(9.5f);

            for (int i = 0; i < rows.Length; i++)
            {
                float y = StatusRect.Top + 44 + i * 22;
                Skin.GlowText(g, rows[i].Key, small, Skin.PhosphorDim, new RectangleF(x, y - 10, 90, 20), sf, false);
                Skin.GlowText(g, rows[i].Value, small, Skin.Phosphor, new RectangleF(x + 82, y - 10, 136, 20), sf);
            }
        }

        // ---- Game start (unchanged logic) ---------------------------------

        private async void Play_Click(object? sender, EventArgs e)
        {
            if (running)
                return;

            if (mapSelect?.SelectedItem is not MapEntry map)
            {
                MessageBox.Show(
                    $"No maps found in:\n{config.MapsFolder}",
                    "Pluto Arena",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            // start-pluto.ps1 refuses to run while any StarCraft is open.
            if (!CloseRunningStarCraft())
                return;

            GameSettings settings = new GameSettings
            {
                HumanRace = humanRace,
                PlutoRace = plutoRace,
                Map = map,
                Difficulty = difficultySelect?.SelectedItem?.ToString() ?? "Original"
            };

            runCts = new CancellationTokenSource();
            CancellationToken ct = runCts.Token;

            bool locked = config.LockInput;
            Progress<string> progress = new Progress<string>(text =>
                SetStatus(locked && text != "READY" && !text.StartsWith("Ready") && !text.StartsWith("Game")
                    ? text + "  (Esc = stop)"
                    : text));
            launcher = new ArenaLauncher(config, progress, ct);
            ArenaLauncher current = launcher;

            SetRunning(true);

            // Ignore the real mouse/keyboard until both players are in the
            // lobby, so a bumped mouse cannot break the automation. Esc cancels.
            using InputBlocker? inputLock = config.LockInput ? new InputBlocker() : null;
            CancellationTokenSource cts = runCts;

            if (inputLock != null)
            {
                inputLock.EscapePressed += () =>
                {
                    ArenaLauncher.Log("ESC pressed - cancelling");
                    try { cts.Cancel(); } catch { }
                };
            }

            try
            {
                inputLock?.Start();

                // Menu automation runs in the background so this window
                // stays responsive.
                await Task.Run(() => current.Run(settings), ct);
            }
            catch (OperationCanceledException)
            {
                inputLock?.Stop();
                current.StopPluto();
                SetStatus("READY");
                SetRunning(false);
                return;
            }
            catch (Exception ex)
            {
                inputLock?.Stop();
                SetStatus("READY");
                SetRunning(false);

                ArenaLauncher.Log("ERROR: " + ex);

                MessageBox.Show(
                    ex is ArenaException ? ex.Message : $"Pluto Arena error:\n\n{ex}",
                    "Pluto Arena",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                return;
            }

            inputLock?.Stop(); // both players are in the lobby - your turn

            await GameLoop(current, settings);
        }

        /// <summary>
        /// Lobby is ready: supervise the game; after it, record the result and
        /// offer a REMATCH while both StarCraft stay open. PLAY becomes
        /// available again once you close your StarCraft (Pluto's StarCraft is
        /// closed automatically).
        /// </summary>
        private async Task GameLoop(ArenaLauncher current, GameSettings settings)
        {
            Action<string> onChat = message =>
            {
                try
                {
                    BeginInvoke(new Action(() => ChatToast.Show(message, current.HumanScreen)));
                }
                catch
                {
                }
            };

            current.PlutoSaid += onChat;

            try
            {
                while (true)
                {
                    DateTime playStarted = DateTime.UtcNow;

                    try
                    {
                        await Task.Run(() => current.Supervise());
                    }
                    catch
                    {
                    }

                    bool atScore = current.AtScoreScreen;

                    // Take Pluto out of its game first: Pluto 2.0 writes its game log
                    // (win estimate for the chart) when it leaves.
                    if (atScore)
                    {
                        SetStatus("Game over");

                        try
                        {
                            await RunLocked(() => current.PlutoLeaveGame());
                        }
                        catch (Exception ex)
                        {
                            ArenaLauncher.Log("PLUTO LEAVE failed: " + ex.Message);
                            atScore = false; // no rematch possible
                        }
                    }

                    GameOutcome outcome = current.Outcome;

                    // Leaving the game (instead of destroying the other side) can
                    // leave the result unknown: ask how it ended.
                    if (!outcome.Known && current.GameStarted)
                        outcome = AskResult(current);

                    bool rematch = AfterGame(outcome, settings, playStarted, offerRematch: atScore);

                    if (!atScore)
                        break; // your StarCraft was closed - Pluto is closed too

                    if (!rematch)
                    {
                        // Stay until you close your StarCraft, then close Pluto.
                        SetStatus("Close StarCraft when you are done");

                        try
                        {
                            await Task.Run(() => current.Supervise(watchScore: false));
                        }
                        catch
                        {
                        }

                        break;
                    }

                    // ---- REMATCH ------------------------------------------------
                    // Same races and map; the rank may have moved (ladder mode).
                    settings = new GameSettings
                    {
                        HumanRace = settings.HumanRace,
                        PlutoRace = settings.PlutoRace,
                        Map = settings.Map,
                        Difficulty = difficultySelect?.SelectedItem?.ToString() ?? settings.Difficulty
                    };

                    SetStatus("Rematch...");

                    try
                    {
                        await RunLocked(() => current.Rematch(settings));
                    }
                    catch (Exception ex)
                    {
                        ArenaLauncher.Log("REMATCH ERROR: " + ex);

                        MessageBox.Show(this,
                            (ex is ArenaException ? ex.Message : $"Rematch failed:\n\n{ex.Message}") +
                            "\n\nClose both StarCraft windows and press PLAY.",
                            "Pluto Arena", MessageBoxButtons.OK, MessageBoxIcon.Error);

                        try
                        {
                            await Task.Run(() => current.Supervise(watchScore: false));
                        }
                        catch
                        {
                        }

                        break;
                    }
                }
            }
            finally
            {
                current.PlutoSaid -= onChat;
            }

            SetStatus("READY");
            SetRunning(false);
        }

        /// <summary>Runs menu automation with the real mouse/keyboard locked (Esc cancels).</summary>
        private async Task RunLocked(Action action)
        {
            using InputBlocker? inputLock = config.LockInput ? new InputBlocker() : null;
            CancellationTokenSource? cts = runCts;

            if (inputLock != null)
            {
                inputLock.EscapePressed += () =>
                {
                    ArenaLauncher.Log("ESC pressed - cancelling");
                    try { cts?.Cancel(); } catch { }
                };
            }

            try
            {
                inputLock?.Start();
                await Task.Run(action);
            }
            finally
            {
                inputLock?.Stop();
            }
        }

        private GameOutcome AskResult(ArenaLauncher current)
        {
            using Form ask = new Form
            {
                Text = "Pluto Arena - how did the game end?",
                Icon = AppInfo.Icon,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                AutoScaleMode = AutoScaleMode.None,
                ClientSize = new Size(620, 200),
                BackColor = Theme.Content,
                ForeColor = Theme.Text,
                ShowInTaskbar = false,
                TopMost = true
            };

            Label text = new Label
            {
                Text = "The game was left before it ended, so the result is not known.\nHow did it end?",
                Font = Theme.Ui(11f, FontStyle.Regular),
                ForeColor = Theme.Text,
                Location = new Point(20, 18),
                Size = new Size(580, 56)
            };
            ask.Controls.Add(text);

            GameOutcome result = GameOutcome.Unknown;
            DateTime start = current.GameStartUtc;
            int frames = (int)Math.Max(0, (DateTime.UtcNow - start).TotalSeconds * 23.81);

            void Add(string caption, int x, bool primary, Func<GameOutcome> pick)
            {
                GlowButton b = new GlowButton
                {
                    Text = caption,
                    Primary = primary,
                    Location = new Point(x, 96),
                    Size = new Size(190, 80),
                    Surface = Theme.Content
                };
                b.Click += (_, _) => { result = pick(); ask.Close(); };
                ask.Controls.Add(b);
            }

            Add("I WON", 14, true, () => new GameOutcome { Known = true, HumanWon = true, Frames = frames, StartedUtc = start });
            Add("PLUTO WON", 214, false, () => new GameOutcome { Known = true, HumanWon = false, Frames = frames, StartedUtc = start });
            Add("DON'T COUNT", 414, false, () => GameOutcome.Unknown);

            ask.ShowDialog(this);

            ArenaLauncher.Log($"GAME END  asked the player: {(result.Known ? (result.HumanWon ? "you won" : "Pluto won") : "not counted")}");
            return result;
        }

        /// <summary>Records the result, moves the ladder rank and shows the summary.</summary>
        private bool AfterGame(GameOutcome outcome, GameSettings settings, DateTime playStartedUtc, bool offerRematch = false)
        {
            if (!outcome.Known)
            {
                // Not counted. A rematch is still possible.
                return offerRematch && MessageBox.Show(this, "The result was not counted.\n\nPlay a rematch?",
                    "Pluto Arena", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            }

            string version = config.PlutoVersion;
            string rank = ArenaLauncher.Rank(settings.Difficulty);

            stats.Add(new GameRecord
            {
                Date = DateTime.Now,
                Version = version,
                Rank = rank,
                Map = settings.Map?.DisplayName ?? "",
                HumanRace = settings.HumanRace,
                PlutoRace = settings.PlutoRace,
                Won = outcome.HumanWon,
                Frames = outcome.Frames
            });

            var (w, l) = stats.Record(version, rank);
            List<string> lines = new()
            {
                $"Pluto {version}, rank {rank}  ·  {settings.Map?.DisplayName}  ·  {outcome.Minutes:0.0} min",
                $"Your record on rank {rank}: {w} W - {l} L"
            };

            if (config.LadderMode)
            {
                string next = stats.NextLadderRank(version, rank);

                if (next != rank)
                {
                    int i = Array.IndexOf(ArenaStats.RankOrder, next);

                    if (i >= 0 && difficultySelect != null)
                        difficultySelect.SelectedIndex = i; // saves config.Rank

                    lines.Add(Array.IndexOf(ArenaStats.RankOrder, next) < Array.IndexOf(ArenaStats.RankOrder, rank)
                        ? $"Ladder: 2 wins in a row - promoted to rank {next}!"
                        : $"Ladder: 2 losses in a row - back to rank {next}.");
                }
                else
                {
                    lines.Add("Ladder: 2 wins in a row = rank up, 2 losses = rank down.");
                }
            }

            PlutoMemory.GameLog? log = version == "2.0"
                ? PlutoMemory.LastGame(config, config.HumanProfile, playStartedUtc.AddMinutes(-1))
                : null;

            if (log != null && log.Opening.Length > 0)
                lines.Add($"Pluto's opening: {log.Opening}");

            SetStatus(outcome.HumanWon ? "YOU WON - READY" : "PLUTO WON - READY");

            if (config.ShowSummary || offerRematch)
            {
                using GameSummaryForm summary = new GameSummaryForm(outcome.HumanWon, lines, log?.WinTrajectory, offerRematch);
                ScrInput.Activate(Handle); // in front of StarCraft
                summary.ShowDialog(this);
                return offerRematch && summary.Rematch;
            }

            return false;
        }

        /// <summary>
        /// Returns true when no StarCraft is running (after asking the user
        /// to close the ones that are).
        /// </summary>
        private bool CloseRunningStarCraft()
        {
            Process[] open = Process.GetProcessesByName("StarCraft");

            if (open.Length == 0)
                return true;

            DialogResult answer = MessageBox.Show(
                "StarCraft is already running.\n\n" +
                "Pluto Arena needs to start both StarCraft windows itself.\n" +
                "Close all running StarCraft windows now?",
                "Pluto Arena",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
                return false;

            foreach (Process p in open)
            {
                try
                {
                    p.CloseMainWindow();

                    if (!p.WaitForExit(4000))
                        p.Kill();

                    p.WaitForExit(4000);
                }
                catch
                {
                }
            }

            return Process.GetProcessesByName("StarCraft").Length == 0;
        }
    }
}
