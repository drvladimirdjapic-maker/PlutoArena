using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace PlutoArena
{
    /// <summary>
    /// Low-level control of a StarCraft: Remastered window.
    ///
    /// All menu positions are expressed in "virtual" 640x480 coordinates.
    /// SC:R draws its menus as a 4:3 image centered in the window
    /// (black bars left/right on a 16:9 screen), so we map 640x480 into that
    /// 4:3 area. This makes the clicks independent of screen resolution.
    /// </summary>
    internal static class ScrInput
    {
        // ---------------------------------------------------------------
        // Win32
        // ---------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        private const uint INPUT_MOUSE = 0;
        private const uint INPUT_KEYBOARD = 1;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const uint MAPVK_VK_TO_VSC = 0;

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;
        private const byte VK_MENU = 0x12;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT p);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        private const uint GW_OWNER = 4;

        // ---------------------------------------------------------------
        // Window lookup / activation
        // ---------------------------------------------------------------

        /// <summary>
        /// Finds the main top-level window of a process. Unlike
        /// Process.MainWindowHandle this also finds hidden windows,
        /// so a hidden Pluto window can be shown again.
        /// </summary>
        public static IntPtr FindMainWindow(int pid, bool includeHidden = false)
        {
            IntPtr found = IntPtr.Zero;

            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowPid);

                if (windowPid != (uint)pid)
                    return true;

                if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero)
                    return true;

                if (!includeHidden && !IsWindowVisible(hWnd))
                    return true;

                if (!GetClientRect(hWnd, out RECT r) ||
                    r.Right - r.Left < 200 ||
                    r.Bottom - r.Top < 150)
                    return true;

                found = hWnd;
                return false;
            }, IntPtr.Zero);

            return found;
        }

        public static IntPtr WaitForMainWindow(int pid, int timeoutMs, CancellationToken ct)
        {
            Stopwatch sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                ct.ThrowIfCancellationRequested();

                IntPtr hwnd = FindMainWindow(pid);

                if (hwnd != IntPtr.Zero)
                    return hwnd;

                Thread.Sleep(200);
            }

            return IntPtr.Zero;
        }

        public static bool Activate(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return false;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (GetForegroundWindow() == hwnd && !IsIconic(hwnd))
                    return true;

                if (IsIconic(hwnd) || !IsWindowVisible(hwnd))
                    ShowWindow(hwnd, SW_RESTORE);

                uint current = GetCurrentThreadId();
                uint target = GetWindowThreadProcessId(hwnd, out _);
                uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), out _);

                // Pressing ALT lets a background process take the foreground.
                keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
                keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                bool attachedFg = foreground != current && AttachThreadInput(current, foreground, true);
                bool attachedTarget = target != current && AttachThreadInput(current, target, true);

                try
                {
                    BringWindowToTop(hwnd);
                    SetForegroundWindow(hwnd);
                }
                finally
                {
                    if (attachedTarget) AttachThreadInput(current, target, false);
                    if (attachedFg) AttachThreadInput(current, foreground, false);
                }

                Thread.Sleep(150);
            }

            return GetForegroundWindow() == hwnd;
        }

        private const int SW_MINIMIZE = 6;

        /// <summary>True when this window is in front and not minimized.</summary>
        public static bool IsForeground(IntPtr hwnd) =>
            hwnd != IntPtr.Zero && GetForegroundWindow() == hwnd && !IsIconic(hwnd);

        public static void Minimize(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero)
                ShowWindow(hwnd, SW_MINIMIZE);
        }

        public static void Hide(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero)
                ShowWindow(hwnd, SW_HIDE);
        }

        public static void Show(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero)
                ShowWindow(hwnd, SW_SHOW);
        }

        // ---------------------------------------------------------------
        // Coordinates
        // ---------------------------------------------------------------

        /// <summary>
        /// Screen rectangle of the 4:3 menu area inside the window.
        /// </summary>
        public static Rectangle GetMenuArea(IntPtr hwnd)
        {
            if (!GetClientRect(hwnd, out RECT r))
                return Rectangle.Empty;

            POINT origin = new POINT { X = 0, Y = 0 };
            ClientToScreen(hwnd, ref origin);

            int cw = r.Right - r.Left;
            int ch = r.Bottom - r.Top;

            int w, h;

            if (cw * 3 >= ch * 4)
            {
                // Wider than 4:3 -> bars left/right.
                h = ch;
                w = ch * 4 / 3;
            }
            else
            {
                // Narrower than 4:3 -> bars top/bottom.
                w = cw;
                h = cw * 3 / 4;
            }

            return new Rectangle(
                origin.X + (cw - w) / 2,
                origin.Y + (ch - h) / 2,
                w,
                h);
        }

        /// <summary>4:3 menu area of the whole monitor the window is on.</summary>
        public static Rectangle GetMonitorMenuArea(IntPtr hwnd)
        {
            Rectangle m = Screen.FromHandle(hwnd).Bounds;

            int w, h;

            if (m.Width * 3 >= m.Height * 4)
            {
                h = m.Height;
                w = m.Height * 4 / 3;
            }
            else
            {
                w = m.Width;
                h = m.Width * 3 / 4;
            }

            return new Rectangle(m.X + (m.Width - w) / 2, m.Y + (m.Height - h) / 2, w, h);
        }

        public static Bitmap? CaptureMonitorMenuArea(IntPtr hwnd)
        {
            Rectangle area = GetMonitorMenuArea(hwnd);

            if (area.Width < 100 || area.Height < 100)
                return null;

            Bitmap bmp = new Bitmap(area.Width, area.Height);

            using (Graphics g = Graphics.FromImage(bmp))
                g.CopyFromScreen(area.Location, Point.Empty, area.Size);

            return bmp;
        }

        /// <summary>Window facts for the log (size, position, front or not).</summary>
        public static string DescribeWindow(IntPtr hwnd)
        {
            try
            {
                GetClientRect(hwnd, out RECT r);
                POINT o = new POINT();
                ClientToScreen(hwnd, ref o);

                IntPtr fg = GetForegroundWindow();
                GetWindowThreadProcessId(fg, out uint fgPid);
                GetWindowThreadProcessId(hwnd, out uint pid);

                return $"hwnd={hwnd} pid={pid} client={r.Right - r.Left}x{r.Bottom - r.Top}@{o.X},{o.Y} " +
                       $"menu={GetMenuArea(hwnd)} monitor={Screen.FromHandle(hwnd).Bounds} " +
                       $"iconic={IsIconic(hwnd)} visible={IsWindowVisible(hwnd)} front={(fg == hwnd ? "yes" : $"no (pid {fgPid})")}";
            }
            catch (Exception ex)
            {
                return "window info failed: " + ex.Message;
            }
        }

        public static Point ToScreen(IntPtr hwnd, double vx, double vy)
        {
            Rectangle area = GetMenuArea(hwnd);

            return new Point(
                area.X + (int)Math.Round(vx * area.Width / 640.0),
                area.Y + (int)Math.Round(vy * area.Height / 480.0));
        }

        public static PointF ToVirtual(IntPtr hwnd, Point screen)
        {
            Rectangle area = GetMenuArea(hwnd);

            if (area.Width == 0 || area.Height == 0)
                return PointF.Empty;

            return new PointF(
                (screen.X - area.X) * 640f / area.Width,
                (screen.Y - area.Y) * 480f / area.Height);
        }

        // ---------------------------------------------------------------
        // Mouse / keyboard
        // ---------------------------------------------------------------

        public static void Click(IntPtr hwnd, double vx, double vy)
        {
            Activate(hwnd);

            Point p = ToScreen(hwnd, vx, vy);
            ClickScreen(p);
        }

        public static void MoveCursor(IntPtr hwnd, double vx, double vy)
        {
            Point p = ToScreen(hwnd, vx, vy);
            SetCursorPos(p.X, p.Y);
        }

        public static void ClickScreen(Point p)
        {
            SetCursorPos(p.X, p.Y);
            Thread.Sleep(60);

            INPUT[] inputs =
            {
                new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } } },
                new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } } }
            };

            SendInput(1, new[] { inputs[0] }, Marshal.SizeOf<INPUT>());
            Thread.Sleep(50);
            SendInput(1, new[] { inputs[1] }, Marshal.SizeOf<INPUT>());
            Thread.Sleep(120);
        }

        /// <summary>
        /// Presses a key using hardware scan codes (games read these more
        /// reliably than SendKeys). Use Keys.M, Keys.Enter, Keys.Down ...
        /// </summary>
        public static void Key(IntPtr hwnd, Keys key, int repeat = 1)
        {
            Activate(hwnd);

            for (int i = 0; i < repeat; i++)
            {
                SendKey(key, false);
                Thread.Sleep(40);
                SendKey(key, true);
                Thread.Sleep(90);
            }
        }

        private const uint KEYEVENTF_UNICODE = 0x0004;

        /// <summary>Types text (e.g. a profile name) into the focused field.</summary>
        public static void TypeText(IntPtr hwnd, string text)
        {
            Activate(hwnd);

            foreach (char c in text)
            {
                foreach (bool up in new[] { false, true })
                {
                    INPUT input = new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        U = new InputUnion
                        {
                            ki = new KEYBDINPUT
                            {
                                wVk = 0,
                                wScan = c,
                                dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0)
                            }
                        }
                    };

                    SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
                    Thread.Sleep(25);
                }

                Thread.Sleep(30);
            }
        }

        private static void SendKey(Keys key, bool up)
        {
            ushort vk = (ushort)key;
            ushort scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);

            uint flags = KEYEVENTF_SCANCODE;

            if (up)
                flags |= KEYEVENTF_KEYUP;

            if (IsExtended(key))
                flags |= KEYEVENTF_EXTENDEDKEY;

            INPUT input = new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags }
                }
            };

            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        private static bool IsExtended(Keys key) =>
            key is Keys.Up or Keys.Down or Keys.Left or Keys.Right
                or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown
                or Keys.Insert or Keys.Delete;

        // ---------------------------------------------------------------
        // Screen capture (used by OCR)
        // ---------------------------------------------------------------

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        private const uint PW_CLIENTONLY = 0x1;
        private const uint PW_RENDERFULLCONTENT = 0x2; // includes DirectX content (Windows 8.1+)

        /// <summary>
        /// Captures the 4:3 menu area of the window.
        /// usePrintWindow = false: copy from the screen (window must be in front).
        /// usePrintWindow = true : ask Windows to render the window itself,
        ///                         which also works for many DirectX windows.
        /// </summary>
        public static Bitmap? CaptureMenuArea(IntPtr hwnd, bool usePrintWindow = false)
        {
            Rectangle area = GetMenuArea(hwnd);

            if (area.Width < 100 || area.Height < 100)
                return null;

            if (!usePrintWindow)
            {
                Bitmap bmp = new Bitmap(area.Width, area.Height);

                using (Graphics g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(area.Location, Point.Empty, area.Size);

                return bmp;
            }

            if (!GetClientRect(hwnd, out RECT r))
                return null;

            int cw = r.Right - r.Left;
            int ch = r.Bottom - r.Top;

            if (cw < 100 || ch < 100)
                return null;

            using Bitmap client = new Bitmap(cw, ch);

            using (Graphics g = Graphics.FromImage(client))
            {
                IntPtr hdc = g.GetHdc();

                try
                {
                    if (!PrintWindow(hwnd, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT))
                        return null;
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }

            // Crop the 4:3 menu area (client coordinates).
            POINT origin = new POINT { X = 0, Y = 0 };
            ClientToScreen(hwnd, ref origin);

            Rectangle crop = new Rectangle(
                area.X - origin.X,
                area.Y - origin.Y,
                area.Width,
                area.Height);

            crop.Intersect(new Rectangle(0, 0, cw, ch));

            if (crop.Width < 100 || crop.Height < 100)
                return null;

            return client.Clone(crop, client.PixelFormat);
        }
    }
}
