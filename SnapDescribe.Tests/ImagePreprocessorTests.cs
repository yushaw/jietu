using System;
using System.IO;
using SkiaSharp;
using SnapDescribe.App.Services;
using Xunit;

namespace SnapDescribe.Tests;

public class ImagePreprocessorTests
{
    private readonly ImagePreprocessor _preprocessor;

    public ImagePreprocessorTests()
    {
        _preprocessor = new ImagePreprocessor();
    }

    [Fact]
    public void PreprocessForOcr_WithNullBytes_ReturnsEmptyArray()
    {
        // Act
        var result = _preprocessor.PreprocessForOcr(null!);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void PreprocessForOcr_WithEmptyBytes_ReturnsEmptyArray()
    {
        // Act
        var result = _preprocessor.PreprocessForOcr(Array.Empty<byte>());

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void PreprocessForOcr_WithValidColorImage_ReturnsGrayscaleImage()
    {
        // Arrange - Create a simple RGB test image
        var original = new SKBitmap(100, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(original))
        {
            canvas.Clear(SKColors.Red);
            canvas.DrawRect(new SKRect(25, 25, 75, 75), new SKPaint { Color = SKColors.Blue });
        }

        byte[] originalBytes;
        using (var image = SKImage.FromBitmap(original))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            originalBytes = data.ToArray();
        }

        // Act
        var processedBytes = _preprocessor.PreprocessForOcr(originalBytes);

        // Assert
        Assert.NotNull(processedBytes);
        Assert.NotEmpty(processedBytes);
        Assert.NotEqual(originalBytes.Length, processedBytes.Length); // Should be different after processing

        // Verify the output is a valid image
        using var stream = new MemoryStream(processedBytes);
        using var processedBitmap = SKBitmap.Decode(stream);
        Assert.NotNull(processedBitmap);

        // Verify it's grayscale (Gray8)
        Assert.Equal(SKColorType.Gray8, processedBitmap.ColorType);

        // Verify dimensions are preserved
        Assert.Equal(100, processedBitmap.Width);
        Assert.Equal(100, processedBitmap.Height);
    }

    [Fact]
    public void PreprocessForOcr_WithInvalidImageData_ReturnsOriginalBytes()
    {
        // Arrange - Invalid image data
        var invalidBytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };

        // Act
        var result = _preprocessor.PreprocessForOcr(invalidBytes);

        // Assert - Should return original bytes when decoding fails
        Assert.Equal(invalidBytes, result);
    }

    [Fact]
    public void PreprocessForOcr_PreservesImageDimensions()
    {
        // Arrange - Create test images of different sizes
        var testSizes = new[] { (50, 50), (200, 100), (1920, 1080) };

        foreach (var (width, height) in testSizes)
        {
            var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.White);
            }

            byte[] originalBytes;
            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            {
                originalBytes = data.ToArray();
            }

            // Act
            var processedBytes = _preprocessor.PreprocessForOcr(originalBytes);

            // Assert
            using var stream = new MemoryStream(processedBytes);
            using var processedBitmap = SKBitmap.Decode(stream);

            Assert.Equal(width, processedBitmap.Width);
            Assert.Equal(height, processedBitmap.Height);
        }
    }

    [Fact]
    public void PreprocessForOcr_DoesNotUpscaleLowResolutionImages()
    {
        // Arrange - Create a small image (simulating low DPI)
        var smallBitmap = new SKBitmap(96, 96, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(smallBitmap))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawText("Test", 10, 50, new SKPaint { Color = SKColors.White, TextSize = 24 });
        }

        byte[] originalBytes;
        using (var image = SKImage.FromBitmap(smallBitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            originalBytes = data.ToArray();
        }

        // Act
        var processedBytes = _preprocessor.PreprocessForOcr(originalBytes);

        // Assert - Dimensions should be preserved, not upscaled
        using var stream = new MemoryStream(processedBytes);
        using var processedBitmap = SKBitmap.Decode(stream);

        Assert.Equal(96, processedBitmap.Width);
        Assert.Equal(96, processedBitmap.Height);
    }

    [Fact]
    public void PreprocessForOcr_HandlesTransparentImages()
    {
        // Arrange - Create an image with transparency
        var bitmap = new SKBitmap(100, 100, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(255, 255, 255, 128)); // Semi-transparent white
            canvas.DrawCircle(50, 50, 30, new SKPaint { Color = SKColors.Black });
        }

        byte[] originalBytes;
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            originalBytes = data.ToArray();
        }

        // Act
        var processedBytes = _preprocessor.PreprocessForOcr(originalBytes);

        // Assert
        Assert.NotNull(processedBytes);
        Assert.NotEmpty(processedBytes);

        using var stream = new MemoryStream(processedBytes);
        using var processedBitmap = SKBitmap.Decode(stream);
        Assert.NotNull(processedBitmap);
        Assert.Equal(SKColorType.Gray8, processedBitmap.ColorType);
    }
}
