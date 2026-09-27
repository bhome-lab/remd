using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using RemoteControl.Core;
using RemoteControl.Infrastructure.Desktop;

namespace RemoteControl.Tests.Desktop;

[SupportedOSPlatform("windows")]
public sealed class WindowsScreenServiceTests
{
    [Fact]
    public async Task PrimaryCaptureDefaultsToFullSizeJpeg75()
    {
        var desktop = new FakeDesktop();
        var service = new WindowsScreenService(desktop);
        var result = await service.CaptureAsync(new CaptureRequest(), default);
        var explicitQuality = await service.CaptureAsync(new CaptureRequest(Quality: 75), default);
        Assert.True(result.Ok);
        Assert.Equal("image/jpeg", result.MimeType);
        Assert.Equal(explicitQuality.Data, result.Data);
        Assert.Equal(desktop.PrimaryBounds, desktop.CapturedBounds);
        Assert.Equal((2560, 1440), Dimensions(result.Data!));
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
    public async Task OversizedRegionFailsBeforeCaptureAllocation()
    {
        var desktop = new FakeDesktop();
        var result = await new WindowsScreenService(desktop).CaptureAsync(
            new CaptureRequest(CaptureKind.Region, Width: 32769, Height: 1), default);
        Assert.Equal("capture_too_large", result.Error);
        Assert.Equal(0, desktop.CaptureCount);
    }

    [Fact]
    public async Task PngUsesItsMimeTypeAndScalesWithinBothLimits()
    {
        var desktop = new FakeDesktop();
        var result = await new WindowsScreenService(desktop).CaptureAsync(
            new CaptureRequest(CaptureKind.Region, Width: 512, Height: 256,
                Format: "png", MaxWidth: 200, MaxHeight: 80), default);
        Assert.True(result.Ok);
        Assert.Equal("image/png", result.MimeType);
        Assert.Equal((160, 80), Dimensions(result.Data!));
        Assert.Equal(1, desktop.CaptureCount);
    }

    [Fact]
    public async Task ByteBudgetShrinksOneCapturedFrameWithoutChangingJpegQuality()
    {
        var desktop = new FakeDesktop();
        var service = new WindowsScreenService(desktop);
        var full = await service.CaptureAsync(new CaptureRequest(CaptureKind.Region, Width: 512, Height: 256, Quality: 70), default);
        var budget = full.Data!.Length / 2;
        var result = await service.CaptureAsync(new CaptureRequest(CaptureKind.Region, Width: 512, Height: 256,
            Quality: 70, MaxBytes: budget), default);

        Assert.True(result.Ok);
        Assert.True(result.Data!.Length <= budget);
        var (width, height) = Dimensions(result.Data);
        Assert.True(width < 512 && height < 256);
        Assert.Equal(2, desktop.CaptureCount);

        var fixedSize = await service.CaptureAsync(new CaptureRequest(CaptureKind.Region, Width: 512, Height: 256,
            Quality: 70, MaxWidth: width), default);
        Assert.Equal((width, height), Dimensions(fixedSize.Data!));
        Assert.Equal(fixedSize.Data, result.Data);
    }

    [Fact]
    public async Task PngByteBudgetReturnsSmallerLosslessImage()
    {
        var desktop = new FakeDesktop();
        var service = new WindowsScreenService(desktop);
        var full = await service.CaptureAsync(new CaptureRequest(CaptureKind.Region,
            Width: 512, Height: 256, Format: "png"), default);
        var budget = full.Data!.Length / 2;
        var result = await service.CaptureAsync(new CaptureRequest(CaptureKind.Region,
            Width: 512, Height: 256, Format: "png", MaxBytes: budget), default);

        Assert.True(result.Ok);
        Assert.Equal("image/png", result.MimeType);
        Assert.True(result.Data!.Length <= budget);
        Assert.True(Dimensions(result.Data).Width < 512);
        Assert.Equal(2, desktop.CaptureCount);
    }

    [Fact]
    public async Task ImpossibleBudgetReturnsErrorWithoutImage()
    {
        var desktop = new FakeDesktop();
        var result = await new WindowsScreenService(desktop).CaptureAsync(
            new CaptureRequest(CaptureKind.Region, Width: 256, Height: 128, MaxBytes: 1), default);
        Assert.False(result.Ok);
        Assert.Equal("image_budget_unreachable", result.Error);
        Assert.Null(result.Data);
        Assert.Equal(1, desktop.CaptureCount);
    }

    [Fact]
    public async Task InvalidEncodingOptionsFailBeforeCapture()
    {
        var desktop = new FakeDesktop();
        var service = new WindowsScreenService(desktop);
        CaptureRequest[] invalid = [
            new(Format: "webp"), new(Quality: 0), new(Quality: 101), new(Format: "png", Quality: 75),
            new(MaxWidth: 0), new(MaxHeight: -1), new(MaxBytes: 0),
            new(MaxBytes: 16 * 1024 * 1024 + 1)
        ];
        foreach (var request in invalid)
        {
            var result = await service.CaptureAsync(request, default);
            Assert.Equal("invalid_capture_options", result.Error);
        }
        Assert.Equal(0, desktop.CaptureCount);
    }

    [Fact]
    public async Task CancellationDuringCaptureIsNotReturnedAsScreenFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var desktop = new FakeDesktop { OnCapture = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WindowsScreenService(desktop).CaptureAsync(new CaptureRequest(), cancellation.Token));
        Assert.Equal(1, desktop.CaptureCount);
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
        public ScreenBounds? CapturedBounds { get; private set; }
        public int CaptureCount { get; private set; }
        public Action? OnCapture { get; set; }
        public bool IsSupported => true;
        public bool IsInteractive => true;
        public ScreenBounds PrimaryBounds => new(0, 0, 2560, 1440);
        public bool TryGetMonitorBounds(int index, out ScreenBounds bounds) { bounds = new(-1920, 0, 1920, 1080); return index == 1; }
        public bool TryGetWindowBounds(long handle, out ScreenBounds bounds) { bounds = default; return false; }
        public Bitmap CaptureFrame(ScreenBounds bounds)
        {
            CapturedBounds = bounds;
            CaptureCount++;
            OnCapture?.Invoke();
            var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Navy);
            for (var x = 0; x < bounds.Width; x += 8)
            {
                using var pen = new Pen(Color.FromArgb(x * 37 % 256, x * 73 % 256, x * 109 % 256));
                graphics.DrawLine(pen, x, 0, x, bounds.Height);
            }
            return bitmap;
        }
        public WindowInfo[] EnumerateWindows() => [];
    }

    private static (int Width, int Height) Dimensions(byte[] encoded)
    {
        using var stream = new MemoryStream(encoded);
        using var image = Image.FromStream(stream);
        return (image.Width, image.Height);
    }
}
