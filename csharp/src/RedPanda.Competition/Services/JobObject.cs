using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RedPanda.Competition.Services;

/// <summary>
/// A Windows job object used to enforce a per-program memory ceiling and to
/// make sure grandchildren do not survive a timed-out test run.
/// </summary>
internal sealed class JobObject : IDisposable
{
    private const uint JobObjectLimitProcessMemory = 0x00000100;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const int JobObjectExtendedLimitInformationClass = 9;

    private IntPtr _handle;

    public JobObject(long memoryLimitBytes)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        var info = new JobObjectExtendedLimitInformation();
        info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

        if (memoryLimitBytes > 0)
        {
            info.BasicLimitInformation.LimitFlags |= JobObjectLimitProcessMemory;
            info.ProcessMemoryLimit = (UIntPtr)(ulong)memoryLimitBytes;
        }

        SetInformationJobObject(
            _handle,
            JobObjectExtendedLimitInformationClass,
            ref info,
            (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>());
    }

    public bool Assign(Process process)
    {
        if (_handle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return AssignProcessToJobObject(_handle, process.Handle);
        }
        catch
        {
            return false;
        }
    }

    public long PeakProcessMemoryBytes
    {
        get
        {
            if (_handle == IntPtr.Zero)
            {
                return 0;
            }

            if (!QueryInformationJobObject(
                    _handle,
                    JobObjectExtendedLimitInformationClass,
                    out var info,
                    (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>(),
                    out _))
            {
                return 0;
            }

            return (long)info.PeakProcessMemoryUsed;
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        CloseHandle(_handle);
        _handle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        IntPtr job,
        int informationClass,
        ref JobObjectExtendedLimitInformation information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(
        IntPtr job,
        int informationClass,
        out JobObjectExtendedLimitInformation information,
        uint informationLength,
        out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
