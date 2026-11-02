using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace UnityBatchBuild
{
    // Windows 10+: the launcher owns the only job handle. Closing its console or
    // killing PowerShell closes that handle even when PowerShell finally cannot run.
    // Neither the main Editor nor any pre-existing process is added to this job.
    public sealed class OwnedProcess : IDisposable
    {
        private SafeFileHandle job;
        private SafeFileHandle process;
        public int Id { get; private set; }

        public static OwnedProcess Start(string executable, string[] arguments, string directory)
        {
            var owned = new OwnedProcess();
            IntPtr attributes = IntPtr.Zero;
            IntPtr jobList = IntPtr.Zero;
            bool initialized = false;
            try
            {
                owned.job = CreateJobObject(IntPtr.Zero, null);
                if (owned.job.IsInvalid) throw Error("CreateJobObject");
                var limits = new ExtendedLimits();
                limits.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                if (!SetInformationJobObject(owned.job, 9, ref limits,
                    (uint)Marshal.SizeOf(typeof(ExtendedLimits))))
                    throw Error("SetInformationJobObject");

                IntPtr size = IntPtr.Zero;
                InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                if (size == IntPtr.Zero) throw Error("Size process attributes");
                attributes = Marshal.AllocHGlobal(size);
                if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size))
                    throw Error("InitializeProcThreadAttributeList");
                initialized = true;
                jobList = Marshal.AllocHGlobal(IntPtr.Size);
                Marshal.WriteIntPtr(jobList, owned.job.DangerousGetHandle());
                // Assign atomically at creation, not after Start-Process: even an
                // immediate launcher crash must not leave an unowned child behind.
                if (!UpdateProcThreadAttribute(attributes, 0, new IntPtr(0x2000d),
                    jobList, new IntPtr(IntPtr.Size), IntPtr.Zero, IntPtr.Zero))
                    throw Error("Set process job list (requires Windows 10 or newer)");

                var startup = new StartupInfoEx();
                startup.Info.Size = Marshal.SizeOf(typeof(StartupInfoEx));
                startup.Info.Flags = 1; // STARTF_USESHOWWINDOW, SW_HIDE
                startup.Attributes = attributes;
                var command = new StringBuilder(Quote(executable));
                foreach (string argument in arguments) command.Append(' ').Append(Quote(argument));
                ProcessInfo info;
                // EXTENDED_STARTUPINFO_PRESENT | CREATE_NO_WINDOW. Inherit the
                // launcher's environment (including Android TEMP), but no handles.
                if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero,
                    false, 0x08080000, IntPtr.Zero, directory, ref startup, out info))
                    throw Error("Create build process");
                owned.process = new SafeFileHandle(info.Process, true);
                CloseHandle(info.Thread);
                owned.Id = unchecked((int)info.ProcessId);
                return owned;
            }
            catch
            {
                owned.Dispose();
                throw;
            }
            finally
            {
                if (initialized) DeleteProcThreadAttributeList(attributes);
                if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
                if (jobList != IntPtr.Zero) Marshal.FreeHGlobal(jobList);
                GC.KeepAlive(owned);
            }
        }

        public bool WaitForExit(int milliseconds)
        {
            if (milliseconds < 0) throw new ArgumentOutOfRangeException("milliseconds");
            uint result = WaitForSingleObject(process, (uint)milliseconds);
            if (result == 0xffffffff) throw Error("WaitForSingleObject");
            return result == 0;
        }

        public int ExitCode
        {
            get
            {
                if (!WaitForExit(0)) throw new InvalidOperationException("Build process is still running.");
                uint code;
                if (!GetExitCodeProcess(process, out code)) throw Error("GetExitCodeProcess");
                return unchecked((int)code);
            }
        }

        public void Dispose()
        {
            // Also clean up surviving children after Unity itself has exited.
            if (job != null) job.Dispose();
            if (process != null) process.Dispose();
        }

        private static Win32Exception Error(string operation)
        {
            int code = Marshal.GetLastWin32Error();
            return new Win32Exception(code, operation + ": " + new Win32Exception(code).Message);
        }

        private static string Quote(string argument)
        {
            if (argument == null || argument.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
                throw new ArgumentException("Process arguments must not contain NUL or newlines.");
            var value = new StringBuilder("\"");
            int slashes = 0;
            foreach (char character in argument)
            {
                if (character == '\\') { slashes++; continue; }
                value.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
                value.Append(character);
                slashes = 0;
            }
            return value.Append('\\', slashes * 2).Append('"').ToString();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long ProcessTime, JobTime;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperations, WriteOperations, OtherOperations;
            public ulong ReadBytes, WriteBytes, OtherBytes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits
        {
            public BasicLimits Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            public int Size;
            public string Reserved, Desktop, Title;
            public uint X, Y, XSize, YSize, XChars, YChars, FillAttribute, Flags;
            public ushort ShowWindow, ReservedBytes;
            public IntPtr ReservedPointer, StdInput, StdOutput, StdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StartupInfoEx
        {
            public StartupInfo Info;
            public IntPtr Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInfo
        {
            public IntPtr Process, Thread;
            public uint ProcessId, ThreadId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass,
            ref ExtendedLimits info, uint length);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute,
            IntPtr value, IntPtr size, IntPtr previous, IntPtr returnedSize);
        [DllImport("kernel32.dll")]
        private static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcess(string application, StringBuilder command,
            IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags,
            IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInfo info);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(SafeFileHandle handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(SafeFileHandle process, out uint code);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
