using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PlutoArena
{
    internal enum CheckLevel { Ok, Warning, Error }

    internal sealed record CheckItem(CheckLevel State, string Text);

    /// <summary>
    /// Finds the needed files on a PC and checks that everything Pluto Arena
    /// needs is present. Used by the setup window.
    /// </summary>
    internal static class SetupCheck
    {
        /// <summary>SHA-256 of the StarCraft.exe the Pluto bridge supports (SC:R 1.23.10.13515).</summary>
        public const string SupportedStarCraftHash = "32dbbdd001dd381cb1b3a719b7ad1fc918a9d4bc99661c675e00254efecca827";

        public const string SupportedStarCraftVersion = "1.23.10.13515";

        /// <summary>True when StarCraft.exe is the version the bridge supports (cached per file date).</summary>
        public static bool IsSupportedStarCraft(string exe)
        {
            try
            {
                DateTime stamp = File.GetLastWriteTimeUtc(exe);

                if (exe == checkedExe && stamp == checkedStamp)
                    return checkedResult;

                checkedResult = Sha256(exe) == SupportedStarCraftHash;
                checkedExe = exe;
                checkedStamp = stamp;
                return checkedResult;
            }
            catch
            {
                return true; // cannot check - let the bridge decide
            }
        }

        private static string? checkedExe;
        private static DateTime checkedStamp;
        private static bool checkedResult;

        public static readonly string[] DifficultyFiles =
        {
            "pluto-scr-original.dll", "pluto-scr-medium.dll", "pluto-scr-easy.dll",
            "pluto-scr-easyplus.dll", "pluto-scr-veryeasy.dll"
        };

        private static string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        private static string AppFolder => AppContext.BaseDirectory;

        /// <summary>Folder next to PlutoArena.exe with maps to install (optional).</summary>
        public static string BundledMaps => Path.Combine(AppFolder, "maps");

        // ---------------------------------------------------------------
        // Auto-detection
        // ---------------------------------------------------------------

        public static void Detect(ArenaConfig c)
        {
            if (!File.Exists(c.StarCraftExe))
                c.StarCraftExe = FirstExisting(StarCraftCandidates()) ?? c.StarCraftExe;

            if (!File.Exists(c.PlutoDll))
            {
                c.PlutoDll = FirstExisting(new[]
                {
                    @"C:\Program Files (x86)\Starcraft116\bwapi-data\AI\pluto.dll",
                    @"C:\StarCraft1161\bwapi-data\AI\pluto.dll",
                    @"C:\StarCraft116\bwapi-data\AI\pluto.dll",
                    @"C:\StarCraft\bwapi-data\AI\pluto.dll",
                    @"C:\Program Files (x86)\StarCraft 1.16.1\bwapi-data\AI\pluto.dll",
                    Path.Combine(AppFolder, "pluto", "pluto.dll")
                }) ?? c.PlutoDll;
            }

            if (!File.Exists(c.PlutoDll2))
            {
                c.PlutoDll2 = FirstExisting(new[]
                {
                    Path.Combine(Documents, "pluto-v2.0", "pluto.dll"),
                    Path.Combine(Documents, "Pluto2", "pluto.dll"),
                    Path.Combine(Documents, "pluto-v2", "pluto.dll"),
                    Path.Combine(AppFolder, "pluto-v2.0", "pluto.dll")
                }) ?? c.PlutoDll2;
            }

            // The installer ships the bridge in <app>\native and the difficulty
            // DLLs in <app>\modes - preferred whenever the configured folder
            // is missing or incomplete.
            if (!IsBridgeFolder(c.NativeFolder))
            {
                c.NativeFolder = new[]
                {
                    BundledNative,
                    Path.Combine(Documents, "PlutoRemasteredControlCenter", "native"),
                    Path.Combine(AppFolder, "PlutoRemasteredControlCenter", "native")
                }.FirstOrDefault(IsBridgeFolder) ?? c.NativeFolder;
            }

            if (!IsModesFolder(c.ModesFolder))
            {
                c.ModesFolder = new[]
                {
                    Path.Combine(AppFolder, "modes"),
                    Path.Combine(Documents, "PlutoDifficultyPack", "modes")
                }.FirstOrDefault(IsModesFolder) ?? c.ModesFolder;
            }
        }

        /// <summary>Bridge shipped with the installer.</summary>
        public static string BundledNative => Path.Combine(AppFolder, "native");

        public static readonly string[] BridgeFiles =
        {
            Path.Combine("bin", "pluto-scr.dll"),
            Path.Combine("bin", "scr-loader.exe"),
            Path.Combine("bin", "build-capabilities.txt"),
            Path.Combine("scripts", "start-pluto.ps1")
        };

        public const string PlutoV1Hash = "7e360b643c8c0156c03fe0cad9972a3058138ccfe22f921c5b4e0cd0aaf0abef";
        public const string PlutoV2Hash = "603fea2f77a4b652f29b832c2977cf5bc9d83bbcb927febd04499220020ce3bc";

        /// <summary>"1.0", "2.0" or null (other/unknown file) for a pluto.dll.</summary>
        public static string? PlutoRelease(string dll)
        {
            try
            {
                if (!File.Exists(dll))
                    return null;

                using FileStream f = File.OpenRead(dll);
                string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(f)).ToLowerInvariant();
                return hash == PlutoV1Hash ? "1.0" : hash == PlutoV2Hash ? "2.0" : null;
            }
            catch
            {
                return null;
            }
        }

        public static bool IsBridgeFolder(string folder) =>
            !string.IsNullOrWhiteSpace(folder) && BridgeFiles.All(f => File.Exists(Path.Combine(folder, f)));

        /// <summary>Original + tuned DLL (new system) or the 5 old per-level DLLs.</summary>
        public static bool IsModesFolder(string folder) =>
            !string.IsNullOrWhiteSpace(folder) &&
            ((File.Exists(Path.Combine(folder, "pluto-scr-original.dll")) && File.Exists(Path.Combine(folder, "pluto-scr-tuned.dll"))) ||
             DifficultyFiles.All(f => File.Exists(Path.Combine(folder, f))));

        private static IEnumerable<string> StarCraftCandidates()
        {
            // Battle.net writes the install folder into the uninstall key.
            foreach (string key in new[]
            {
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\StarCraft",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\StarCraft"
            })
            {
                string? location = null;

                try
                {
                    using RegistryKey? k = Registry.LocalMachine.OpenSubKey(key);
                    location = k?.GetValue("InstallLocation") as string;
                }
                catch
                {
                }

                if (!string.IsNullOrWhiteSpace(location))
                    yield return Path.Combine(location, "x86", "StarCraft.exe");
            }

            foreach (string drive in new[] { "C", "D", "E", "F" })
            {
                yield return $@"{drive}:\Program Files (x86)\StarCraft\x86\StarCraft.exe";
                yield return $@"{drive}:\Program Files\StarCraft\x86\StarCraft.exe";
                yield return $@"{drive}:\Games\StarCraft\x86\StarCraft.exe";
                yield return $@"{drive}:\StarCraft\x86\StarCraft.exe";
            }
        }

        private static string? FirstExisting(IEnumerable<string> paths) =>
            paths.FirstOrDefault(p => { try { return File.Exists(p); } catch { return false; } });

        private static string? FirstExistingDir(IEnumerable<string> paths) =>
            paths.FirstOrDefault(p => { try { return Directory.Exists(p); } catch { return false; } });

        // ---------------------------------------------------------------
        // Checks
        // ---------------------------------------------------------------

        public static bool AllOk(ArenaConfig c) => Run(c).All(i => i.State != CheckLevel.Error);

        public static List<CheckItem> Run(ArenaConfig c)
        {
            List<CheckItem> items = new();

            void Add(bool ok, string good, string bad, bool onlyWarning = false) =>
                items.Add(new CheckItem(ok ? CheckLevel.Ok : onlyWarning ? CheckLevel.Warning : CheckLevel.Error, ok ? good : bad));

            // StarCraft
            bool scExists = File.Exists(c.StarCraftExe);
            Add(scExists, "StarCraft: Remastered found", "StarCraft.exe not found (…\\StarCraft\\x86\\StarCraft.exe)");

            if (scExists)
            {
                string? hash = Sha256(c.StarCraftExe);
                Add(hash == SupportedStarCraftHash,
                    $"StarCraft version {SupportedStarCraftVersion} (supported by the Pluto bridge)",
                    $"StarCraft is not version {SupportedStarCraftVersion} (new Blizzard patch?) - the Pluto bridge only works with that version");
            }

            // Pluto AI
            string plutoFolder = Path.Combine(Path.GetDirectoryName(c.PlutoDll) ?? "", "pluto");
            // Pluto 1.0 is only required when it is the selected version.
            bool v1Optional = c.PlutoVersion == "2.0";
            Add(File.Exists(c.PlutoDll), "Pluto AI (pluto.dll) found", "pluto.dll not found (choose the folder with pluto.dll and the pluto subfolder)", v1Optional);
            Add(File.Exists(Path.Combine(plutoFolder, "pluto_infer.exe")) && File.Exists(Path.Combine(plutoFolder, "pluto_weights.bin")),
                "Pluto model files found (pluto_infer.exe, pluto_weights.bin)",
                "Pluto model files missing: pluto\\pluto_infer.exe and pluto\\pluto_weights.bin must be next to pluto.dll", v1Optional);

            string? release1 = PlutoRelease(c.PlutoDll);
            CheckLevel v1Level = v1Optional ? CheckLevel.Warning : CheckLevel.Error;
            if (release1 == "2.0")
                items.Add(new CheckItem(v1Level, "The Pluto AI field points to Pluto 2.0 - put it in the Pluto AI 2.0 field and use release cog2026-2578600 here"));
            else if (File.Exists(c.PlutoDll) && release1 == null)
                items.Add(new CheckItem(v1Level, "pluto.dll is not release cog2026-2578600 - the Pluto bridge will refuse it"));

            // Pluto 2.0 (optional)
            string plutoFolder2 = Path.Combine(Path.GetDirectoryName(c.PlutoDll2) ?? "", "pluto");
            bool has2 = File.Exists(c.PlutoDll2) &&
                        File.Exists(Path.Combine(plutoFolder2, "pluto_infer.exe")) &&
                        File.Exists(Path.Combine(plutoFolder2, "pluto_weights.bin"));
            string? release2 = has2 ? PlutoRelease(c.PlutoDll2) : null;

            if (has2 && release2 == "2.0")
                items.Add(new CheckItem(CheckLevel.Ok, "Pluto 2.0 found (optional, needs a 6+ core CPU with AVX2)"));
            else if (has2)
                items.Add(new CheckItem(CheckLevel.Warning, "Pluto AI 2.0 field: this pluto.dll is not Pluto v2.0"));
            else
                items.Add(new CheckItem(c.PlutoVersion == "2.0" ? CheckLevel.Error : CheckLevel.Warning,
                    "Pluto 2.0 not found (optional): pluto.dll + pluto\\pluto_infer.exe + pluto\\pluto_weights.bin"));

            // Remastered bridge
            string[] missingBridge = BridgeFiles.Where(f => !File.Exists(Path.Combine(c.NativeFolder, f))).ToArray();
            Add(missingBridge.Length == 0,
                "Pluto Remastered bridge (native folder) found",
                "Pluto Remastered bridge incomplete, missing: " + string.Join(", ", missingBridge));

            // Difficulty pack: Original + either the tuned bridge or the 4 old per-level DLLs
            bool hasOriginal = File.Exists(Path.Combine(c.ModesFolder, "pluto-scr-original.dll"));
            bool hasTuned = File.Exists(Path.Combine(c.ModesFolder, "pluto-scr-tuned.dll"));
            string[] missingModes = DifficultyFiles.Where(f => !File.Exists(Path.Combine(c.ModesFolder, f))).ToArray();

            if (hasOriginal && hasTuned)
                items.Add(new CheckItem(CheckLevel.Ok, "Difficulty bridge found (reaction delay + APM limit)"));
            else
                Add(missingModes.Length == 0,
                    "Difficulty DLLs found (old difficulty pack)",
                    "Difficulty DLLs missing: " + string.Join(", ", missingModes));

            // Maps
            int maps = c.LoadMaps().Count;
            bool bundled = Directory.Exists(BundledMaps) && Directory.GetFiles(BundledMaps).Any(IsMap);

            if (maps > 0)
                items.Add(new CheckItem(CheckLevel.Ok, $"{maps} maps in Maps\\{c.MapsFolderName}"));
            else if (bundled)
                items.Add(new CheckItem(CheckLevel.Warning, $"Maps\\{c.MapsFolderName} is empty - maps will be copied there on Save"));
            else
                items.Add(new CheckItem(CheckLevel.Error, $"No maps in StarCraft\\Maps\\{c.MapsFolderName}"));

            // Profiles
            Add(!string.IsNullOrWhiteSpace(c.HumanProfile) && !string.IsNullOrWhiteSpace(c.PlutoProfile) &&
                !c.HumanProfile.Equals(c.PlutoProfile, StringComparison.OrdinalIgnoreCase),
                $"LAN profiles: you = {c.HumanProfile}, Pluto = {c.PlutoProfile} (created automatically if missing)",
                "Enter two different LAN profile names");

            // OCR
            ScrOcr.Initialize();
            if (!ScrOcr.Available)
                items.Add(new CheckItem(CheckLevel.Error,
                    "Windows text recognition (OCR) not available - " + ScrOcr.EnglishHelp));
            else if (!ScrOcr.IsEnglish)
                items.Add(new CheckItem(CheckLevel.Error,
                    $"Windows OCR has no English, only {ScrOcr.LanguageTag} - " + ScrOcr.EnglishHelp));
            else
                items.Add(new CheckItem(CheckLevel.Ok, $"Windows text recognition (OCR) available ({ScrOcr.LanguageTag})"));

            items.Add(new CheckItem(CheckLevel.Warning,
                "In StarCraft use: English client, Options > Graphics > Display Mode = Windowed (Fullscreen)"));

            return items;
        }

        public static bool IsMap(string file) =>
            file.EndsWith(".scx", StringComparison.OrdinalIgnoreCase) ||
            file.EndsWith(".scm", StringComparison.OrdinalIgnoreCase);

        private static string? Sha256(string path)
        {
            try
            {
                using FileStream fs = File.OpenRead(path);
                return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Copies maps shipped next to PlutoArena.exe into StarCraft\Maps\000_PlutoArena.</summary>
        public static string? InstallBundledMaps(ArenaConfig c)
        {
            if (c.LoadMaps().Count > 0 || !Directory.Exists(BundledMaps))
                return null;

            try
            {
                Directory.CreateDirectory(c.MapsFolder);

                foreach (string file in Directory.GetFiles(BundledMaps).Where(IsMap))
                    File.Copy(file, Path.Combine(c.MapsFolder, Path.GetFileName(file)), false);

                return null;
            }
            catch (Exception ex)
            {
                return
                    $"Could not copy the maps into\n{c.MapsFolder}\n\n{ex.Message}\n\n" +
                    $"Copy the files from\n{BundledMaps}\ninto that folder by hand " +
                    "(or start Pluto Arena once as administrator).";
            }
        }
    }

    // =====================================================================
    // Setup window
    // =====================================================================

    internal sealed class SetupForm : Form
    {
        private readonly ArenaConfig config;

        private readonly TextBox starCraftBox;
        private readonly TextBox plutoBox;
        private readonly TextBox plutoBox2;
        private readonly TextBox nativeBox;
        private readonly TextBox modesBox;
        private readonly TextBox humanBox;
        private readonly TextBox plutoProfileBox;

        private readonly ChecklistView checklist;

        public SetupForm(ArenaConfig config)
        {
            this.config = config;

            Text = $"Pluto Arena {AppInfo.Version} - Setup";
            Icon = AppInfo.Icon;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(900, 940);
            BackColor = Theme.Content;
            ForeColor = Theme.Text;
            Font = Theme.Ui(10f, FontStyle.Regular);
            DoubleBuffered = true;

            int y = 92;

            starCraftBox = AddPathRow("StarCraft.exe (Remastered)", config.StarCraftExe, ref y, file: "StarCraft.exe|StarCraft.exe");
            plutoBox = AddPathRow("Pluto AI  (pluto.dll, release cog2026-2578600)", config.PlutoDll, ref y, file: "pluto.dll|pluto.dll");
            plutoBox2 = AddPathRow("Pluto AI 2.0  (pluto.dll, optional)", config.PlutoDll2, ref y, file: "pluto.dll|pluto.dll");
            nativeBox = AddPathRow("Pluto Remastered bridge  (native folder)", config.NativeFolder, ref y);
            modesBox = AddPathRow("Difficulty DLLs  (modes folder)", config.ModesFolder, ref y);
            humanBox = AddTextRow("Your LAN profile name", config.HumanProfile, ref y);
            plutoProfileBox = AddTextRow("Pluto LAN profile name", config.PlutoProfile, ref y);

            checklist = new ChecklistView
            {
                Location = new Point(30, y + 6),
                Size = new Size(840, 304)
            };
            Controls.Add(checklist);

            GlowButton check = new GlowButton { Text = "CHECK", Location = new Point(300, 850), Size = new Size(190, 80), Surface = Theme.Content };
            GlowButton save = new GlowButton { Text = "SAVE", Primary = true, Location = new Point(496, 850), Size = new Size(210, 80), Surface = Theme.Content };
            GlowButton cancel = new GlowButton { Text = "CANCEL", Location = new Point(712, 850), Size = new Size(170, 80), Surface = Theme.Content };

            GlowButton reset = new GlowButton { Text = "RESET MEMORY", Location = new Point(18, 850), Size = new Size(270, 80), Surface = Theme.Content };
            new ToolTip().SetToolTip(reset, "Delete what Pluto remembers about you (its opening statistics against your LAN name)");
            reset.Click += (_, _) => ResetPlutoMemory();
            Controls.Add(reset);

            check.Click += (_, _) => RunChecks();
            save.Click += (_, _) => Save();
            cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(check);
            Controls.Add(save);
            Controls.Add(cancel);

            Shown += (_, _) => RunChecks();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // Dark title bar on Windows 10 (20H1+) / 11.
            try
            {
                int on = 1;
                DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
            }
            catch
            {
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.HighQuality(g);

            using Font title = new Font(Theme.TitleFamily, 24f, FontStyle.Bold, GraphicsUnit.Point);
            using SolidBrush white = new SolidBrush(Theme.Text);
            using SolidBrush muted = new SolidBrush(Theme.TextMuted);
            using Font small = Theme.Ui(10f, FontStyle.Regular);

            g.DrawString("SETUP", title, white, 26, 14);
            g.DrawString("Tell Pluto Arena where everything is. Paths that were found automatically are already filled in.",
                small, muted, 30, 58);

            using Pen line = new Pen(Color.FromArgb(120, Theme.Accent), 1.5f);
            g.DrawLine(line, 30, 84, ClientSize.Width - 30, 84);
        }

        private TextBox AddPathRow(string label, string value, ref int y, string? file = null)
        {
            TextBox box = AddTextRow(label, value, ref y, browse: true);

            Button browse = new Button
            {
                Text = "Browse…",
                Location = new Point(box.Right + 8, box.Top - 1),
                Size = new Size(110, box.Height + 2),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.ButtonTop,
                ForeColor = Theme.Text,
                Cursor = Cursors.Hand
            };

            browse.FlatAppearance.BorderColor = Theme.ButtonEdge;

            browse.Click += (_, _) =>
            {
                if (file != null)
                {
                    using OpenFileDialog dlg = new OpenFileDialog { Filter = file + "|All files|*.*" };

                    try { dlg.InitialDirectory = Path.GetDirectoryName(box.Text); } catch { }

                    if (dlg.ShowDialog(this) == DialogResult.OK)
                        box.Text = dlg.FileName;
                }
                else
                {
                    using FolderBrowserDialog dlg = new FolderBrowserDialog { SelectedPath = box.Text };

                    if (dlg.ShowDialog(this) == DialogResult.OK)
                        box.Text = dlg.SelectedPath;
                }

                RunChecks();
            };

            Controls.Add(browse);
            return box;
        }

        private TextBox AddTextRow(string label, string value, ref int y, bool browse = false)
        {
            Label l = new Label
            {
                Text = label,
                Location = new Point(30, y),
                AutoSize = true,
                ForeColor = Theme.TextMuted,
                BackColor = Color.Transparent
            };

            TextBox box = new TextBox
            {
                Text = value,
                Location = new Point(30, y + 24),
                Width = browse ? 722 : 340,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(9, 15, 24),
                ForeColor = Theme.Text,
                Font = Theme.Ui(11f, FontStyle.Regular)
            };

            Controls.Add(l);
            Controls.Add(box);

            y += 62;
            return box;
        }

        private void ApplyToConfig(ArenaConfig c)
        {
            c.StarCraftExe = starCraftBox.Text.Trim().Trim('"');
            c.PlutoDll = plutoBox.Text.Trim().Trim('"');
            c.PlutoDll2 = plutoBox2.Text.Trim().Trim('"');
            c.NativeFolder = nativeBox.Text.Trim().Trim('"');
            c.ModesFolder = modesBox.Text.Trim().Trim('"');
            c.HumanProfile = humanBox.Text.Trim();
            c.PlutoProfile = plutoProfileBox.Text.Trim();
        }

        /// <summary>
        /// Pluto picks its opening with statistics per opponent (your LAN
        /// name). Deleting the file makes Pluto start fresh against you.
        /// </summary>
        private void ResetPlutoMemory()
        {
            ApplyToConfig(config);
            string name = config.HumanProfile;

            if (PlutoMemory.Files(config, name).Count == 0)
            {
                MessageBox.Show(this, $"Pluto has nothing saved about \"{name}\" yet.", "Pluto Arena",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this,
                    $"Delete what Pluto remembers about \"{name}\"?\n\n" +
                    "Pluto keeps statistics of which openings won against you and prefers those. " +
                    "After the reset it starts fresh. Your Pluto Arena record stays.",
                    "Pluto Arena", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            int deleted = PlutoMemory.Reset(config, name, out string? error);

            if (error != null)
                MessageBox.Show(this, $"Could not delete everything:\n\n{error}\n\nClose StarCraft and try again.",
                    "Pluto Arena", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else
                MessageBox.Show(this, $"Done - {deleted} file(s) deleted. Pluto starts fresh against \"{name}\".",
                    "Pluto Arena", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RunChecks()
        {
            ApplyToConfig(config);

            Cursor = Cursors.WaitCursor;
            checklist.Items = SetupCheck.Run(config);
            Cursor = Cursors.Default;
        }

        private void Save()
        {
            ApplyToConfig(config);

            string? mapError = SetupCheck.InstallBundledMaps(config);

            if (mapError != null)
                MessageBox.Show(this, mapError, "Pluto Arena", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            config.Save();

            List<CheckItem> items = SetupCheck.Run(config);
            checklist.Items = items;

            if (items.Any(i => i.State == CheckLevel.Error))
            {
                DialogResult answer = MessageBox.Show(this,
                    "Some required files are still missing (red lines).\n\n" +
                    "The settings were saved, but PLAY will not work until they are fixed.\n" +
                    "Close the setup anyway?",
                    "Pluto Arena", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (answer != DialogResult.Yes)
                    return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>List of check results with colored marks.</summary>
        private sealed class ChecklistView : Control
        {
            private List<CheckItem> items = new();

            public ChecklistView()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            public List<CheckItem> Items
            {
                get => items;
                set { items = value; Invalidate(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Theme.HighQuality(g);
                g.Clear(Theme.Content);

                RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);

                using (GraphicsPath shape = Theme.Chamfer(r, 8))
                {
                    using SolidBrush fill = new SolidBrush(Theme.Row);
                    g.FillPath(fill, shape);

                    using Pen edge = new Pen(Theme.RowEdge, 1.2f);
                    g.DrawPath(edge, shape);
                }

                using Font font = Theme.Ui(10f, FontStyle.Regular);
                using Font mark = Theme.Ui(11f);

                float y = 12;

                foreach (CheckItem item in items)
                {
                    (string symbol, Color color) = item.State switch
                    {
                        CheckLevel.Ok => ("✔", Color.FromArgb(90, 220, 150)),
                        CheckLevel.Warning => ("!", Color.FromArgb(255, 196, 80)),
                        _ => ("✖", Color.FromArgb(255, 96, 96))
                    };

                    using SolidBrush markBrush = new SolidBrush(color);
                    using SolidBrush textBrush = new SolidBrush(item.State == CheckLevel.Ok ? Theme.Text : color);

                    g.DrawString(symbol, mark, markBrush, 16, y - 1);
                    g.DrawString(item.Text, font, textBrush, new RectangleF(44, y, Width - 60, 24),
                        new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });

                    y += 22;
                }
            }
        }
    }
}
