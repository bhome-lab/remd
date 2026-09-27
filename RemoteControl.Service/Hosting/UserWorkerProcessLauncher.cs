using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace RemoteControl.Service.Hosting;

/// <summary>Launches the same executable on the interactive user's desktop and environment.</summary>
public sealed class UserWorkerProcessLauncher : IWorkerProcessLauncher
{
    public int? GetActiveSessionId()
    {
        if (Environment.UserInteractive)
        {
            using var current = Process.GetCurrentProcess();
            return current.SessionId;
        }

        var session = Native.WTSGetActiveConsoleSessionId();
        return session == uint.MaxValue ? null : checked((int)session);
    }

    public IWorkerProcess Start(int sessionId, string pipeName)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("process_path_not_found");
        var entry = Assembly.GetEntryAssembly()?.Location ?? throw new InvalidOperationException("entry_assembly_not_found");
        var usesDotnetHost = string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase);
        var arguments = new List<string>();
        if (usesDotnetHost) arguments.Add(entry);
        arguments.AddRange(["--desktop-worker", "--pipe", pipeName]);

        if (Environment.UserInteractive)
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            return new WorkerProcess(Process.Start(startInfo) ?? throw new InvalidOperationException("desktop_worker_start_failed"));
        }

        if (!Native.WTSQueryUserToken(checked((uint)sessionId), out var userToken))
            throw Native.Error("interactive_session_unavailable");
        try
        {
            if (!Native.DuplicateTokenEx(userToken, Native.TokenAllAccess, IntPtr.Zero, 2, 1, out var primaryToken))
                throw Native.Error("duplicate_user_token_failed");
            try
            {
                if (!Native.CreateEnvironmentBlock(out var environment, primaryToken, false))
                    throw Native.Error("user_environment_block_failed");
                try
                {
                    var command = new StringBuilder(string.Join(" ", new[] { executable }.Concat(arguments).Select(QuoteArgument)));
                    var startup = new Native.StartupInfo
                    {
                        Size = Marshal.SizeOf<Native.StartupInfo>(),
                        Desktop = "winsta0\\default"
                    };
                    if (!Native.CreateProcessAsUser(primaryToken, executable, command, IntPtr.Zero, IntPtr.Zero,
                            false, Native.CreateUnicodeEnvironment | Native.CreateNoWindow, environment,
                            AppContext.BaseDirectory, ref startup, out var info))
                        throw Native.Error("desktop_worker_create_process_failed");

                    try { return new WorkerProcess(Process.GetProcessById(checked((int)info.ProcessId))); }
                    finally
                    {
                        Native.CloseHandle(info.Thread);
                        Native.CloseHandle(info.Process);
                    }
                }
                finally { Native.DestroyEnvironmentBlock(environment); }
            }
            finally { Native.CloseHandle(primaryToken); }
        }
        finally { Native.CloseHandle(userToken); }
    }

    // Quotes Windows argv values including a path whose last character is a backslash.
    private static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        result.Append('\\', slashes * 2);
        return result.Append('"').ToString();
    }

    private sealed class WorkerProcess(Process process) : IWorkerProcess
    {
        public bool HasExited => process.HasExited;
        public void Terminate()
        {
            if (process.HasExited) return;
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        public void Dispose() => process.Dispose();
    }

    private static class Native
    {
        public const uint TokenAllAccess = 0xF01FF;
        public const uint CreateUnicodeEnvironment = 0x00000400;
        public const uint CreateNoWindow = 0x08000000;
        public static InvalidOperationException Error(string name) => new($"{name}:{Marshal.GetLastWin32Error()}");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct StartupInfo
        {
            public int Size;
            public string? Reserved;
            public string? Desktop;
            public string? Title;
            public int X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
            public short ShowWindow, ReservedSize;
            public IntPtr Reserved2, StdInput, StdOutput, StdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessInformation
        {
            public IntPtr Process, Thread;
            public uint ProcessId, ThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] public static extern uint WTSGetActiveConsoleSessionId();
        [DllImport("wtsapi32.dll", SetLastError = true)] public static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] public static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attributes, int impersonationLevel, int tokenType, out IntPtr primaryToken);
        [DllImport("userenv.dll", SetLastError = true)] public static extern bool CreateEnvironmentBlock(out IntPtr environment, IntPtr token, bool inherit);
        [DllImport("userenv.dll", SetLastError = true)] public static extern bool DestroyEnvironmentBlock(IntPtr environment);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CreateProcessAsUser(IntPtr token, string applicationName, StringBuilder commandLine, IntPtr processAttributes,
            IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory,
            ref StartupInfo startupInfo, out ProcessInformation processInformation);
    }
}
