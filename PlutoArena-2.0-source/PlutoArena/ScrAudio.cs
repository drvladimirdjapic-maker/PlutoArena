using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace PlutoArena
{
    /// <summary>
    /// Mutes one process in the Windows volume mixer (Core Audio sessions),
    /// e.g. Pluto's StarCraft, while your StarCraft keeps its sound.
    ///
    /// Windows remembers the mixer setting per program (StarCraft.exe), so
    /// Pluto's sound is switched back on before Pluto closes, and your
    /// StarCraft is kept unmuted while the game runs.
    /// </summary>
    internal static class ScrAudio
    {
        /// <summary>Sets mute for every audio session of the process (all output devices).
        /// Returns the number of sessions changed.</summary>
        public static int SetMute(int pid, bool mute)
        {
            if (pid <= 0)
                return 0;

            int changed = 0;

            try
            {
                IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();

                if (enumerator.EnumAudioEndpoints(0 /* eRender */, 1 /* ACTIVE */, out IMMDeviceCollection devices) != 0)
                    return 0;

                devices.GetCount(out int deviceCount);

                for (int d = 0; d < deviceCount; d++)
                {
                    if (devices.Item(d, out IMMDevice device) != 0 || device == null)
                        continue;

                    Guid iid = typeof(IAudioSessionManager2).GUID;

                    if (device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out object managerObj) != 0)
                        continue;

                    IAudioSessionManager2 manager = (IAudioSessionManager2)managerObj;

                    if (manager.GetSessionEnumerator(out IAudioSessionEnumerator sessions) != 0)
                        continue;

                    sessions.GetCount(out int count);

                    for (int i = 0; i < count; i++)
                    {
                        if (sessions.GetSession(i, out IAudioSessionControl2 session) != 0 || session == null)
                            continue;

                        session.GetProcessId(out uint sessionPid);

                        if (sessionPid != (uint)pid)
                            continue;

                        if (session is ISimpleAudioVolume volume)
                        {
                            volume.GetMute(out bool current);

                            if (current != mute)
                            {
                                Guid context = Guid.Empty;
                                volume.SetMute(mute, ref context);
                                changed++;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Sound is not essential.
            }

            return changed;
        }

        /// <summary>
        /// Keeps Pluto muted and your StarCraft unmuted until <paramref name="stop"/>
        /// is set or Pluto exits. StarCraft opens its audio session a few seconds
        /// after start, so this checks repeatedly.
        /// </summary>
        public static Thread StartGuard(Func<int> plutoPid, Func<int> humanPid, CancellationToken stop)
        {
            Thread t = new Thread(() =>
            {
                bool logged = false;

                while (!stop.IsCancellationRequested)
                {
                    int pluto = plutoPid();

                    if (pluto > 0 && !IsAlive(pluto))
                        break; // Pluto closed - nothing left to do

                    if (pluto > 0 && SetMute(pluto, true) > 0 && !logged)
                    {
                        ArenaLauncher.Log("SOUND Pluto StarCraft muted");
                        logged = true;
                    }

                    int human = humanPid();

                    if (human > 0)
                        SetMute(human, false);

                    stop.WaitHandle.WaitOne(1000);
                }
            })
            {
                IsBackground = true,
                Name = "PlutoArena sound guard"
            };

            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
            return t;
        }

        private static bool IsAlive(int pid)
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

        // -----------------------------------------------------------------
        // Core Audio COM interfaces (only the methods used; order matters)
        // -----------------------------------------------------------------

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorCom
        {
        }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object instance);
            [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            // IAudioSessionManager
            [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr sessionControl);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr audioVolume);
            // IAudioSessionManager2
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);
            [PreserveSig] int RegisterSessionNotification(IntPtr notification);
            [PreserveSig] int UnregisterSessionNotification(IntPtr notification);
            [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr notification);
            [PreserveSig] int UnregisterDuckNotification(IntPtr notification);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
        }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            // IAudioSessionControl
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
            [PreserveSig] int GetGroupingParam(out Guid param);
            [PreserveSig] int SetGroupingParam(ref Guid param, ref Guid context);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
            // IAudioSessionControl2
            [PreserveSig] int GetSessionIdentifier(out IntPtr id);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
            [PreserveSig] int GetProcessId(out uint pid);
            [PreserveSig] int IsSystemSoundsSession();
            [PreserveSig] int SetDuckingPreference(bool optOut);
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float level, ref Guid context);
            [PreserveSig] int GetMasterVolume(out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }
    }
}
