namespace RemoteControl.Infrastructure.Desktop;

public readonly record struct ScreenBounds(int X, int Y, int Width, int Height);

public interface IDesktopScreen
{
    bool IsSupported { get; }
    bool IsInteractive { get; }
    ScreenBounds PrimaryBounds { get; }
    bool TryGetMonitorBounds(int index, out ScreenBounds bounds);
    bool TryGetWindowBounds(long handle, out ScreenBounds bounds);
    byte[] CaptureJpeg(ScreenBounds bounds, int quality);
    WindowInfo[] EnumerateWindows();
}

/// <summary>Desktop capture independent of MCP and the service/worker transport.</summary>
public sealed class WindowsScreenService : IScreenService
{
    private readonly IDesktopScreen _desktop;
    public WindowsScreenService(IDesktopScreen? desktop = null) => _desktop = desktop ?? new GdiDesktopScreen();

    public Task<DesktopStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new DesktopStatus(_desktop.IsInteractive, "running", _desktop.IsSupported ? "gdi" : "unavailable"));
    }

    public Task<CaptureResult> CaptureAsync(CaptureRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_desktop.IsSupported) return Task.FromResult(new CaptureResult(false, Error: "screen_unavailable"));

        try
        {
            ScreenBounds bounds;
            switch (request.Kind)
            {
                case CaptureKind.Primary: bounds = _desktop.PrimaryBounds; break;
                case CaptureKind.Region: bounds = new(request.X, request.Y, request.Width, request.Height); break;
                case CaptureKind.Monitor:
                    if (!_desktop.TryGetMonitorBounds(request.Monitor, out bounds))
                        return Task.FromResult(new CaptureResult(false, Error: "monitor_not_found"));
                    break;
                case CaptureKind.Window:
                    if (!_desktop.TryGetWindowBounds(request.WindowId, out bounds))
                        return Task.FromResult(new CaptureResult(false, Error: "window_not_found"));
                    break;
                default: return Task.FromResult(new CaptureResult(false, Error: "invalid_capture_kind"));
            }

            if (bounds.Width <= 0 || bounds.Height <= 0)
                return Task.FromResult(new CaptureResult(false, Error: "screen_unavailable"));

            var data = _desktop.CaptureJpeg(bounds, 75);
            return Task.FromResult(new CaptureResult(true, data));
        }
        catch (Exception exception)
        {
            return Task.FromResult(new CaptureResult(false, Error: $"screen_unavailable:{exception.Message}"));
        }
    }

    public Task<WindowInfo[]> GetWindowsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_desktop.IsSupported ? _desktop.EnumerateWindows() : []);
    }
}
