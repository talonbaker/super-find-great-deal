using System;
using System.Runtime.InteropServices;

namespace MpFoundation.Net.Hosting;

/// <summary>
/// Windows job object with KILL_ON_JOB_CLOSE: the hosting client's spawned match server is
/// assigned to the job, so if the client process dies for any reason — including a hard
/// kill — the OS reaps the child. No orphaned Godot processes, by construction. On
/// non-Windows this is a no-op; there the session boundary provides the same guarantee.
///
/// Relocated from the retired matchmaking service (matchmaking/Provisioning/) — the
/// orphan-prevention guarantee it provided the multi-tenant provisioner matters just as
/// much for the client-local spawn that replaced it.
/// </summary>
public sealed class WindowsJobObject : IDisposable
{
    private nint _handle;

    public bool IsActive => _handle != 0;

    public WindowsJobObject()
    {
        if (!OperatingSystem.IsWindows())
            return;

        _handle = CreateJobObject(0, null);
        if (_handle == 0)
            return;

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE },
        };
        int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        nint ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, ptr, (uint)size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>Best-effort: assigns a child process to the job. Safe to call on any OS.</summary>
    public void Assign(System.Diagnostics.Process process)
    {
        if (!OperatingSystem.IsWindows() || _handle == 0)
            return;
        try
        {
            AssignProcessToJobObject(_handle, process.Handle);
        }
        catch
        {
            // Assignment can fail if the process already exited; the exit event handles cleanup.
        }
    }

    public void Dispose()
    {
        // Idempotent: a double-Dispose must not close a recycled handle value.
        if (OperatingSystem.IsWindows() && _handle != 0)
        {
            CloseHandle(_handle); // KILL_ON_JOB_CLOSE reaps any remaining children here
            _handle = 0;
        }
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private const int JobObjectExtendedLimitInformation = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateJobObject(nint securityAttributes, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(nint job, int infoClass, nint info, uint infoLength);

    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
}
