using System.Text.Json.Serialization;

namespace RemoteControl.Infrastructure.Transport;

public sealed record WorkerRequest(string Op, WorkerArgs Args);

public sealed record WorkerArgs(
    int X = 0, int Y = 0, int X1 = 0, int Y1 = 0, int X2 = 0, int Y2 = 0,
    int Delta = 0, string? Text = null, KeyInput? Key = null, KeyChord? Keys = null,
    byte[]? Report = null, CaptureRequest? Capture = null,
    string? Runtime = null, string? Code = null, string? Environment = null,
    string? Session = null, string? Name = null, int TimeoutMs = 30000,
    string[]? Packages = null, InputAction[]? Actions = null,
    int? HoldTimeoutMs = null, bool ResetBefore = false, bool ResetAfter = false);

public sealed record WorkerResponse(
    bool Ok, string? Error = null,
    InputResult? Input = null, InputStatus? InputStatus = null,
    DesktopStatus? Desktop = null, CaptureResult? Capture = null,
    WindowInfo[]? Windows = null, ExecutionResult? Execution = null,
    EnvironmentResult? Environment = null, LuaResult? Lua = null,
    ShellStatus? Shell = null, bool? Closed = null, string[]? Environments = null);

public interface IWorkerEndpoint
{
    string PipeName { get; }
    WorkerStatus Status { get; }
}

public interface IWorkerConnection
{
    Task<WorkerResponse> CallAsync(WorkerRequest request, CancellationToken cancellationToken);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(WorkerRequest))]
[JsonSerializable(typeof(WorkerResponse))]
public partial class WorkerJsonContext : JsonSerializerContext;
