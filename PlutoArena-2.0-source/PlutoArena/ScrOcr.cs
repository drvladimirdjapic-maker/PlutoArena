using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace PlutoArena
{
    /// <summary>
    /// One line of text found on the SC:R screen, with its position in
    /// virtual 640x480 menu coordinates.
    /// </summary>
    internal sealed record ScreenText(string Text, RectangleF Bounds)
    {
        public PointF Center => new PointF(
            Bounds.X + Bounds.Width / 2,
            Bounds.Y + Bounds.Height / 2);
    }

    /// <summary>
    /// Reads the text on the StarCraft screen with the built-in Windows OCR.
    /// Used to wait until a menu screen is really shown (instead of
    /// guessing with long sleeps) and to find items such as a profile name.
    ///
    /// If OCR is not available (no OCR language installed, or the game
    /// runs in exclusive fullscreen so the capture is black) every method
    /// simply returns "unknown" and the launcher falls back to fixed delays.
    /// </summary>
    internal static class ScrOcr
    {
        private static OcrEngine? engine;
        private static bool initialized;
        private static int blackCaptures;
        private static int goodCaptures;

        public static bool Available { get; private set; }

        /// <summary>Language of the OCR engine in use, e.g. "en-US".</summary>
        public static string LanguageTag { get; private set; } = "";

        public const string EnglishHelp =
            "add English (United States) in Settings > Time & language > Language & region, " +
            "or run in PowerShell as administrator: Add-WindowsCapability -Online -Name \"Language.OCR~~~en-US~0.0.1.0\"";

        /// <summary>True when the OCR engine reads English (needed for the StarCraft menus).</summary>
        public static bool IsEnglish => LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase);

        /// <summary>Set by the launcher so OCR problems end up in PlutoArena.log.</summary>
        public static Action<string>? Logger { get; set; }

        private static int debugImages;

        /// <summary>Save captures without text to the debug folder (PlutoArena.ini: DebugImages=1).</summary>
        public static bool SaveDebugImages { get; set; }

        /// <summary>Saves what was captured (max 8 per run) to the "debug" folder next to the exe.</summary>
        private static void SaveDebug(Bitmap bmp, string reason)
        {
            try
            {
                if (!SaveDebugImages || debugImages >= 6)
                    return;

                string folder = Path.Combine(AppContext.BaseDirectory, "debug");
                Directory.CreateDirectory(folder);

                string file = Path.Combine(folder, $"capture-{DateTime.Now:HHmmss-fff}-{reason}.png");
                bmp.Save(file, ImageFormat.Png);
                debugImages++;

                Logger?.Invoke($"  [ocr] saved {Path.GetFileName(file)}");
            }
            catch
            {
            }
        }

        public static void ResetDebug() => debugImages = 0;

        /// <summary>True when OCR exists but the game screen could not be captured.</summary>
        public static bool CaptureFailed { get; private set; }

        /// <summary>Capture method that worked last: false = screen copy, true = PrintWindow.</summary>
        private static bool? preferPrintWindow;

        public static string CaptureMethod =>
            preferPrintWindow == null ? "unknown" : preferPrintWindow.Value ? "PrintWindow" : "screen copy";

        /// <summary>Captures the menu area, trying both methods; null if both are black.</summary>
        private static Bitmap? Capture(IntPtr hwnd)
        {
            // Always try the screen copy first: it shows exactly what is on
            // screen. PrintWindow is only a fallback - with SC:R it can return
            // an empty or stale picture (even the other StarCraft's screen).
            // A black screen copy can also just mean SC:R is still loading.
            foreach (bool usePrintWindow in new[] { false, true })
            {
                Bitmap? bmp = null;

                try
                {
                    bmp = ScrInput.CaptureMenuArea(hwnd, usePrintWindow);
                }
                catch
                {
                }

                if (bmp == null)
                    continue;

                if (IsBlack(bmp))
                {
                    bmp.Dispose();
                    continue;
                }

                preferPrintWindow = usePrintWindow;
                return bmp;
            }

            return null;
        }

        public static void Initialize()
        {
            if (initialized)
                return;

            initialized = true;

            try
            {
                // The StarCraft menus are English, so an English recognizer is
                // needed: any installed English variant (en-US, en-GB ...).
                // Another language reads the menus as garbage.
                foreach (string tag in new[] { "en-US", "en-GB" })
                {
                    engine ??= OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(tag));
                }

                if (engine == null)
                {
                    foreach (Windows.Globalization.Language lang in OcrEngine.AvailableRecognizerLanguages)
                    {
                        if (lang.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                        {
                            engine = OcrEngine.TryCreateFromLanguage(lang);

                            if (engine != null)
                                break;
                        }
                    }
                }

                // Last resort (menus will probably not be read correctly).
                engine ??= OcrEngine.TryCreateFromUserProfileLanguages();

                Available = engine != null;
                LanguageTag = engine?.RecognizerLanguage?.LanguageTag ?? "";
            }
            catch
            {
                Available = false;
            }
        }

        /// <summary>
        /// Reads all lines of text on the window's menu area.
        /// <paramref name="want"/>: texts the caller is looking for. When the
        /// normal reading has none of them (garbage on a bright background),
        /// the high-contrast reading is added as well.
        /// </summary>
        public static List<ScreenText>? Read(IntPtr hwnd, string[]? want = null, bool activate = true)
        {
            Initialize();

            if (!Available || engine == null)
                return null;

            try
            {
                // The screen copy needs this StarCraft window in front.
                // (During the game it already is: do not touch the focus then.)
                if (activate)
                    ScrInput.Activate(hwnd);

                List<ScreenText>? lines = null;
                List<Bitmap> captures = new();

                try
                {
                    // 1st: the window's own area. 2nd: the whole monitor the
                    // window is on (both StarCraft windows are fullscreen).
                    foreach (bool monitor in new[] { false, true })
                    {
                        Bitmap? bmp = monitor ? ScrInput.CaptureMonitorMenuArea(hwnd) : Capture(hwnd);

                        if (bmp == null)
                            continue;

                        if (IsBlack(bmp))
                        {
                            bmp.Dispose();
                            continue;
                        }

                        captures.Add(bmp);
                    }

                    if (captures.Count > 0)
                    {
                        // Pass 1: plain OCR of the whole picture - the normal case.
                        for (int i = 0; i < captures.Count && lines == null; i++)
                        {
                            List<ScreenText> found = RecognizeWhole(captures[i]);

                            if (found.Count > 0)
                            {
                                if (i > 0)
                                    Logger?.Invoke($"  [ocr] window area gave no text, monitor area worked ({ScrInput.DescribeWindow(hwnd)})");

                                lines = found;
                            }
                        }

                        // Pass 2: bright backgrounds (the moon on Select Connection,
                        // Attitude SE ...) - high-contrast copy + halves.
                        for (int i = 0; i < captures.Count && lines == null; i++)
                        {
                            List<ScreenText> found = RecognizeHard(captures[i]);

                            if (found.Count > 0)
                                lines = found;
                            else if (i == 0)
                                SaveDebug(captures[i], "notext");
                        }

                        // Pass 3: something was read, but not what we are waiting
                        // for - maybe garbage. Add the high-contrast reading.
                        if (lines != null && want != null && want.Length > 0 && Find(lines, want) == null)
                        {
                            List<ScreenText> hard = RecognizeHard(captures[0], masks: true, halves: false);

                            if (hard.Count > 0)
                            {
                                if (Find(hard, want) != null)
                                    Logger?.Invoke("  [ocr] found with the high-contrast copy: " + string.Join(" | ", hard.Take(8).Select(l => l.Text)));

                                lines = lines.Concat(hard).ToList();
                            }

                            if (Find(lines, want) == null)
                                SaveDebug(captures[captures.Count - 1], "nomatch");
                        }

                        lines ??= new List<ScreenText>(); // readable, but no text
                    }
                }
                finally
                {
                    foreach (Bitmap b in captures)
                        b.Dispose();
                }

                if (lines == null)
                {
                    // Black with every method. Only give up when the screen could
                    // NEVER be read; once it has worked, black is just a screen change.
                    if (goodCaptures == 0 && ++blackCaptures >= 4)
                    {
                        Available = false;
                        CaptureFailed = true;
                    }

                    return null;
                }

                if (lines.Count == 0)
                    Logger?.Invoke($"  [ocr] no text found ({ScrInput.DescribeWindow(hwnd)})");

                blackCaptures = 0;
                goodCaptures++;

                return lines;
            }
            catch (Exception ex)
            {
                Logger?.Invoke($"  [ocr] error: {ex.GetType().Name}: {ex.Message} ({ScrInput.DescribeWindow(hwnd)})");
                return null;
            }
        }

        /// <summary>
        /// Windows OCR sometimes returns nothing (or garbage) for a whole SC:R
        /// screen with a bright picture behind the panels (the moon on
        /// Select Connection, the Attitude SE map ...). Then read a
        /// high-contrast copy that keeps only the coloured menu text, plus
        /// the left and right half separately; last a smaller copy.
        /// </summary>
        private static List<ScreenText> RecognizeHard(Bitmap bmp, bool masks = true, bool halves = true)
        {
            List<ScreenText> lines = new();

            if (masks)
            {
                // Two copies: only the coloured menu text, and all bright text.
                foreach (bool brightToo in new[] { false, true })
                {
                    using Bitmap mask = TextMask(bmp, brightToo);
                    List<ScreenText> masked = RecognizeWhole(mask);

                    if (masked.Count > 0)
                        lines.AddRange(masked);
                }
            }

            if (!halves)
                return lines;

            int w = bmp.Width, h = bmp.Height;
            int half = w / 2, overlap = w / 20;
            List<ScreenText> halfLines = new();

            foreach (Rectangle part in new[]
            {
                new Rectangle(0, 0, half + overlap, h),                  // left panel (+ a little overlap)
                new Rectangle(half - overlap, 0, w - (half - overlap), h) // right panel
            })
            {
                List<ScreenText> found = RecognizePart(bmp, part);

                if (found.Count == 0)
                {
                    // Still nothing: split this half into a top and bottom part.
                    int third = part.Height / 3;
                    found.AddRange(RecognizePart(bmp, new Rectangle(part.X, 0, part.Width, part.Height / 2 + third / 2)));
                    found.AddRange(RecognizePart(bmp, new Rectangle(part.X, part.Height / 2 - third / 2, part.Width, part.Height - (part.Height / 2 - third / 2))));
                }

                halfLines.AddRange(found);
            }

            if (halfLines.Count > 0)
            {
                Logger?.Invoke("  [ocr] whole screen gave no text, halves worked");
                lines.AddRange(halfLines);
            }

            if (lines.Count > 0)
                return lines;

            using Bitmap small = new Bitmap(bmp, new Size(w / 2, h / 2));
            lines = RecognizeWhole(small);

            if (lines.Count > 0)
                Logger?.Invoke("  [ocr] whole screen gave no text, smaller copy worked");

            return lines;
        }

        /// <summary>
        /// Black text on white: keeps the SC:R menu text colours (green,
        /// yellow, white/bright cyan highlights are rare on the panels) and
        /// removes the background picture, which is mostly grey/blue.
        /// </summary>
        private static Bitmap TextMask(Bitmap source, bool brightToo)
        {
            int w = source.Width, h = source.Height;
            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            using (Bitmap src = source.Clone(new Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb))
            {
                BitmapData sd = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                BitmapData dd = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

                try
                {
                    int bytes = sd.Stride * h;
                    byte[] buf = new byte[bytes];
                    Marshal.Copy(sd.Scan0, buf, 0, bytes);

                    for (int y = 0; y < h; y++)
                    {
                        int row = y * sd.Stride;

                        for (int x = 0; x < w; x++)
                        {
                            int i = row + x * 4;
                            int b = buf[i], g = buf[i + 1], r = buf[i + 2];

                            bool green = g > 110 && g - r > 45 && g - b > 45;
                            bool yellow = r > 150 && g > 140 && b < 100;
                            // Titles/descriptions are near-white; the moon is light grey.
                            bool white = brightToo && r > 225 && g > 225 && b > 225;

                            byte v = green || yellow || white ? (byte)0 : (byte)255;
                            buf[i] = buf[i + 1] = buf[i + 2] = v;
                            buf[i + 3] = 255;
                        }
                    }

                    Marshal.Copy(buf, 0, dd.Scan0, bytes);
                }
                finally
                {
                    src.UnlockBits(sd);
                    result.UnlockBits(dd);
                }
            }

            return result;
        }

        /// <summary>OCR of one part of the picture, coordinates mapped back to the whole picture.</summary>
        private static List<ScreenText> RecognizePart(Bitmap bmp, Rectangle part)
        {
            List<ScreenText> result = new();
            int w = bmp.Width, h = bmp.Height;

            part.Intersect(new Rectangle(0, 0, w, h));

            if (part.Width < 50 || part.Height < 50)
                return result;

            using Bitmap crop = bmp.Clone(part, bmp.PixelFormat);

            float fx = part.Width / (float)w, fy = part.Height / (float)h;
            float ox = part.X * 640f / w, oy = part.Y * 480f / h;

            foreach (ScreenText t in RecognizeWhole(crop))
            {
                result.Add(new ScreenText(t.Text, new RectangleF(
                    ox + t.Bounds.X * fx, oy + t.Bounds.Y * fy,
                    t.Bounds.Width * fx, t.Bounds.Height * fy)));
            }

            return result;
        }

        private static List<ScreenText> RecognizeWhole(Bitmap bmp)
        {
            SoftwareBitmap softwareBitmap = ToSoftwareBitmap(bmp, out double scale);

            OcrResult result =
                engine!.RecognizeAsync(softwareBitmap).AsTask().GetAwaiter().GetResult();

            // OCR rectangles are in the (possibly rescaled) OCR image.
            float sx = (float)(640.0 / (bmp.Width * scale));
            float sy = (float)(480.0 / (bmp.Height * scale));

            List<ScreenText> lines = new();

            foreach (OcrLine line in result.Lines)
            {
                if (line.Words.Count == 0)
                    continue;

                double left = line.Words.Min(w => w.BoundingRect.X);
                double top = line.Words.Min(w => w.BoundingRect.Y);
                double right = line.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
                double bottom = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);

                lines.Add(new ScreenText(
                    line.Text,
                    new RectangleF(
                        (float)left * sx,
                        (float)top * sy,
                        (float)(right - left) * sx,
                        (float)(bottom - top) * sy)));
            }

            return lines;
        }

        /// <summary>
        /// Finds the first line that contains any of the given texts.
        /// Comparison ignores case, spaces and common OCR confusions.
        /// </summary>
        public static ScreenText? Find(
            List<ScreenText>? lines,
            params string[] texts)
        {
            if (lines == null)
                return null;

            foreach (string text in texts)
            {
                string wanted = Normalize(text);

                ScreenText? hit =
                    lines.FirstOrDefault(l => Normalize(l.Text).Contains(wanted));

                if (hit != null)
                    return hit;
            }

            // Second pass: tolerate 1-2 wrong letters in longer words
            // ("Titeset" for "Tileset", "scenacio" for "scenario").
            foreach (string text in texts)
            {
                string wanted = Normalize(text);

                if (wanted.Length < 6)
                    continue;

                int allowed = Math.Max(1, wanted.Length / 7);

                ScreenText? hit =
                    lines.FirstOrDefault(l => ContainsAlmost(Normalize(l.Text), wanted, allowed));

                if (hit != null)
                    return hit;
            }

            return null;
        }

        /// <summary>
        /// True when <paramref name="lineText"/> contains <paramref name="wanted"/>,
        /// allowing 1-2 wrong letters in longer texts (same rules as Find).
        /// </summary>
        public static bool Matches(string lineText, string wanted)
        {
            string n = Normalize(lineText), w = Normalize(wanted);

            if (w.Length == 0)
                return false;

            if (n.Contains(w))
                return true;

            return w.Length >= 6 && ContainsAlmost(n, w, Math.Max(1, w.Length / 7));
        }

        /// <summary>True when some part of <paramref name="text"/> equals
        /// <paramref name="wanted"/> except for at most <paramref name="allowed"/> letters.</summary>
        private static bool ContainsAlmost(string text, string wanted, int allowed)
        {
            for (int start = 0; start + wanted.Length <= text.Length; start++)
            {
                int wrong = 0;

                for (int i = 0; i < wanted.Length && wrong <= allowed; i++)
                {
                    if (text[start + i] != wanted[i])
                        wrong++;
                }

                if (wrong <= allowed)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Waits until one of the texts is visible.
        /// Returns true = seen, false = timeout, null = OCR not usable
        /// (caller should continue with a fixed delay).
        /// </summary>
        public static bool? WaitFor(
            IntPtr hwnd,
            int timeoutMs,
            CancellationToken ct,
            params string[] texts)
        {
            Initialize();

            if (!Available)
                return null;

            Stopwatch sw = Stopwatch.StartNew();
            bool anyRead = false;

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                ct.ThrowIfCancellationRequested();

                List<ScreenText>? lines = Read(hwnd, texts);

                if (lines != null)
                {
                    anyRead = true;

                    if (Find(lines, texts) != null)
                        return true;
                }
                else if (!Available)
                {
                    return null;
                }

                Thread.Sleep(250);
            }

            return anyRead ? false : null;
        }

        public static string Normalize(string s)
        {
            char[] chars = s
                .ToLowerInvariant()
                // OCR often reads a thin "l" as "\\" or "|" ("Rep\\ays").
                .Select(c => c is '\\' or '|' ? 'l' : c)
                .Where(char.IsLetterOrDigit)
                .Select(c => c switch
                {
                    '0' => 'o',
                    '1' => 'l',
                    'i' => 'l',
                    '5' => 's',
                    _ => c
                })
                .ToArray();

            return new string(chars);
        }

        private static bool IsBlack(Bitmap bmp)
        {
            int bright = 0;
            int samples = 0;

            for (int y = bmp.Height / 10; y < bmp.Height; y += bmp.Height / 10)
            {
                for (int x = bmp.Width / 10; x < bmp.Width; x += bmp.Width / 10)
                {
                    Color c = bmp.GetPixel(x, y);
                    samples++;

                    if (c.R + c.G + c.B > 30)
                        bright++;
                }
            }

            return bright < samples / 20 + 1;
        }

        private static SoftwareBitmap ToSoftwareBitmap(Bitmap source, out double scale)
        {
            // Upscale small captures a little (OCR reads small fonts better),
            // but never above the OCR engine's size limit.
            scale = source.Height < 900 ? 1.5 : 1.0;

            double limit = OcrEngine.MaxImageDimension - 8;
            scale = Math.Min(scale, Math.Min(limit / source.Width, limit / source.Height));

            int w = (int)(source.Width * scale);
            int h = (int)(source.Height * scale);

            using Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);

            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, w, h);
            }

            BitmapData data = bmp.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            byte[] bytes;

            try
            {
                bytes = new byte[data.Stride * h];
                Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            // Stride of a 32bpp bitmap is always width * 4, so the buffer is tight.
            return SoftwareBitmap.CreateCopyFromBuffer(
                CryptographicBuffer.CreateFromByteArray(bytes),
                BitmapPixelFormat.Bgra8,
                w,
                h,
                BitmapAlphaMode.Premultiplied);
        }
    }
}
