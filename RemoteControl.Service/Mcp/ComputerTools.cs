using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RemoteControl.Core;
using RemoteControl.Infrastructure.Transport;

namespace RemoteControl.Service.Mcp;

[McpServerToolType]
public sealed class ComputerTools(
    IInputService input,
    IScreenService screen,
    IExecutionService execution,
    IEnvironmentService environments,
    ILuaService lua,
    IWorkerEndpoint worker)
{
    [McpServerTool(Name = "computer.status"), Description("Return local runtime, desktop and input status.")]
    public async Task<ComputerStatus> Status(CancellationToken cancellationToken)
    {
        var shell = await execution.GetStatusAsync(cancellationToken);
        var desktop = await screen.GetStatusAsync(cancellationToken);
        var inputStatus = await input.GetStatusAsync(cancellationToken);
        return new("running", ".NET 10", desktop, worker.Status, inputStatus, shell.Runtimes, shell.Environments);
    }

    [McpServerTool(Name = "computer.screenshot"), Description("Capture the local primary desktop and return an MCP image.")]
    public Task<IEnumerable<ContentBlock>> Screenshot(CancellationToken cancellationToken) =>
        CaptureAsync(new(), cancellationToken);

    [McpServerTool(Name = "computer.screenshot_region"), Description("Capture a local desktop region and return an MCP image.")]
    public Task<IEnumerable<ContentBlock>> ScreenshotRegion(int x, int y, int width, int height, CancellationToken cancellationToken) =>
        CaptureAsync(new(CaptureKind.Region, x, y, width, height), cancellationToken);

    [McpServerTool(Name = "computer.screenshot_monitor"), Description("Capture one local monitor by zero-based index.")]
    public Task<IEnumerable<ContentBlock>> ScreenshotMonitor(int monitor, CancellationToken cancellationToken) =>
        CaptureAsync(new(CaptureKind.Monitor, Monitor: monitor), cancellationToken);

    [McpServerTool(Name = "computer.screenshot_window"), Description("Capture a visible top-level window by handle returned from computer.windows.")]
    public Task<IEnumerable<ContentBlock>> ScreenshotWindow(long windowId, CancellationToken cancellationToken) =>
        CaptureAsync(new(CaptureKind.Window, WindowId: windowId), cancellationToken);

    [McpServerTool(Name = "computer.windows"), Description("List visible top-level windows on the local desktop.")]
    public Task<WindowInfo[]> Windows(CancellationToken cancellationToken) => screen.GetWindowsAsync(cancellationToken);

    [McpServerTool(Name = "computer.run"), Description("Execute PowerShell, Node.js or Python once or in a named persistent session.")]
    public Task<ExecutionResult> Run(string runtime, string code, string? environment = null, string? session = null, int timeoutMs = 30000, CancellationToken cancellationToken = default) =>
        execution.RunAsync(runtime, code, environment, session, timeoutMs, cancellationToken);

    [McpServerTool(Name = "computer.close_session"), Description("Close a persistent local runtime session.")]
    public Task<bool> CloseSession(string runtime, string session, CancellationToken cancellationToken) =>
        execution.CloseSessionAsync(runtime, session, cancellationToken);

    [McpServerTool(Name = "computer.environment"), Description("Create or update a local Python venv or Node.js project environment.")]
    public Task<EnvironmentResult> Environment(string runtime, string name, string[]? packages = null, CancellationToken cancellationToken = default) =>
        environments.EnsureAsync(runtime, name, packages ?? [], cancellationToken);

    [McpServerTool(Name = "computer.lua"), Description("Run a local Lua script with computer bindings.")]
    public Task<LuaResult> Lua(string code, string? session = null, CancellationToken cancellationToken = default) =>
        lua.RunAsync(code, session, cancellationToken);

    [McpServerTool(Name = "computer.click"), Description("Move, pause and click the local mouse.")]
    public Task<InputResult> Click(int x, int y, CancellationToken cancellationToken = default) => input.ClickAsync(x, y, cancellationToken);

    [McpServerTool(Name = "computer.double_click"), Description("Move, pause and double click the local mouse.")]
    public Task<InputResult> DoubleClick(int x, int y, CancellationToken cancellationToken = default) => input.DoubleClickAsync(x, y, cancellationToken);

    [McpServerTool(Name = "computer.type"), Description("Type text with key down/key up events.")]
    public Task<InputResult> Type(string text, CancellationToken cancellationToken = default) => input.TypeAsync(text, cancellationToken);

    [McpServerTool(Name = "computer.press"), Description("Press and release one named key.")]
    public Task<InputResult> Press(string key, CancellationToken cancellationToken = default) => input.PressAsync(key, cancellationToken);

    [McpServerTool(Name = "computer.key_down"), Description(ToolDocumentation.KeyDown)]
    public Task<InputResult> KeyDown(KeyInput key, int holdTimeoutMs, CancellationToken cancellationToken = default) =>
        input.KeyDownAsync(key, holdTimeoutMs, cancellationToken);

    [McpServerTool(Name = "computer.key_up"), Description(ToolDocumentation.KeyUp)]
    public Task<InputResult> KeyUp(KeyInput key, CancellationToken cancellationToken = default) =>
        input.KeyUpAsync(key, cancellationToken);

    [McpServerTool(Name = "computer.hotkey"), Description("Press a key combination, for example CTRL+L.")]
    public Task<InputResult> Hotkey(string keys, CancellationToken cancellationToken = default) => input.HotkeyAsync(keys, cancellationToken);

    [McpServerTool(Name = "computer.scroll"), Description("Scroll the local mouse wheel.")]
    public Task<InputResult> Scroll(int delta, CancellationToken cancellationToken = default) => input.ScrollAsync(delta, cancellationToken);

    [McpServerTool(Name = "computer.drag"), Description("Drag the local mouse along a human-like trajectory.")]
    public Task<InputResult> Drag(int x1, int y1, int x2, int y2, CancellationToken cancellationToken = default) => input.DragAsync(x1, y1, x2, y2, cancellationToken);

    [McpServerTool(Name = "computer.sequence"), Description(ToolDocumentation.Sequence)]
    public Task<InputResult> Sequence(InputAction[] actions, bool resetBefore = false, bool resetAfter = false, CancellationToken cancellationToken = default) =>
        input.SequenceAsync(actions, resetBefore, resetAfter, cancellationToken);

    [McpServerTool(Name = "computer.raw_mouse"), Description("Send one raw FakerInput mouse report. The report is base64 in JSON.")]
    public Task<InputResult> RawMouse(byte[] report, CancellationToken cancellationToken = default) => input.RawMouseAsync(report, cancellationToken);

    [McpServerTool(Name = "computer.raw_keyboard"), Description("Send one raw FakerInput keyboard report. The report is base64 in JSON.")]
    public Task<InputResult> RawKeyboard(byte[] report, CancellationToken cancellationToken = default) => input.RawKeyboardAsync(report, cancellationToken);

    [McpServerTool(Name = "computer.release_all"), Description("Release all currently held virtual input buttons and keys.")]
    public Task<InputResult> ReleaseAll(CancellationToken cancellationToken = default) => input.ReleaseAllAsync(cancellationToken);

    private async Task<IEnumerable<ContentBlock>> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        var result = await screen.CaptureAsync(request, cancellationToken);
        return result.Ok && result.Data is not null
            ? [ImageContentBlock.FromBytes(result.Data, result.MimeType)]
            : [new TextContentBlock { Text = result.Error ?? "screen_unavailable" }];
    }
}

