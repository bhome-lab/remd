using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RemoteControl.Infrastructure.Execution;

/// <summary>Owns process lifetime, including children that inherit redirected streams.</summary>
internal sealed class OwnedProcess : IDisposable
{
    private SafeFileHandle? _job;
    private bool _detached;
    public Process Process { get; }

    private OwnedProcess(Process process) => Process = process;

    public static OwnedProcess Start(ProcessStartInfo startInfo)
    {
        var owner = new OwnedProcess(Process.Start(startInfo) ?? throw new InvalidOperationException("process_start_failed"));
        try
        {
            if (OperatingSystem.IsWindows())
            {
                owner._job = CreateJobObject(IntPtr.Zero, null);
                if (owner._job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                owner.SetKillOnClose(true);
                if (!AssignProcessToJobObject(owner._job, owner.Process.Handle) && !owner.Process.HasExited)
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            return owner;
        }
        catch { owner.Dispose(); throw; }
    }

    public void Stop()
    {
        if (_job is not null) _job.Dispose();
        try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    // A successful one-shot command may intentionally have started a background app.
    public void DetachChildren()
    {
        if (_job is { IsClosed: false }) SetKillOnClose(false);
        _detached = true;
    }

    public void Dispose()
    {
        if (!_detached)
        {
            Stop();
            try { Process.WaitForExit(); }
            catch (InvalidOperationException) { }
        }
        _job?.Dispose();
        Process.Dispose();
    }

    private void SetKillOnClose(bool enabled)
    {
        var info = new ExtendedLimitInformation();
        info.BasicLimitInformation.LimitFlags = enabled ? 0x2000u : 0u;
        if (!SetInformationJobObject(_job!, 9, ref info, (uint)Marshal.SizeOf<ExtendedLimitInformation>()))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, ref ExtendedLimitInformation info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
