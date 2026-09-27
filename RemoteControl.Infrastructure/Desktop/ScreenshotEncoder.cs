using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RemoteControl.Infrastructure.Desktop;

internal enum ScreenshotFormat { Jpeg, Png }

internal readonly record struct ScreenshotOptions(
    ScreenshotFormat Format, int Quality, int? MaxWidth, int? MaxHeight, int? MaxBytes);

internal static class ScreenshotEncoder
{
    private const int MaximumBudgetBytes = 16 * 1024 * 1024;

    public static bool TryGetOptions(CaptureRequest request, out ScreenshotOptions options)
    {
        options = default;
        ScreenshotFormat format;
        if (string.Equals(request.Format, "jpeg", StringComparison.OrdinalIgnoreCase)) format = ScreenshotFormat.Jpeg;
        else if (string.Equals(request.Format, "png", StringComparison.OrdinalIgnoreCase)) format = ScreenshotFormat.Png;
        else return false;

        if (format == ScreenshotFormat.Png && request.Quality is not null) return false;
        var quality = request.Quality ?? 75;
        if (quality is < 1 or > 100 || request.MaxWidth is <= 0 or > 32768 ||
            request.MaxHeight is <= 0 or > 32768 || request.MaxBytes is <= 0 or > MaximumBudgetBytes)
            return false;

        options = new(format, quality, request.MaxWidth, request.MaxHeight, request.MaxBytes);
        return true;
    }

    public static CaptureResult Encode(Bitmap frame, ScreenshotOptions options, CancellationToken cancellationToken)
    {
        var scale = Math.Min(1.0, Math.Min(
            (double)(options.MaxWidth ?? frame.Width) / frame.Width,
            (double)(options.MaxHeight ?? frame.Height) / frame.Height));
        var width = Math.Max(1, (int)Math.Floor(frame.Width * scale));
        var height = Math.Max(1, (int)Math.Floor(frame.Height * scale));
        var mimeType = options.Format == ScreenshotFormat.Jpeg ? "image/jpeg" : "image/png";

        cancellationToken.ThrowIfCancellationRequested();
        var data = EncodeAtSize(frame, width, height, options);
        if (options.MaxBytes is not int budget || data.Length <= budget)
            return new(true, data, mimeType);

        // Search dimensions of this one captured frame; quality and codec stay fixed.
        var largestDimension = Math.Max(width, height);
        byte[]? bestFit = null;
        var smallestSide = 1;
        var largestSide = largestDimension - 1;
        while (smallestSide <= largestSide)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var side = smallestSide + (largestSide - smallestSide) / 2;
            var candidateWidth = Math.Max(1, (int)Math.Floor((double)width * side / largestDimension));
            var candidateHeight = Math.Max(1, (int)Math.Floor((double)height * side / largestDimension));
            var candidate = EncodeAtSize(frame, candidateWidth, candidateHeight, options);
            if (candidate.Length <= budget)
            {
                bestFit = candidate;
                smallestSide = side + 1;
            }
            else largestSide = side - 1;
        }

        return bestFit is null
            ? new(false, Error: "image_budget_unreachable")
            : new(true, bestFit, mimeType);
    }

    private static byte[] EncodeAtSize(Bitmap frame, int width, int height, ScreenshotOptions options)
    {
        Bitmap? resized = null;
        try
        {
            if (frame.Width != width || frame.Height != height)
            {
                resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                using var graphics = Graphics.FromImage(resized);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(frame, new Rectangle(0, 0, width, height));
            }

            using var stream = new MemoryStream();
            var image = resized ?? frame;
            if (options.Format == ScreenshotFormat.Jpeg)
            {
                var codec = ImageCodecInfo.GetImageEncoders().First(candidate => candidate.FormatID == ImageFormat.Jpeg.Guid);
                using var parameters = new EncoderParameters(1);
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)options.Quality);
                image.Save(stream, codec, parameters);
            }
            else image.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        finally { resized?.Dispose(); }
    }
}
