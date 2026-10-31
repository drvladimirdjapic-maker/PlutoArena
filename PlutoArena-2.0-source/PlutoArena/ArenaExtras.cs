using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace PlutoArena
{
    /// <summary>Program version and icon, shared by all windows.</summary>
    internal static class AppInfo
    {
        /// <summary>"1.4" or "1.4.1" (from the Version in PlutoArena.csproj).</summary>
        public static string Version
        {
            get
            {
                Version? v = typeof(AppInfo).Assembly.GetName().Version;

                if (v == null)
                    return "";

                return v.Build > 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";
            }
        }

        private static Icon? icon;

        /// <summary>pluto-arena.ico embedded in the exe (null if missing).</summary>
        public static Icon? Icon
        {
            get
            {
                if (icon != null)
                    return icon;

                try
                {
                    Assembly asm = typeof(AppInfo).Assembly;
                    string? name = asm.GetManifestResourceNames()
                        .FirstOrDefault(n => n.EndsWith("pluto-arena.ico", StringComparison.OrdinalIgnoreCase));

                    if (name != null)
                    {
                        using Stream? s = asm.GetManifestResourceStream(name);

                        if (s != null)
                            icon = new Icon(s);
                    }
                }
                catch
                {
                }

                return icon;
            }
        }
    }

    /// <summary>
    /// Shows what Pluto writes in chat ("gl hf", "gg" ...) on top of your
    /// StarCraft for a few seconds. Pluto's chat only appears in its own
    /// (hidden) window, so you would not see it otherwise. The window never
    /// takes the focus and mouse clicks go through it.
    /// </summary>
    internal sealed class ChatToast : Form
    {
        private readonly string text;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 50 };
        private int ticks;

        private const int ShowTicks = 140; // 7 s
        private const int FadeTicks = 20;  // 1 s

        private static ChatToast? current;

        public static void Show(string message, Rectangle screen)
        {
            try
            {
                current?.Close();
                current = new ChatToast(message, screen);
                current.Show();
            }
            catch
            {
            }
        }

        private ChatToast(string message, Rectangle screen)
        {
            text = "Pluto:  " + message;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.FromArgb(10, 18, 30);
            Opacity = 0.92;
            DoubleBuffered = true;

            using Font font = ToastFont();
            Size measured = TextRenderer.MeasureText(text, font);
            Size = new Size(Math.Min(screen.Width - 80, measured.Width + 48), measured.Height + 26);
            Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top + (int)(screen.Height * 0.10));

            timer.Tick += (_, _) =>
            {
                ticks++;

                if (ticks > ShowTicks)
                    Opacity = Math.Max(0, 0.92 * (1 - (ticks - ShowTicks) / (double)FadeTicks));

                if (ticks > ShowTicks + FadeTicks)
                    Close();
            };

            timer.Start();
        }

        private static Font ToastFont() => Theme.Ui(13f);

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOPMOST = 0x00000008;
                const int WS_EX_TRANSPARENT = 0x00000020;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_NOACTIVATE = 0x08000000;

                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.HighQuality(g);

            RectangleF r = new RectangleF(1, 1, Width - 3, Height - 3);
            using GraphicsPath shape = Theme.Chamfer(r, 8);

            using (Pen pen = new Pen(Theme.Accent, 1.6f))
                g.DrawPath(pen, shape);

            using Font font = ToastFont();
            TextRenderer.DrawText(g, text, font, new Rectangle(0, 0, Width, Height), Color.FromArgb(255, 220, 140),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                timer.Dispose();

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// After a game: result, your record on this rank, the ladder step and -
    /// with Pluto 2.0 - the chart of Pluto's own win estimate during the game.
    /// </summary>
    internal sealed class GameSummaryForm : Form
    {
        private readonly bool won;
        private readonly string[] lines;
        private readonly List<double> trajectory;

        private readonly RectangleF linesPanel;
        private readonly RectangleF chartPanel;

        /// <summary>True when the player clicked REMATCH.</summary>
        public bool Rematch { get; private set; }

        private static readonly Color Win = Color.FromArgb(90, 230, 150);
        private static readonly Color Loss = Color.FromArgb(255, 90, 80);

        public GameSummaryForm(bool won, IEnumerable<string> lines, List<double>? trajectory, bool offerRematch = false)
        {
            this.won = won;
            this.lines = lines.ToArray();
            this.trajectory = trajectory ?? new List<double>();

            Text = "Pluto Arena - game summary";
            Icon = AppInfo.Icon;
            TopMost = true; // shows over your StarCraft
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Content;
            ForeColor = Theme.Text;
            DoubleBuffered = true;

            const int width = 680;
            float y = 92;

            linesPanel = new RectangleF(20, y, width - 40, this.lines.Length * 24 + 22);
            y = linesPanel.Bottom + 16;

            if (this.trajectory.Count > 1)
            {
                chartPanel = new RectangleF(20, y, width - 40, 230);
                y = chartPanel.Bottom + 12;
            }

            ClientSize = new Size(width, (int)y + 86);

            GlowButton ok = new GlowButton
            {
                Text = offerRematch ? "CLOSE" : "OK",
                Primary = !offerRematch,
                Size = new Size(170, 70),
                Surface = Theme.Content
            };
            ok.Location = new Point(ClientSize.Width - ok.Width - 14, ClientSize.Height - ok.Height - 8);
            ok.Click += (_, _) => Close();
            Controls.Add(ok);

            if (offerRematch)
            {
                GlowButton rematch = new GlowButton
                {
                    Text = "REMATCH",
                    Primary = true,
                    Size = new Size(220, 70),
                    Surface = Theme.Content
                };
                rematch.Location = new Point(ok.Left - rematch.Width - 8, ok.Top);
                rematch.Click += (_, _) => { Rematch = true; Close(); };
                Controls.Add(rematch);
            }

            AcceptButton = null;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Sfx.Play(won ? "play" : "click");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.HighQuality(g);

            DrawPlate(g);

            Color c = won ? Win : Loss;
            Skin.ChromeText(g, won ? "VICTORY" : "DEFEAT", 36f, new PointF(26, 18), c);

            using (Font tag = Skin.Mono(9.5f))
            using (StringFormat far = new StringFormat { Alignment = StringAlignment.Far })
                Skin.GlowText(g, won ? "> MISSION COMPLETE" : "> MISSION FAILED", tag, c,
                    new RectangleF(ClientSize.Width - 300, 40, 276, 20), far);

            // Result lines on a monitor panel
            Skin.DrawCrt(g, linesPanel);

            using (Font font = Skin.Mono(10.5f))
            using (StringFormat sf = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
            {
                float y = linesPanel.Top + 12;

                foreach (string line in lines)
                {
                    Skin.GlowText(g, "> " + line, font, Skin.Phosphor, new RectangleF(linesPanel.Left + 14, y, linesPanel.Width - 28, 22), sf);
                    y += 24;
                }
            }

            if (trajectory.Count > 1)
            {
                Skin.DrawCrt(g, chartPanel);
                DrawChart(g, new RectangleF(chartPanel.Left + 16, chartPanel.Top + 36, chartPanel.Width - 70, chartPanel.Height - 62));
            }
        }

        /// <summary>Brushed-metal plate with a hazard stripe at the top.</summary>
        private void DrawPlate(Graphics g)
        {
            Rectangle all = ClientRectangle;

            using (LinearGradientBrush metal = new LinearGradientBrush(all, Color.FromArgb(52, 56, 62), Color.FromArgb(28, 30, 34), 90f))
                g.FillRectangle(metal, all);

            Random rnd = new Random(7);
            using (Pen streak = new Pen(Color.FromArgb(10, 255, 255, 255)))
                for (int i = 0; i < 160; i++)
                {
                    float y = rnd.Next(all.Height), x = rnd.Next(all.Width);
                    g.DrawLine(streak, x, y, x + 40 + rnd.Next(160), y);
                }

            // Hazard stripe
            GraphicsState state = g.Save();
            g.SetClip(new Rectangle(0, 0, all.Width, 6));
            using (SolidBrush black = new SolidBrush(Color.FromArgb(20, 20, 18)))
                g.FillRectangle(black, 0, 0, all.Width, 6);
            using (SolidBrush yellow = new SolidBrush(Color.FromArgb(205, 158, 30)))
                for (int x = -12; x < all.Width + 12; x += 22)
                    g.FillPolygon(yellow, new[] { new PointF(x, 6), new PointF(x + 11, 6), new PointF(x + 17, 0), new PointF(x + 6, 0) });
            g.Restore(state);
        }

        /// <summary>Pluto's win estimate (-1 .. +1) over the game, drawn as YOUR chances.</summary>
        private void DrawChart(Graphics g, RectangleF r)
        {
            using (Font f = Skin.Mono(9f))
            using (StringFormat sf = new StringFormat())
            {
                Skin.GlowText(g, "YOUR WINNING CHANCES, AS PLUTO SAW THEM", f, Skin.Phosphor,
                    new RectangleF(r.Left, r.Top - 26, r.Width, 18), sf);

                Skin.GlowText(g, "100%", f, Skin.PhosphorDim, new RectangleF(r.Right + 6, r.Top - 7, 50, 16), sf, false);
                Skin.GlowText(g, "50%", f, Skin.PhosphorDim, new RectangleF(r.Right + 6, r.Top + r.Height / 2 - 7, 50, 16), sf, false);
                Skin.GlowText(g, "0%", f, Skin.PhosphorDim, new RectangleF(r.Right + 6, r.Bottom - 8, 50, 16), sf, false);
                Skin.GlowText(g, "START", f, Skin.PhosphorDim, new RectangleF(r.Left, r.Bottom + 3, 60, 16), sf, false);

                using StringFormat far = new StringFormat { Alignment = StringAlignment.Far };
                Skin.GlowText(g, "END", f, Skin.PhosphorDim, new RectangleF(r.Right - 60, r.Bottom + 3, 60, 16), far, false);
            }

            // Grid
            using (Pen grid = new Pen(Color.FromArgb(40, Skin.Phosphor)))
            {
                for (int i = 0; i <= 4; i++)
                {
                    float y = r.Top + r.Height * i / 4;
                    g.DrawLine(grid, r.Left, y, r.Right, y);
                }

                for (int i = 0; i <= 10; i++)
                {
                    float x = r.Left + r.Width * i / 10;
                    g.DrawLine(grid, x, r.Top, x, r.Bottom);
                }
            }

            using (Pen mid = new Pen(Color.FromArgb(110, Skin.Phosphor)) { DashStyle = DashStyle.Dash })
                g.DrawLine(mid, r.Left, r.Top + r.Height / 2, r.Right, r.Top + r.Height / 2);

            int n = trajectory.Count;
            PointF[] pts = new PointF[n];

            for (int i = 0; i < n; i++)
            {
                // Pluto's value is from Pluto's side: your chance = (1 - v) / 2.
                double you = Math.Clamp((1 - trajectory[i]) / 2, 0, 1);
                pts[i] = new PointF(r.Left + r.Width * i / (n - 1), r.Bottom - (float)(you * r.Height));
            }

            Color line = Theme.Accent;

            using (GraphicsPath area = new GraphicsPath())
            {
                area.AddLine(r.Left, r.Bottom, pts[0].X, pts[0].Y);
                area.AddLines(pts);
                area.AddLine(pts[n - 1].X, pts[n - 1].Y, r.Right, r.Bottom);
                area.CloseFigure();

                using LinearGradientBrush fill = new LinearGradientBrush(r,
                    Color.FromArgb(110, line), Color.FromArgb(8, line), 90f);
                g.FillPath(fill, area);
            }

            using (Pen glowPen = new Pen(Color.FromArgb(70, line), 6f) { LineJoin = LineJoin.Round })
                g.DrawLines(glowPen, pts);

            using (Pen pen = new Pen(Theme.Lighten(line, 0.35f), 2.2f) { LineJoin = LineJoin.Round })
                g.DrawLines(pen, pts);

            // End point
            PointF end = pts[n - 1];
            using (SolidBrush dot = new SolidBrush(won ? Win : Loss))
                g.FillEllipse(dot, end.X - 4, end.Y - 4, 8, 8);
        }
    }
}
