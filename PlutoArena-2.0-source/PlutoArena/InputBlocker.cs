using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PlutoArena
{
    /// <summary>
    /// Temporarily ignores the real mouse and keyboard while Pluto Arena
    /// clicks through the StarCraft menus, so a bumped mouse cannot break
    /// the automation.
    ///
    /// Works with low-level hooks: input that Pluto Arena itself sends is
    /// marked "injected" by Windows and passes; real input is dropped.
    /// Esc cancels the automation and unlocks immediately. Ctrl+Alt+Del is
    /// always handled by Windows. No administrator rights are needed.
    /// </summary>
    internal sealed class InputBlocker : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const uint LLKHF_INJECTED = 0x10;
        private const uint LLMHF_INJECTED = 0x01;
        private const int VK_ESCAPE = 0x1B;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public int x, y;
            public uint mouseData, flags, time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr hMod, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? name);

        // Keep the delegates alive - the GC must not collect them while hooked.
        private readonly HookProc keyboardProc;
        private readonly HookProc mouseProc;

        private IntPtr keyboardHook;
        private IntPtr mouseHook;
        private Thread? thread;
        private ApplicationContext? context;
        private readonly Stopwatch age = Stopwatch.StartNew();
        private volatile bool active;

        /// <summary>Raised (on the hook thread) when the user presses Esc.</summary>
        public event Action? EscapePressed;

        /// <summary>Safety limit: the lock opens by itself after this time.</summary>
        public TimeSpan MaxDuration { get; set; } = TimeSpan.FromMinutes(3);

        public InputBlocker()
        {
            keyboardProc = KeyboardHook;
            mouseProc = MouseHook;
        }

        public void Start()
        {
            if (thread != null)
                return;

            using ManualResetEventSlim ready = new ManualResetEventSlim();

            // Low-level hooks are called on the thread that installed them,
            // which therefore needs its own message loop.
            thread = new Thread(() =>
            {
                IntPtr module = GetModuleHandle(null);
                keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, module, 0);
                mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, module, 0);

                active = keyboardHook != IntPtr.Zero || mouseHook != IntPtr.Zero;
                ArenaLauncher.Log($"INPUT LOCK on (keyboard={keyboardHook != IntPtr.Zero}, mouse={mouseHook != IntPtr.Zero})");

                context = new ApplicationContext();
                ready.Set();
                Application.Run(context);

                if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook);
                if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
                keyboardHook = mouseHook = IntPtr.Zero;
                active = false;
            })
            {
                IsBackground = true,
                Name = "PlutoArena input lock"
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait(2000);
        }

        public void Stop()
        {
            if (thread == null)
                return;

            active = false;

            try
            {
                context?.ExitThread();
            }
            catch
            {
            }

            thread.Join(2000);
            thread = null;

            ArenaLauncher.Log("INPUT LOCK off");
        }

        public void Dispose() => Stop();

        private bool Expired => age.Elapsed > MaxDuration;

        private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && active && !Expired)
            {
                KBDLLHOOKSTRUCT info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                if ((info.flags & LLKHF_INJECTED) == 0)
                {
                    if (info.vkCode == VK_ESCAPE && ((int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN))
                    {
                        active = false; // unlock right away
                        ThreadPool.QueueUserWorkItem(_ => EscapePressed?.Invoke());
                    }

                    return (IntPtr)1; // swallow the real key
                }
            }

            return CallNextHookEx(keyboardHook, nCode, wParam, lParam);
        }

        private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && active && !Expired)
            {
                MSLLHOOKSTRUCT info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                if ((info.flags & LLMHF_INJECTED) == 0)
                    return (IntPtr)1; // swallow real mouse movement / clicks
            }

            return CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }
    }
}
