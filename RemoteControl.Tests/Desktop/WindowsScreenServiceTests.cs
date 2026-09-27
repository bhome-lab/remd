using RemoteControl.Core;
using RemoteControl.Infrastructure.Desktop;

namespace RemoteControl.Tests.Desktop;

public sealed class WindowsScreenServiceTests
{
    [Fact]
    public async Task PrimaryCaptureUsesDesktopDimensionsAndJpeg75()
    {
        var desktop = new FakeDesktop();
        var service = new WindowsScreenService(desktop);
        var result = await service.CaptureAsync(new CaptureRequest(), default);
        Assert.True(result.Ok);
        Assert.Equal("image/jpeg", result.MimeType);
        Assert.Equal(desktop.Jpeg, result.Data);
        Assert.Equal(desktop.PrimaryBounds, desktop.CapturedBounds);
        Assert.Equal(75, desktop.Quality);
    }

    [Fact]
    public async Task MonitorCapturePreservesNegativeDesktopCoordinates()
    {
        var desktop = new FakeDesktop();
        var result = await new WindowsScreenService(desktop).CaptureAsync(new CaptureRequest(CaptureKind.Monitor, Monitor: 1), default);
        Assert.True(result.Ok);
        Assert.Equal(new ScreenBounds(-1920, 0, 1920, 1080), desktop.CapturedBounds);
    }

    [Fact]
    public async Task InvalidDimensionsFailBeforeCaptureAllocation()
    {
        var desktop = new FakeDesktop();
        var result = await new WindowsScreenService(desktop).CaptureAsync(new CaptureRequest(CaptureKind.Region, Width: 0, Height: 20), default);
        Assert.False(result.Ok);
        Assert.Equal("screen_unavailable", result.Error);
        Assert.Null(desktop.CapturedBounds);
    }

    [Fact]
    public async Task MissingWindowReturnsSpecificFailure()
    {
        var desktop = new FakeDesktop();
        var result = await new WindowsScreenService(desktop).CaptureAsync(new CaptureRequest(CaptureKind.Window, WindowId: 999), default);
        Assert.False(result.Ok);
        Assert.Equal("window_not_found", result.Error);
        Assert.Null(desktop.CapturedBounds);
    }

    private sealed class FakeDesktop : IDesktopScreen
    {
        public byte[] Jpeg { get; } = [0xff, 0xd8, 0xff, 0xd9];
        public ScreenBounds? CapturedBounds { get; private set; }
        public int Quality { get; private set; }
        public bool IsSupported => true;
        public bool IsInteractive => true;
        public ScreenBounds PrimaryBounds => new(0, 0, 2560, 1440);
        public bool TryGetMonitorBounds(int index, out ScreenBounds bounds) { bounds = new(-1920, 0, 1920, 1080); return index == 1; }
        public bool TryGetWindowBounds(long handle, out ScreenBounds bounds) { bounds = default; return false; }
        public byte[] CaptureJpeg(ScreenBounds bounds, int quality) { CapturedBounds = bounds; Quality = quality; return Jpeg; }
        public WindowInfo[] EnumerateWindows() => [];
    }
}
