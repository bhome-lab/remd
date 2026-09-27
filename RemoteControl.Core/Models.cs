namespace RemoteControl.Core;

public sealed record ExecutionResult(bool Ok, string Runtime, string? Session, string Stdout, string Stderr, int ExitCode, long DurationMs, string? Error = null);
public sealed record EnvironmentResult(bool Ok, string Runtime, string Name, string Path, string[] Packages, string? Error = null);
public sealed record LuaResult(bool Ok, string Output, string? Error = null);
public sealed record InputResult(bool Ok, string Backend, string? Error = null);
public sealed record InputAction(string Op, int X = 0, int Y = 0, int X2 = 0, int Y2 = 0, int Delta = 0, string? Text = null, KeyInput? Key = null, KeyChord? Keys = null, int? HoldTimeoutMs = null);
public sealed record DriverStatus(bool Connected, string Wrapper, bool Device, bool Signed, string? Error = null);
public sealed record InputStatus(string Backend, DriverStatus Driver);
public sealed record WindowInfo(long Handle, string Title);
public sealed record ShellStatus(Dictionary<string, bool> Runtimes, string[] Environments);
public sealed record DesktopStatus(bool Interactive, string Worker, string Backend);
public sealed record WorkerStatus(string State, string? Error = null);
public sealed record ComputerStatus(string Service, string Runtime, DesktopStatus Desktop, WorkerStatus Worker, InputStatus Input, Dictionary<string, bool> Runtimes, string[] Environments);
public enum CaptureKind { Primary, Region, Monitor, Window }
public sealed record CaptureRequest(
    CaptureKind Kind = CaptureKind.Primary, int X = 0, int Y = 0, int Width = 0, int Height = 0,
    int Monitor = 0, long WindowId = 0, string Format = "jpeg", int? Quality = null,
    int? MaxWidth = null, int? MaxHeight = null, int? MaxBytes = null);
public sealed record CaptureResult(bool Ok, byte[]? Data = null, string MimeType = "image/jpeg", string? Error = null);
