using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace PlutoArena
{
    /// <summary>
    /// Your results against one Pluto version, split by Pluto's race, your
    /// race and map (from PlutoArena-stats.txt). Opened with STATS on the
    /// status screen.
    /// </summary>
    internal sealed class StatsForm : Form
    {
        private readonly ArenaStats stats;
        private string version;

        private const int Wd = 760;
        private const int MaxMaps = 8;

        private readonly GlowButton otherVersion;

        public StatsForm(ArenaStats stats, string version)
        {
            this.stats = stats;
            this.version = version;

            Text = "Pluto Arena - statistics";
            Icon = AppInfo.Icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Content;
            ForeColor = Theme.Text;
            DoubleBuffered = true;
            ShowInTaskbar = false;
            ClientSize = new Size(Wd, 760);

            GlowButton close = new GlowButton { Text = "CLOSE", Primary = true, Size = new Size(170, 66) };
            close.Location = new Point(ClientSize.Width - close.Width - 14, ClientSize.Height - close.Height - 8);
            close.Click += (_, _) => Close();
            Controls.Add(close);

            otherVersion = new GlowButton { Size = new Size(220, 66) };
            otherVersion.Location = new Point(close.Left - otherVersion.Width - 8, close.Top);
            otherVersion.Click += (_, _) =>
            {
                this.version = this.version == "2.0" ? "1.0" : "2.0";
                UpdateButton();
                Invalidate();
            };
            Controls.Add(otherVersion);
            UpdateButton();

            CancelButton = null;
            KeyPreview = true;
            KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        private void UpdateButton() => otherVersion.Text = version == "2.0" ? "SHOW PLUTO 1.0" : "SHOW PLUTO 2.0";

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.HighQuality(g);

            using (LinearGradientBrush metal = new LinearGradientBrush(ClientRectangle,
                       Color.FromArgb(52, 56, 62), Color.FromArgb(28, 30, 34), 90f))
                g.FillRectangle(metal, ClientRectangle);

            Skin.ChromeText(g, "STATISTICS", 26f, new PointF(24, 16), Theme.Accent);

            var total = stats.By(version, _ => "all");
            int w = total.Sum(t => t.Wins), l = total.Sum(t => t.Losses);

            using (Font tag = Skin.Mono(10f))
            using (StringFormat far = new StringFormat { Alignment = StringAlignment.Far })
                Skin.GlowText(g, $"PLUTO {version}  //  ALL RANKS  //  {w + l} GAMES", tag, Skin.Phosphor,
                    new RectangleF(Wd - 420, 30, 396, 20), far);

            float y = 78;

            if (w + l == 0)
            {
                RectangleF empty = new RectangleF(20, y, Wd - 40, 80);
                Skin.DrawCrt(g, empty);
                using Font f = Skin.Mono(11f);
                using StringFormat c = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                Skin.GlowText(g, $"> NO GAMES AGAINST PLUTO {version} YET", f, Skin.Phosphor, empty, c);
                return;
            }

            y = Table(g, y, "VS PLUTO'S RACE", stats.By(version, r => r.PlutoRace), 4);
            y = Table(g, y + 14, "YOUR RACE", stats.By(version, r => r.HumanRace), 4);
            Table(g, y + 14, "MAPS", stats.By(version, r => r.Map), MaxMaps);
        }

        /// <summary>One monitor panel: name, W - L, win rate bar. The best row (2+ games) is marked.</summary>
        private float Table(Graphics g, float top, string title, List<(string Key, int Wins, int Losses)> rows, int max)
        {
            List<(string Key, int Wins, int Losses)> shown = rows.Take(max).ToList();
            const float rowH = 24;

            RectangleF panel = new RectangleF(20, top, Wd - 40, 38 + shown.Count * rowH + 8);
            Skin.DrawCrt(g, panel);

            using Font head = Skin.Mono(10f, FontStyle.Bold);
            using Font font = Skin.Mono(10f);
            using StringFormat sf = new StringFormat { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center };
            using StringFormat far = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

            float x0 = panel.Left + 16;
            Skin.GlowText(g, title, head, Theme.Accent, new RectangleF(x0, panel.Top + 8, 300, 22), sf);
            Skin.GlowText(g, "W - L      WIN", head, Skin.PhosphorDim, new RectangleF(panel.Left + 300, panel.Top + 8, 170, 22), sf, false);

            if (rows.Count > max)
                Skin.GlowText(g, $"top {max} of {rows.Count}", font, Skin.PhosphorDim, new RectangleF(panel.Right - 200, panel.Top + 8, 184, 22), far, false);

            // Best: highest win rate with at least 2 games
            string? best = shown.Where(r => r.Wins + r.Losses >= 2)
                .OrderByDescending(r => (double)r.Wins / (r.Wins + r.Losses))
                .ThenByDescending(r => r.Wins + r.Losses)
                .Select(r => r.Key)
                .FirstOrDefault();

            float y = panel.Top + 36;

            foreach (var (key, wins, losses) in shown)
            {
                int games = wins + losses;
                double rate = games == 0 ? 0 : (double)wins / games;
                bool isBest = key == best && shown.Count > 1;
                Color color = isBest ? Theme.Lighten(Skin.Phosphor, 0.35f) : Skin.Phosphor;

                Skin.GlowText(g, key, font, color, new RectangleF(x0, y, 270, rowH), sf);
                Skin.GlowText(g, $"{wins,3} - {losses,-3}", font, color, new RectangleF(panel.Left + 300, y, 100, rowH), sf);
                Skin.GlowText(g, $"{rate * 100,3:0}%", font, color, new RectangleF(panel.Left + 400, y, 70, rowH), sf);

                // Win rate bar
                RectangleF bar = new RectangleF(panel.Left + 470, y + 7, 180, rowH - 14);
                using (SolidBrush back = new SolidBrush(Color.FromArgb(40, Skin.Phosphor)))
                    g.FillRectangle(back, bar);

                if (rate > 0)
                {
                    RectangleF fillRect = new RectangleF(bar.X, bar.Y, bar.Width * (float)rate, bar.Height);
                    using SolidBrush fill = new SolidBrush(isBest ? Theme.Accent : Skin.Phosphor);
                    g.FillRectangle(fill, fillRect);
                }

                if (isBest)
                    Skin.GlowText(g, "BEST", font, Theme.Accent, new RectangleF(bar.Right + 8, y, 60, rowH), sf);

                y += rowH;
            }

            return panel.Bottom;
        }
    }
}
