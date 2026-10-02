using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AgentCapture;

// Close-on-exit also bounds worker lifetime if an Agent kills the supervisor abruptly.
internal sealed class WorkerJob : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    {
        public long ProcessTime, JobTime; public uint Flags; public nuint MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcessLimit; public nuint Affinity; public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters
    { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    { public BasicLimits Basic; public IoCounters Io; public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(nint attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(SafeFileHandle job, int info, in ExtendedLimits limits, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, nint process);
    private readonly SafeFileHandle handle;

    public WorkerJob()
    {
        handle = CreateJobObject(0, null);
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
        if (handle.IsInvalid || !SetInformationJobObject(handle, 9, limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        { handle.Dispose(); throw new CaptureError("worker_isolation_failed", "Cannot create the capture worker job."); }
    }
    public void Assign(Process process)
    {
        if (AssignProcessToJobObject(handle, process.Handle)) return;
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        throw new CaptureError("worker_isolation_failed", "Cannot assign the worker to its lifetime job.");
    }
    public void Dispose() => handle.Dispose();
}
