namespace RemoteControl.Infrastructure.Transport;

public sealed class WorkerDispatcher(
    IInputService input,
    IScreenService screen,
    IExecutionService execution,
    IEnvironmentService environments,
    ILuaService lua)
{
    public async Task<WorkerResponse> DispatchAsync(WorkerRequest request, CancellationToken cancellationToken)
    {
        var a = request.Args;
        return request.Op switch
        {
            "ping" => new(true),
            "input_status" => new(true, InputStatus: await input.GetStatusAsync(cancellationToken)),
            "desktop_status" => new(true, Desktop: await screen.GetStatusAsync(cancellationToken)),
            "shell_status" => new(true, Shell: await execution.GetStatusAsync(cancellationToken)),
            "windows" => new(true, Windows: await screen.GetWindowsAsync(cancellationToken)),
            "capture" => new(true, Capture: await screen.CaptureAsync(a.Capture ?? new(), cancellationToken)),
            "click" => new(true, Input: await input.ClickAsync(a.X, a.Y, cancellationToken)),
            "double_click" => new(true, Input: await input.DoubleClickAsync(a.X, a.Y, cancellationToken)),
            "type" => new(true, Input: await input.TypeAsync(a.Text ?? "", cancellationToken)),
            "press" => new(true, Input: await input.PressAsync(a.Key ?? default, cancellationToken)),
            "hotkey" => new(true, Input: await input.HotkeyAsync(a.Keys ?? default, cancellationToken)),
            "scroll" => new(true, Input: await input.ScrollAsync(a.Delta, cancellationToken)),
            "drag" => new(true, Input: await input.DragAsync(a.X1, a.Y1, a.X2, a.Y2, cancellationToken)),
            "sequence" => new(true, Input: await input.SequenceAsync(a.Actions ?? [], cancellationToken)),
            "raw_mouse" => new(true, Input: await input.RawMouseAsync(a.Report ?? [], cancellationToken)),
            "raw_keyboard" => new(true, Input: await input.RawKeyboardAsync(a.Report ?? [], cancellationToken)),
            "release_all" => new(true, Input: await input.ReleaseAllAsync(cancellationToken)),
            "run" => new(true, Execution: await execution.RunAsync(a.Runtime ?? "", a.Code ?? "", a.Environment, a.Session, a.TimeoutMs, cancellationToken)),
            "close_session" => new(true, Closed: await execution.CloseSessionAsync(a.Runtime ?? "", a.Session ?? "", cancellationToken)),
            "environment" => new(true, Environment: await environments.EnsureAsync(a.Runtime ?? "", a.Name ?? "", a.Packages ?? [], cancellationToken)),
            "environments" => new(true, Environments: await environments.ListAsync(cancellationToken)),
            "lua" => new(true, Lua: await lua.RunAsync(a.Code ?? "", a.Session, cancellationToken)),
            _ => new(false, Error: "unknown_worker_operation")
        };
    }
}
