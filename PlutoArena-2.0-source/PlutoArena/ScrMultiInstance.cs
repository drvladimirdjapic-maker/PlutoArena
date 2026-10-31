using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text;

namespace PlutoArena
{
    internal static class ScrMultiInstance
    {
        const uint PROCESS_DUP_HANDLE = 0x0040;
        const uint PROCESS_QUERY_INFORMATION = 0x0400;
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        const uint DUPLICATE_SAME_ACCESS = 0x00000002;
        const uint DUPLICATE_CLOSE_SOURCE = 0x00000001;

        const int ProcessHandleInformation = 0x33;
        const int ObjectNameInformation = 1;

        const int STATUS_INFO_LENGTH_MISMATCH =
            unchecked((int)0xC0000004);

        const int STATUS_BUFFER_OVERFLOW =
            unchecked((int)0x80000005);

        const int STATUS_BUFFER_TOO_SMALL =
            unchecked((int)0xC0000023);

        const uint CREATE_NEW_CONSOLE = 0x00000010;
        const uint CREATE_NO_WINDOW = 0x08000000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFO
        {
            public uint cb;
            public string? lpReserved;
            public string? lpDesktop;
            public string? lpTitle;
            public uint dwX;
            public uint dwY;
            public uint dwXSize;
            public uint dwYSize;
            public uint dwXCountChars;
            public uint dwYCountChars;
            public uint dwFillAttribute;
            public uint dwFlags;
            public ushort wShowWindow;
            public ushort cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public uint dwProcessId;
            public uint dwThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct UNICODE_STRING
        {
            public ushort Length;
            public ushort MaximumLength;
            public IntPtr Buffer;
        }

        [DllImport(
    "kernel32.dll",
    SetLastError = true,
    CharSet = CharSet.Unicode)]
static extern bool CreateProcessW(
    string? lpApplicationName,
    StringBuilder lpCommandLine,
    IntPtr lpProcessAttributes,
    IntPtr lpThreadAttributes,
    bool bInheritHandles,
    uint dwCreationFlags,
    IntPtr lpEnvironment,
    string? lpCurrentDirectory,
    ref STARTUPINFO lpStartupInfo,
    out PROCESS_INFORMATION lpProcessInformation
);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(
            uint dwDesiredAccess,
            bool bInheritHandle,
            int dwProcessId
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DuplicateHandle(
            IntPtr hSourceProcessHandle,
            IntPtr hSourceHandle,
            IntPtr hTargetProcessHandle,
            out IntPtr lpTargetHandle,
            uint dwDesiredAccess,
            bool bInheritHandle,
            uint dwOptions
        );

        [DllImport("ntdll.dll")]
        static extern int NtQueryInformationProcess(
            IntPtr ProcessHandle,
            int ProcessInformationClass,
            IntPtr ProcessInformation,
            uint ProcessInformationLength,
            out uint ReturnLength
        );

        [DllImport("ntdll.dll")]
        static extern int NtQueryObject(
            IntPtr Handle,
            int ObjectInformationClass,
            IntPtr ObjectInformation,
            uint ObjectInformationLength,
            out uint ReturnLength
        );

        public static bool LaunchSecondInstance(
    string starCraftPath,
    out string error,
    out int newPid)
        {
            error = "";
            newPid = 0;

            STARTUPINFO si = new STARTUPINFO();
            si.cb = (uint)Marshal.SizeOf<STARTUPINFO>();

            StringBuilder commandLine =
    new StringBuilder($"\"{starCraftPath}\" -launch");

            bool created = CreateProcessW(
    starCraftPath,
    commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CREATE_NEW_CONSOLE | CREATE_NO_WINDOW,
                IntPtr.Zero,
                null,
                ref si,
                out PROCESS_INFORMATION pi
            );

            if (!created)
{
    error =
        $"CreateProcessW failed. Win32 error: " +
        Marshal.GetLastWin32Error();

    return false;
}

newPid = (int)pi.dwProcessId;

            if (pi.hThread != IntPtr.Zero)
                CloseHandle(pi.hThread);

            bool unlocked = false;

            try
            {
                for (int attempt = 1;
                     attempt <= 40;
                     attempt++)
                {
                    Thread.Sleep(100);

                    if (TryCloseSingleInstanceHandle(
                        (int)pi.dwProcessId))
                    {
                        unlocked = true;
                        break;
                    }
                }
            }
            finally
            {
                if (pi.hProcess != IntPtr.Zero)
                    CloseHandle(pi.hProcess);
            }

            if (!unlocked)
            {
                error =
                    "Second StarCraft instance started, " +
                    "but the single-instance handle " +
                    "was not found.";

                return false;
            }

            return true;
        }
public static bool UnlockExistingInstance(int pid)
{
    return TryCloseSingleInstanceHandle(pid);
}
        static bool TryCloseSingleInstanceHandle(int pid)
        {
            IntPtr processHandle = OpenProcess(
                PROCESS_DUP_HANDLE |
                PROCESS_QUERY_INFORMATION |
                PROCESS_QUERY_LIMITED_INFORMATION,
                false,
                pid
            );

            if (processHandle == IntPtr.Zero)
                return false;

            try
            {
                uint bufferSize = 0x10000;
                IntPtr buffer = IntPtr.Zero;

                try
                {
                    while (true)
                    {
                        buffer =
                            Marshal.AllocHGlobal(
                                (int)bufferSize);

                        int status =
                            NtQueryInformationProcess(
                                processHandle,
                                ProcessHandleInformation,
                                buffer,
                                bufferSize,
                                out uint requiredSize
                            );

                        if (status == 0)
                            break;

                        Marshal.FreeHGlobal(buffer);
                        buffer = IntPtr.Zero;

                        if (
                            status ==
                                STATUS_INFO_LENGTH_MISMATCH ||
                            status ==
                                STATUS_BUFFER_TOO_SMALL ||
                            status ==
                                STATUS_BUFFER_OVERFLOW)
                        {
                            bufferSize = Math.Max(
                                requiredSize + 0x1000,
                                bufferSize * 2
                            );

                            continue;
                        }

                        return false;
                    }

                    ulong handleCount =
                        IntPtr.Size == 8
                        ? (ulong)Marshal.ReadInt64(buffer)
                        : (uint)Marshal.ReadInt32(buffer);

                    int headerSize = IntPtr.Size * 2;
                    int entrySize =
                        IntPtr.Size == 8 ? 40 : 28;

                    for (
                        ulong i = 0;
                        i < handleCount;
                        i++)
                    {
                        IntPtr entry = IntPtr.Add(
                            buffer,
                            headerSize +
                            checked(
                                (int)(
                                    i *
                                    (ulong)entrySize))
                        );

                        IntPtr remoteHandle =
                            IntPtr.Size == 8
                            ? new IntPtr(
                                Marshal.ReadInt64(entry))
                            : new IntPtr(
                                Marshal.ReadInt32(entry));

                        if (!DuplicateHandle(
                            processHandle,
                            remoteHandle,
                            GetCurrentProcess(),
                            out IntPtr localHandle,
                            0,
                            false,
                            DUPLICATE_SAME_ACCESS))
                        {
                            continue;
                        }

                        try
                        {
                            string? name =
                                GetObjectName(localHandle);

                            if (string.IsNullOrEmpty(name))
                                continue;

                            if (!name.Contains(
                                "Starcraft Check For Other Instances",
                                StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            bool closed =
                                DuplicateHandle(
                                    processHandle,
                                    remoteHandle,
                                    GetCurrentProcess(),
                                    out IntPtr tempHandle,
                                    0,
                                    false,
                                    DUPLICATE_CLOSE_SOURCE
                                );

                            if (tempHandle != IntPtr.Zero)
                                CloseHandle(tempHandle);

                            return closed;
                        }
                        finally
                        {
                            CloseHandle(localHandle);
                        }
                    }

                    return false;
                }
                finally
                {
                    if (buffer != IntPtr.Zero)
                        Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(processHandle);
            }
        }

        static string? GetObjectName(IntPtr handle)
        {
            uint size = 0x400;
            IntPtr buffer = IntPtr.Zero;

            try
            {
                while (true)
                {
                    buffer =
                        Marshal.AllocHGlobal((int)size);

                    int status = NtQueryObject(
                        handle,
                        ObjectNameInformation,
                        buffer,
                        size,
                        out uint requiredSize
                    );

                    if (status == 0)
                        break;

                    Marshal.FreeHGlobal(buffer);
                    buffer = IntPtr.Zero;

                    if (
                        status ==
                            STATUS_INFO_LENGTH_MISMATCH ||
                        status ==
                            STATUS_BUFFER_OVERFLOW ||
                        status ==
                            STATUS_BUFFER_TOO_SMALL)
                    {
                        size = Math.Max(
                            requiredSize + 0x100,
                            size * 2
                        );

                        continue;
                    }

                    return null;
                }

                UNICODE_STRING str =
                    Marshal.PtrToStructure<UNICODE_STRING>(
                        buffer);

                if (
                    str.Buffer == IntPtr.Zero ||
                    str.Length == 0)
                {
                    return null;
                }

                return Marshal.PtrToStringUni(
                    str.Buffer,
                    str.Length / 2
                );
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(buffer);
            }
        }
    }
}