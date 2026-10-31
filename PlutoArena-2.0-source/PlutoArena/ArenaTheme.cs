using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace PlutoArena
{
    // =====================================================================
    // Colors, fonts and drawing helpers for the Pluto Arena look.
    // The main window uses the sprites in ArenaSkin.cs; the dialogs and
    // anything without a sprite are drawn here in code.
    // =====================================================================

    internal static class Theme
    {
        // Gunmetal panels (2.0 look). The accent follows your race.
        public static readonly Color Window = Color.FromArgb(16, 17, 19);
        public static readonly Color Frame = Color.FromArgb(26, 28, 31);
        public static readonly Color FrameEdge = Color.FromArgb(72, 78, 86);
        public static Color FrameGlow => Accent;
        public static readonly Color Content = Color.FromArgb(28, 30, 33);
        public static readonly Color Row = Color.FromArgb(19, 21, 23);
        public static readonly Color RowEdge = Color.FromArgb(62, 66, 72);

        public static readonly Color ButtonTop = Color.FromArgb(70, 76, 84);
        public static readonly Color ButtonBottom = Color.FromArgb(30, 33, 37);
        public static readonly Color ButtonEdge = Color.FromArgb(92, 98, 106);

        public static Color SelectedTop => Scale(Accent, 0.36f);
        public static Color SelectedBottom => Scale(Accent, 0.14f);
        public static Color SelectedEdge => Accent;

        public static readonly Color Text = Color.FromArgb(232, 236, 242);
        public static readonly Color TextMuted = Color.FromArgb(140, 148, 158);

        /// <summary>Race color of the human player (set by the main window).</summary>
        public static Color Accent { get; set; } = Color.FromArgb(80, 165, 255);

        public static Color Scale(Color c, float k) => Color.FromArgb(c.A,
            Math.Clamp((int)(c.R * k), 0, 255), Math.Clamp((int)(c.G * k), 0, 255), Math.Clamp((int)(c.B * k), 0, 255));

        public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        private static string? titleFamily;
        private static string? uiFamily;

        /// <summary>Bahnschrift (Windows 10+) has the right techy look; Segoe UI otherwise.</summary>
        public static string TitleFamily => titleFamily ??= Pick("Bahnschrift", "Segoe UI");

        public static string UiFamily => uiFamily ??= Pick("Segoe UI Semibold", "Segoe UI");

        internal static string Pick(params string[] names)
        {
            using InstalledFontCollection fonts = new InstalledFontCollection();

            foreach (string name in names)
                if (fonts.Families.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    return name;

            return "Segoe UI";
        }

        public static Font Ui(float size, FontStyle style = FontStyle.Bold) =>
            new Font(UiFamily, size, style, GraphicsUnit.Point);

        /// <summary>Rectangle with cut (chamfered) corners.</summary>
        public static GraphicsPath Chamfer(RectangleF r, float c)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddPolygon(new[]
            {
                new PointF(r.Left + c, r.Top),
                new PointF(r.Right - c, r.Top),
                new PointF(r.Right, r.Top + c),
                new PointF(r.Right, r.Bottom - c),
                new PointF(r.Right - c, r.Bottom),
                new PointF(r.Left + c, r.Bottom),
                new PointF(r.Left, r.Bottom - c),
                new PointF(r.Left, r.Top + c)
            });
            return p;
        }

        public static void HighQuality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        /// <summary>Soft outer glow around a path.</summary>
        public static void Glow(Graphics g, GraphicsPath path, Color color, int size, int maxAlpha)
        {
            for (int i = size; i >= 1; i--)
            {
                int alpha = maxAlpha * (size - i + 1) / (size * size);
                using Pen pen = new Pen(Color.FromArgb(Math.Clamp(alpha, 0, 255), color), i * 2)
                {
                    LineJoin = LineJoin.Round
                };
                g.DrawPath(pen, path);
            }
        }

        public static Color Lighten(Color c, float amount) => Color.FromArgb(
            c.A,
            (int)(c.R + (255 - c.R) * amount),
            (int)(c.G + (255 - c.G) * amount),
            (int)(c.B + (255 - c.B) * amount));
    }

    // =====================================================================
    // Race / dice icons (original drawings, not game logos)
    // =====================================================================

    internal enum ArenaIcon { Terran, Protoss, Zerg, Random }

    internal static class Icons
    {
        public static void Draw(Graphics g, ArenaIcon icon, RectangleF r, bool dim = false)
        {
            GraphicsState state = g.Save();
            Theme.HighQuality(g);

            switch (icon)
            {
                case ArenaIcon.Terran: DrawShield(g, r); break;
                case ArenaIcon.Protoss: DrawCrystal(g, r); break;
                case ArenaIcon.Zerg: DrawClaws(g, r); break;
                default: DrawDice(g, r); break;
            }

            if (dim)
            {
                using SolidBrush veil = new SolidBrush(Color.FromArgb(120, Theme.ButtonBottom));
                g.FillRectangle(veil, r);
            }

            g.Restore(state);
        }

        /// <summary>Armored shield with visor - stands for the human faction.</summary>
        private static void DrawShield(Graphics g, RectangleF r)
        {
            float w = r.Width, h = r.Height, x = r.X, y = r.Y;

            using GraphicsPath shield = new GraphicsPath();
            shield.AddLines(new[]
            {
                new PointF(x + w * 0.50f, y + h * 0.02f),
                new PointF(x + w * 0.90f, y + h * 0.16f),
                new PointF(x + w * 0.86f, y + h * 0.58f),
                new PointF(x + w * 0.50f, y + h * 0.98f),
                new PointF(x + w * 0.14f, y + h * 0.58f),
                new PointF(x + w * 0.10f, y + h * 0.16f)
            });
            shield.CloseFigure();

            using (LinearGradientBrush steel = new LinearGradientBrush(r,
                       Color.FromArgb(244, 248, 252), Color.FromArgb(108, 128, 152), 90f))
            {
                ColorBlend blend = new ColorBlend
                {
                    Colors = new[]
                    {
                        Color.FromArgb(250, 252, 255), Color.FromArgb(176, 194, 214),
                        Color.FromArgb(228, 236, 245), Color.FromArgb(96, 116, 142)
                    },
                    Positions = new[] { 0f, 0.42f, 0.55f, 1f }
                };
                steel.InterpolationColors = blend;
                g.FillPath(steel, shield);
            }

            using (Pen edge = new Pen(Color.FromArgb(40, 58, 82), Math.Max(1.2f, w * 0.05f)) { LineJoin = LineJoin.Round })
                g.DrawPath(edge, shield);

            // Center ridge
            using (Pen ridge = new Pen(Color.FromArgb(150, 50, 70, 96), Math.Max(1f, w * 0.04f)))
                g.DrawLine(ridge, x + w * 0.5f, y + h * 0.12f, x + w * 0.5f, y + h * 0.86f);

            // Visor
            using GraphicsPath visor = new GraphicsPath();
            visor.AddPolygon(new[]
            {
                new PointF(x + w * 0.24f, y + h * 0.34f),
                new PointF(x + w * 0.76f, y + h * 0.34f),
                new PointF(x + w * 0.70f, y + h * 0.46f),
                new PointF(x + w * 0.30f, y + h * 0.46f)
            });

            using (LinearGradientBrush glass = new LinearGradientBrush(r,
                       Color.FromArgb(120, 210, 255), Color.FromArgb(20, 90, 180), 0f))
                g.FillPath(glass, visor);
        }

        /// <summary>Faceted golden crystal.</summary>
        private static void DrawCrystal(Graphics g, RectangleF r)
        {
            float w = r.Width, h = r.Height, x = r.X, y = r.Y;

            PointF top = new PointF(x + w * 0.50f, y + h * 0.00f);
            PointF upperL = new PointF(x + w * 0.22f, y + h * 0.30f);
            PointF upperR = new PointF(x + w * 0.78f, y + h * 0.30f);
            PointF lowerL = new PointF(x + w * 0.28f, y + h * 0.72f);
            PointF lowerR = new PointF(x + w * 0.72f, y + h * 0.72f);
            PointF bottom = new PointF(x + w * 0.50f, y + h * 1.00f);
            PointF core = new PointF(x + w * 0.50f, y + h * 0.40f);

            void Facet(Color c, params PointF[] pts)
            {
                using SolidBrush b = new SolidBrush(c);
                g.FillPolygon(b, pts);
            }

            Facet(Color.FromArgb(255, 238, 168), top, upperL, core);
            Facet(Color.FromArgb(255, 200, 84), top, core, upperR);
            Facet(Color.FromArgb(240, 160, 40), upperL, lowerL, bottom, core);
            Facet(Color.FromArgb(196, 108, 20), core, bottom, lowerR, upperR);

            // Energy core
            using (GraphicsPath glowPath = new GraphicsPath())
            {
                glowPath.AddEllipse(x + w * 0.38f, y + h * 0.28f, w * 0.24f, h * 0.24f);
                using PathGradientBrush pg = new PathGradientBrush(glowPath)
                {
                    CenterColor = Color.FromArgb(230, 190, 255, 255),
                    SurroundColors = new[] { Color.FromArgb(0, 120, 220, 255) }
                };
                g.FillPath(pg, glowPath);
            }

            using Pen edge = new Pen(Color.FromArgb(120, 70, 20), Math.Max(1f, w * 0.035f)) { LineJoin = LineJoin.Round };
            g.DrawPolygon(edge, new[] { top, upperR, lowerR, bottom, lowerL, upperL });
        }

        /// <summary>Three curved claw slashes.</summary>
        private static void DrawClaws(Graphics g, RectangleF r)
        {
            float w = r.Width, h = r.Height, x = r.X, y = r.Y;

            for (int i = 0; i < 3; i++)
            {
                float ox = x + w * (0.12f + i * 0.26f);
                float oy = y + h * (i == 1 ? 0.0f : 0.08f);

                using GraphicsPath claw = new GraphicsPath();
                // Outer curve (top -> tip), inner curve back (tip -> top)
                claw.AddBezier(
                    ox + w * 0.10f, oy,
                    ox + w * 0.34f, oy + h * 0.25f,
                    ox + w * 0.30f, oy + h * 0.65f,
                    ox + w * 0.02f, oy + h * 0.92f);
                claw.AddBezier(
                    ox + w * 0.02f, oy + h * 0.92f,
                    ox + w * 0.18f, oy + h * 0.60f,
                    ox + w * 0.18f, oy + h * 0.28f,
                    ox + w * 0.00f, oy + h * 0.06f);
                claw.CloseFigure();

                using LinearGradientBrush purple = new LinearGradientBrush(r,
                    Color.FromArgb(226, 176, 255), Color.FromArgb(110, 36, 200), 90f);
                g.FillPath(purple, claw);

                using Pen edge = new Pen(Color.FromArgb(70, 20, 120), Math.Max(1f, w * 0.03f)) { LineJoin = LineJoin.Round };
                g.DrawPath(edge, claw);
            }
        }

        /// <summary>Isometric dice.</summary>
        private static void DrawDice(Graphics g, RectangleF r)
        {
            float s = Math.Min(r.Width, r.Height);
            float x = r.X + (r.Width - s) / 2, y = r.Y + (r.Height - s) / 2;

            PointF top = new PointF(x + s * 0.50f, y + s * 0.04f);
            PointF left = new PointF(x + s * 0.08f, y + s * 0.26f);
            PointF right = new PointF(x + s * 0.92f, y + s * 0.26f);
            PointF center = new PointF(x + s * 0.50f, y + s * 0.48f);
            PointF bottomL = new PointF(x + s * 0.08f, y + s * 0.74f);
            PointF bottomR = new PointF(x + s * 0.92f, y + s * 0.74f);
            PointF bottom = new PointF(x + s * 0.50f, y + s * 0.96f);

            void Face(Color c, params PointF[] pts)
            {
                using SolidBrush b = new SolidBrush(c);
                g.FillPolygon(b, pts);
            }

            Face(Color.FromArgb(250, 252, 255), top, right, center, left);
            Face(Color.FromArgb(214, 222, 232), left, center, bottom, bottomL);
            Face(Color.FromArgb(160, 172, 188), center, right, bottomR, bottom);

            using (Pen edge = new Pen(Color.FromArgb(50, 62, 80), Math.Max(1f, s * 0.035f)) { LineJoin = LineJoin.Round })
            {
                g.DrawPolygon(edge, new[] { top, right, bottomR, bottom, bottomL, left });
                g.DrawLine(edge, left, center);
                g.DrawLine(edge, center, right);
                g.DrawLine(edge, center, bottom);
            }

            using SolidBrush pip = new SolidBrush(Color.FromArgb(30, 40, 56));
            float d = s * 0.11f;

            void Pip(float px, float py, float scaleX = 1f) =>
                g.FillEllipse(pip, px - d * scaleX / 2, py - d / 2.6f, d * scaleX, d / 1.3f);

            // top face: 1 pip
            Pip(x + s * 0.50f, y + s * 0.26f, 1.4f);

            // left face: 2 pips
            Pip(x + s * 0.20f, y + s * 0.42f, 0.8f);
            Pip(x + s * 0.38f, y + s * 0.74f, 0.8f);

            // right face: 3 pips
            Pip(x + s * 0.80f, y + s * 0.40f, 0.8f);
            Pip(x + s * 0.71f, y + s * 0.58f, 0.8f);
            Pip(x + s * 0.62f, y + s * 0.76f, 0.8f);
        }
    }

    // =====================================================================
    // Base for owner-drawn controls
    // =====================================================================

    internal class ArenaControl : Control
    {
        protected bool hover;
        protected bool pressed;

        /// <summary>Color behind the control (the panel it sits on).</summary>
        public Color Surface { get; set; } = Theme.Row;

        /// <summary>
        /// Sprite name in the skin, e.g. "race_terran" (+ "_normal", "_hover",
        /// "_pressed", "_disabled"). Null = drawn in code.
        /// </summary>
        public string? SkinName { get; set; }

        /// <summary>UI sounds (Sfx); null = silent.</summary>
        public string? HoverSound { get; set; } = "hover";
        public string? ClickSound { get; set; } = "click";

        public ArenaControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint |
                ControlStyles.SupportsTransparentBackColor, true);

            // Transparent: WinForms paints the form (frame, rows, glow) behind
            // the control, so glows and chamfered corners blend in.
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();

            if (Enabled)
                Sfx.Play(HoverSound);

            base.OnMouseEnter(e);
        }

        protected override void OnClick(EventArgs e)
        {
            if (Enabled)
                Sfx.Play(ClickSound);

            base.OnClick(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected Graphics Begin(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.HighQuality(g);
            return g;
        }

        /// <summary>State part of the sprite name.</summary>
        protected string StateSuffix => !Enabled ? "_disabled" : pressed ? "_pressed" : hover ? "_hover" : "_normal";

        /// <summary>
        /// Draws the sprite <paramref name="baseName"/> + state at (0, 0).
        /// Falls back to the normal sprite (dimmed when disabled); false if
        /// the skin has no such sprite.
        /// </summary>
        protected bool DrawSprite(Graphics g, string baseName)
        {
            Bitmap? bmp = Skin.Get(baseName + StateSuffix);

            if (bmp != null)
            {
                Skin.Blit(g, bmp, 0, 0);
                return true;
            }

            bmp = Skin.Get(baseName + "_normal");

            if (bmp == null)
                return false;

            if (Enabled)
            {
                Skin.Blit(g, bmp, 0, 0);
            }
            else
            {
                using System.Drawing.Imaging.ImageAttributes dim = Skin.Dimmed();
                Skin.Blit(g, bmp, 0, 0, dim);
            }

            return true;
        }
    }

    // =====================================================================
    // Race button (icon + text), selectable
    // =====================================================================

    internal sealed class RaceButton : ArenaControl
    {
        private bool selected;

        public ArenaIcon Icon { get; set; }

        /// <summary>Wide layout: icon left, text right (used for Random).</summary>
        public bool Wide { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selected
        {
            get => selected;
            set { selected = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);

            if (SkinName != null && DrawSprite(g, SkinName + (selected ? "_sel" : "")))
                return;

            const float pad = 5f;
            RectangleF r = new RectangleF(pad, pad, Width - pad * 2 - 1, Height - pad * 2 - 1);

            using GraphicsPath shape = Theme.Chamfer(r, 6);

            if (selected)
                Theme.Glow(g, shape, Theme.SelectedEdge, 5, 150);

            Color top = selected ? Theme.SelectedTop : Theme.ButtonTop;
            Color bottom = selected ? Theme.SelectedBottom : Theme.ButtonBottom;

            if (hover && Enabled)
            {
                top = Theme.Lighten(top, 0.08f);
                bottom = Theme.Lighten(bottom, 0.05f);
            }

            using (LinearGradientBrush fill = new LinearGradientBrush(r, top, bottom, 90f))
                g.FillPath(fill, shape);

            // Thin top highlight
            using (Pen shine = new Pen(Color.FromArgb(selected ? 110 : 45, 255, 255, 255)))
                g.DrawLine(shine, r.Left + 7, r.Top + 1.5f, r.Right - 7, r.Top + 1.5f);

            Color edge = selected ? Theme.SelectedEdge : hover ? Theme.Lighten(Theme.ButtonEdge, 0.25f) : Theme.ButtonEdge;

            using (Pen border = new Pen(edge, selected ? 2f : 1.2f))
                g.DrawPath(border, shape);

            Color textColor = Enabled ? Theme.Text : Theme.TextMuted;

            using Font font = Theme.Ui(11.5f);
            using SolidBrush brush = new SolidBrush(textColor);

            if (Wide)
            {
                float iconSize = Math.Min(r.Height * 0.55f, 42);
                RectangleF iconRect = new RectangleF(r.Left + 14, r.Top + (r.Height - iconSize) / 2, iconSize, iconSize);
                Icons.Draw(g, Icon, iconRect, !Enabled);

                RectangleF textRect = new RectangleF(iconRect.Right + 4, r.Top, r.Right - iconRect.Right - 8, r.Height);
                using StringFormat sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    FormatFlags = StringFormatFlags.NoWrap
                };
                g.DrawString(Text, font, brush, textRect, sf);
            }
            else
            {
                float iconSize = Math.Min(r.Height * 0.44f, 40);
                RectangleF iconRect = new RectangleF(r.Left + (r.Width - iconSize) / 2, r.Top + r.Height * 0.12f, iconSize, iconSize);
                Icons.Draw(g, Icon, iconRect, !Enabled);

                RectangleF textRect = new RectangleF(r.Left, iconRect.Bottom + 2, r.Width, r.Bottom - iconRect.Bottom - 6);
                using StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(Text, font, brush, textRect, sf);
            }
        }
    }

    // =====================================================================
    // Big glowing button (PLAY / EXIT)
    // =====================================================================

    internal sealed class GlowButton : ArenaControl
    {
        public bool Primary { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);

            if (SkinName != null && DrawSprite(g, SkinName))
                return;

            // Metal key, drawn in code (dialogs). Primary = lit in the race color.
            const float pad = 6f;
            RectangleF r = new RectangleF(pad, pad, Width - pad * 2 - 1, Height - pad * 2 - 1);

            if (pressed && Enabled)
                r.Offset(0, 1.5f);

            using GraphicsPath shape = Theme.Chamfer(r, 10);

            Color acc = Theme.Accent;
            bool lit = Primary && Enabled;

            // Shadow under the key
            using (GraphicsPath shadow = Theme.Chamfer(new RectangleF(r.X, r.Y + 3, r.Width, r.Height), 10))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                g.FillPath(b, shadow);

            if (lit)
                Theme.Glow(g, shape, acc, 5, hover ? 200 : 130);

            Color top, mid, bottom;

            if (lit)
            {
                float k = hover ? 1.25f : pressed ? 0.8f : 1f;
                top = Theme.Scale(acc, 0.36f * k);
                mid = Theme.Scale(acc, 0.16f * k);
                bottom = Theme.Scale(acc, 0.10f);
            }
            else if (!Enabled)
            {
                top = Color.FromArgb(52, 56, 62); mid = Color.FromArgb(34, 37, 41); bottom = Color.FromArgb(24, 26, 29);
            }
            else if (pressed)
            {
                top = Color.FromArgb(40, 44, 49); mid = Color.FromArgb(34, 37, 42); bottom = Color.FromArgb(30, 33, 37);
            }
            else if (hover)
            {
                top = Color.FromArgb(98, 105, 115); mid = Color.FromArgb(54, 59, 66); bottom = Color.FromArgb(36, 40, 45);
            }
            else
            {
                top = Color.FromArgb(78, 84, 92); mid = Color.FromArgb(42, 46, 52); bottom = Color.FromArgb(28, 31, 35);
            }

            using (LinearGradientBrush fill = new LinearGradientBrush(r, top, bottom, 90f))
            {
                fill.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { top, mid, bottom },
                    Positions = new[] { 0f, 0.5f, 1f }
                };
                g.FillPath(fill, shape);
            }

            DrawHexTexture(g, shape, r, lit ? Color.FromArgb(30, Theme.Lighten(acc, 0.4f)) : Color.FromArgb(12, 170, 185, 200));

            // Bevel: light top edge
            using (Pen shine = new Pen(Color.FromArgb(lit ? 150 : 90, lit ? Theme.Lighten(acc, 0.5f) : Color.White), 1.2f))
                g.DrawLine(shine, r.Left + 10, r.Top + 1, r.Right - 10, r.Top + 1);

            Color edge = lit ? acc : hover && Enabled ? Theme.Mix(Color.FromArgb(5, 6, 7), acc, 0.55f) : Color.FromArgb(5, 6, 7);

            using (Pen border = new Pen(edge, 1.4f))
                g.DrawPath(border, shape);

            // LED
            float lx = r.Right - 14, ly = r.Top + 12;
            if (lit)
            {
                using SolidBrush halo = new SolidBrush(Color.FromArgb(90, acc));
                g.FillEllipse(halo, lx - 6, ly - 6, 12, 12);
            }

            using (SolidBrush led = new SolidBrush(lit ? Theme.Lighten(acc, 0.5f) : Color.FromArgb(20, 22, 24)))
                g.FillEllipse(led, lx - 3, ly - 3, 6, 6);

            // Text
            float size = Math.Clamp(r.Height * 0.30f, 12f, Primary ? 24f : 17f);
            using FontFamily family = new FontFamily(Theme.TitleFamily);
            using GraphicsPath text = new GraphicsPath();
            using StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            text.AddString(Text, family, (int)FontStyle.Bold, size * 96f / 72f, r, sf);

            if (lit)
                Theme.Glow(g, text, acc, 3, 140);

            using (Matrix down = new Matrix())
            using (GraphicsPath shadowText = (GraphicsPath)text.Clone())
            using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            {
                down.Translate(0, 1.2f);
                shadowText.Transform(down);
                g.FillPath(b, shadowText);
            }

            Color textColor = !Enabled ? Color.FromArgb(110, 115, 122)
                : lit ? Theme.Lighten(acc, 0.6f)
                : hover ? Color.FromArgb(235, 240, 246) : Color.FromArgb(180, 188, 198);

            using (SolidBrush b = new SolidBrush(textColor))
                g.FillPath(b, text);
        }

        private static void DrawHexTexture(Graphics g, GraphicsPath clip, RectangleF r, Color color)
        {
            GraphicsState state = g.Save();
            g.SetClip(clip);

            using Pen hex = new Pen(color, 1f);
            float size = 9f;
            float hStep = size * 1.732f;

            for (int row = 0; row * size * 1.5f < r.Height + size; row++)
            {
                for (int col = 0; col * hStep < r.Width + hStep; col++)
                {
                    float cx = r.X + col * hStep + (row % 2 == 1 ? hStep / 2 : 0);
                    float cy = r.Y + row * size * 1.5f;

                    PointF[] pts = new PointF[6];
                    for (int i = 0; i < 6; i++)
                    {
                        double a = Math.PI / 3 * i + Math.PI / 6;
                        pts[i] = new PointF(cx + (float)Math.Cos(a) * size, cy + (float)Math.Sin(a) * size);
                    }

                    g.DrawPolygon(hex, pts);
                }
            }

            g.Restore(state);
        }
    }

    // =====================================================================
    // Dark drop-down (replaces ComboBox, whose look can't be changed)
    // =====================================================================

    internal sealed class DarkSelect : ArenaControl
    {
        private int selectedIndex = -1;
        private readonly ContextMenuStrip menu;

        public List<object> Items { get; } = new();

        /// <summary>
        /// Monitor style (main window): the field itself is part of the window
        /// background; only the green text and the hover glow are drawn here.
        /// </summary>
        public bool Crt { get; set; }

        public event EventHandler? SelectedIndexChanged;

        public DarkSelect()
        {
            ClickSound = "click";

            menu = new ContextMenuStrip
            {
                Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors(false)) { RoundedEdges = false },
                ShowImageMargin = false,
                BackColor = Color.FromArgb(20, 22, 25),
                ForeColor = Theme.Text,
                Font = Theme.Ui(11f, FontStyle.Regular)
            };

            menu.Closed += (_, _) => Invalidate();
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedIndex
        {
            get => selectedIndex;
            set
            {
                if (value < -1 || value >= Items.Count || value == selectedIndex)
                    return;

                selectedIndex = value;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Selects an index after the item list was replaced.</summary>
        public void ResetSelection(int index)
        {
            selectedIndex = -1;
            SelectedIndex = Math.Clamp(index, -1, Items.Count - 1);
            Invalidate();
        }

        public object? SelectedItem => selectedIndex >= 0 && selectedIndex < Items.Count ? Items[selectedIndex] : null;

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);

            if (!Enabled || Items.Count == 0)
                return;

            menu.Items.Clear();

            if (Crt)
            {
                menu.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors(true)) { RoundedEdges = false };
                menu.BackColor = Color.FromArgb(6, 14, 9);
                menu.Font = Skin.Mono(12f);
            }

            for (int i = 0; i < Items.Count; i++)
            {
                int index = i;
                ToolStripMenuItem item = new ToolStripMenuItem(Items[i]?.ToString() ?? "")
                {
                    AutoSize = false,
                    Width = Width - 12,
                    Height = 34,
                    Padding = new Padding(10, 0, 0, 0),
                    ForeColor = Crt
                        ? (i == selectedIndex ? Skin.Phosphor : Color.FromArgb(105, 200, 125))
                        : (i == selectedIndex ? Theme.Accent : Theme.Text),
                    Checked = false
                };

                item.Click += (_, _) => SelectedIndex = index;
                menu.Items.Add(item);
            }

            menu.MinimumSize = new Size(Width - 12, 0);
            menu.Show(this, new Point(6, Height - 6));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);

            if (Crt)
            {
                PaintCrt(g);
                return;
            }

            const float pad = 6f;
            RectangleF r = new RectangleF(pad, pad, Width - pad * 2 - 1, Height - pad * 2 - 1);

            using GraphicsPath shape = Theme.Chamfer(r, 5);

            bool open = menu.Visible;

            if ((hover || open) && Enabled)
                Theme.Glow(g, shape, Theme.Accent, 4, 90);

            using (LinearGradientBrush fill = new LinearGradientBrush(r,
                       Color.FromArgb(9, 15, 24), Color.FromArgb(14, 22, 34), 90f))
                g.FillPath(fill, shape);

            Color edge = (hover || open) && Enabled ? Theme.Lighten(Theme.ButtonEdge, 0.35f) : Theme.ButtonEdge;

            using (Pen border = new Pen(edge, 1.4f))
                g.DrawPath(border, shape);

            using Font font = Theme.Ui(12.5f, FontStyle.Regular);
            using SolidBrush brush = new SolidBrush(Enabled ? Theme.Text : Theme.TextMuted);
            using StringFormat sf = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            string text = SelectedItem?.ToString() ?? "(select)";
            g.DrawString(text, font, brush, new RectangleF(r.Left + 16, r.Top, r.Width - 60, r.Height), sf);

            // Arrow
            float ax = r.Right - 30, ay = r.Top + r.Height / 2;
            using SolidBrush arrow = new SolidBrush(Enabled ? Theme.Text : Theme.TextMuted);
            g.FillPolygon(arrow, new[]
            {
                new PointF(ax - 8, ay - 4),
                new PointF(ax + 8, ay - 4),
                new PointF(ax, ay + 5)
            });
        }

        private void PaintCrt(Graphics g)
        {
            bool open = menu.Visible;
            RectangleF r = new RectangleF(1.5f, 1.5f, Width - 4, Height - 4);

            if ((hover || open) && Enabled)
            {
                using GraphicsPath shape = Theme.Chamfer(r, 7);
                Theme.Glow(g, shape, Theme.Accent, 3, 110);

                using Pen border = new Pen(Color.FromArgb(220, Theme.Accent), 1.3f);
                g.DrawPath(border, shape);
            }

            using Font font = Skin.Mono(13f);
            using StringFormat sf = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            string text = SelectedItem?.ToString() ?? "(select)";
            Color color = Enabled ? (hover || open ? Theme.Lighten(Skin.Phosphor, 0.4f) : Skin.Phosphor) : Skin.PhosphorDim;

            // 50 px on the right: the arrow button of the background
            Skin.GlowText(g, text, font, color, new RectangleF(16, 0, Width - 70, Height), sf, Enabled);
        }

        private sealed class DarkMenuColors : ProfessionalColorTable
        {
            private readonly bool crt;

            public DarkMenuColors(bool crt) => this.crt = crt;

            private Color Back => crt ? Color.FromArgb(6, 14, 9) : Color.FromArgb(20, 22, 25);
            private Color Hot => crt ? Color.FromArgb(18, 58, 32) : Theme.Scale(Theme.Accent, 0.3f);

            public override Color ToolStripDropDownBackground => Back;
            public override Color MenuBorder => crt ? Skin.PhosphorDim : Theme.ButtonEdge;
            public override Color MenuItemBorder => crt ? Skin.Phosphor : Theme.SelectedEdge;
            public override Color MenuItemSelected => Hot;
            public override Color MenuItemSelectedGradientBegin => Hot;
            public override Color MenuItemSelectedGradientEnd => Hot;
            public override Color ImageMarginGradientBegin => Back;
            public override Color ImageMarginGradientMiddle => Back;
            public override Color ImageMarginGradientEnd => Back;
        }
    }

    // =====================================================================
    // Small check box
    // =====================================================================

    internal sealed class ArenaCheck : ArenaControl
    {
        private bool isChecked;

        public event EventHandler? CheckedChanged;

        /// <summary>Main window style: a switch sprite with a green monitor-style label.</summary>
        public bool Switch { get; set; }

        public ArenaCheck() => ClickSound = "toggle";

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Checked
        {
            get => isChecked;
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled) Checked = !Checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);

            if (Switch)
            {
                Bitmap? sprite = Skin.Get((isChecked ? "toggle_on" : "toggle_off") + (hover ? "_hover" : ""));
                int spriteWidth = 40;

                if (sprite != null)
                {
                    Skin.Blit(g, sprite, 0, (Height - sprite.Height) / 2);
                    spriteWidth = sprite.Width;
                }

                using Font mono = Skin.Mono(10f);
                using StringFormat lsf = new StringFormat { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
                Color color = isChecked ? (hover ? Theme.Lighten(Skin.Phosphor, 0.4f) : Skin.Phosphor)
                                        : (hover ? Color.FromArgb(95, 190, 115) : Skin.PhosphorDim);
                Skin.GlowText(g, Text, mono, color, new RectangleF(spriteWidth + 6, 0, Width - spriteWidth - 6, Height), lsf, isChecked);
                return;
            }

            float box = 18;
            RectangleF b = new RectangleF(2, (Height - box) / 2, box, box);

            using (GraphicsPath shape = Theme.Chamfer(b, 3))
            {
                using SolidBrush fill = new SolidBrush(isChecked ? Theme.SelectedBottom : Color.FromArgb(9, 15, 24));
                g.FillPath(fill, shape);

                using Pen border = new Pen(isChecked || hover ? Theme.SelectedEdge : Theme.ButtonEdge, 1.3f);
                g.DrawPath(border, shape);
            }

            if (isChecked)
            {
                using Pen tick = new Pen(Color.White, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                g.DrawLines(tick, new[]
                {
                    new PointF(b.Left + 4, b.Top + 9.5f),
                    new PointF(b.Left + 7.5f, b.Top + 13),
                    new PointF(b.Left + 14, b.Top + 5)
                });
            }

            using Font font = Theme.Ui(10f, FontStyle.Regular);
            using SolidBrush brush = new SolidBrush(hover ? Theme.Text : Theme.TextMuted);
            using StringFormat sf = new StringFormat { LineAlignment = StringAlignment.Center };
            g.DrawString(Text, font, brush, new RectangleF(b.Right + 8, 0, Width - b.Right - 8, Height), sf);
        }
    }

    // =====================================================================
    // Small text button on a green monitor ("[ STATS ]")
    // =====================================================================

    internal sealed class CrtLink : ArenaControl
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Begin(e);

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);

            if (hover && Enabled)
            {
                using GraphicsPath shape = Theme.Chamfer(r, 3);
                using SolidBrush fill = new SolidBrush(Color.FromArgb(60, Skin.Phosphor));
                g.FillPath(fill, shape);
                using Pen edge = new Pen(Skin.Phosphor, 1f);
                g.DrawPath(edge, shape);
            }

            using Font font = Skin.Mono(9.5f, FontStyle.Bold);
            using StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            Skin.GlowText(g, "[ " + Text + " ]", font, hover ? Color.White : Skin.Phosphor, r, sf);
        }
    }

    // =====================================================================
    // Minimize / close buttons for the borderless window
    // =====================================================================

    internal sealed class WindowButton : ArenaControl
    {
        public bool IsClose { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.HighQuality(g);

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using GraphicsPath shape = Theme.Chamfer(r, 4);

            using (SolidBrush bg = new SolidBrush(hover
                       ? (IsClose ? Color.FromArgb(200, 170, 36, 26) : Color.FromArgb(70, Skin.Phosphor))
                       : Color.FromArgb(120, 0, 0, 0)))
                g.FillPath(bg, shape);

            using (Pen edge = new Pen(hover ? (IsClose ? Color.FromArgb(255, 120, 100) : Skin.Phosphor) : Skin.PhosphorDim, 1f))
                g.DrawPath(edge, shape);

            float cx = Width / 2f, cy = Height / 2f, s = 5f;

            using Pen pen = new Pen(hover ? Color.White : Skin.Phosphor, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

            if (IsClose)
            {
                g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                g.DrawLine(pen, cx + s, cy - s, cx - s, cy + s);
            }
            else
            {
                g.DrawLine(pen, cx - s, cy, cx + s, cy);
            }
        }
    }
}
