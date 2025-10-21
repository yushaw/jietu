using System;
using System.IO;
using SkiaSharp;

namespace SnapDescribe.App.Services;

/// <summary>
/// Image preprocessing service to improve OCR accuracy
/// </summary>
public class ImagePreprocessor
{
    /// <summary>
    /// Preprocess image for optimal OCR recognition
    /// </summary>
    /// <param name="imageBytes">Original image bytes</param>
    /// <returns>Preprocessed image bytes in PNG format</returns>
    public byte[] PreprocessForOcr(byte[] imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0)
        {
            return imageBytes ?? Array.Empty<byte>();
        }

        try
        {
            using var inputStream = new MemoryStream(imageBytes);
            using var original = SKBitmap.Decode(inputStream);

            if (original == null)
            {
                return imageBytes;
            }

            // Minimal preprocessing for Tesseract:
            // - Convert to grayscale (helps Tesseract focus on text structure)
            // - Keep original resolution (upscaling doesn't add information)
            // - Let Tesseract handle binarization with its adaptive algorithms

            using var grayscale = ConvertToGrayscale(original);

            // Encode back to PNG
            using var image = SKImage.FromBitmap(grayscale);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("Image preprocessing failed, using original image", ex);
            return imageBytes;
        }
    }

    private SKBitmap ConvertToGrayscale(SKBitmap source)
    {
        var grayscale = new SKBitmap(source.Width, source.Height, SKColorType.Gray8, SKAlphaType.Opaque);

        using var canvas = new SKCanvas(grayscale);
        using var paint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateColorMatrix(new float[]
            {
                0.299f, 0.587f, 0.114f, 0, 0, // Red channel
                0.299f, 0.587f, 0.114f, 0, 0, // Green channel
                0.299f, 0.587f, 0.114f, 0, 0, // Blue channel
                0,      0,      0,      1, 0  // Alpha channel
            })
        };

        canvas.DrawBitmap(source, 0, 0, paint);
        return grayscale;
    }

    private SKBitmap EnhanceContrast(SKBitmap source)
    {
        var result = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);

        // Calculate histogram
        var histogram = new int[256];
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                var pixel = source.GetPixel(x, y);
                histogram[pixel.Red]++;
            }
        }

        // Calculate CDF (Cumulative Distribution Function)
        var cdf = new int[256];
        cdf[0] = histogram[0];
        for (int i = 1; i < 256; i++)
        {
            cdf[i] = cdf[i - 1] + histogram[i];
        }

        // Normalize CDF for histogram equalization
        var totalPixels = source.Width * source.Height;
        var lookupTable = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            lookupTable[i] = (byte)((cdf[i] * 255) / totalPixels);
        }

        // Apply equalization
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                var pixel = source.GetPixel(x, y);
                var newValue = lookupTable[pixel.Red];
                result.SetPixel(x, y, new SKColor(newValue, newValue, newValue));
            }
        }

        return result;
    }

    private SKBitmap ApplySimpleThreshold(SKBitmap source)
    {
        // Calculate Otsu's threshold (simple global thresholding)
        var histogram = new int[256];
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                histogram[source.GetPixel(x, y).Red]++;
            }
        }

        int total = source.Width * source.Height;
        float sum = 0;
        for (int i = 0; i < 256; i++)
        {
            sum += i * histogram[i];
        }

        float sumB = 0;
        int wB = 0;
        int wF = 0;
        float maxVariance = 0;
        int threshold = 0;

        for (int i = 0; i < 256; i++)
        {
            wB += histogram[i];
            if (wB == 0) continue;

            wF = total - wB;
            if (wF == 0) break;

            sumB += i * histogram[i];
            float mB = sumB / wB;
            float mF = (sum - sumB) / wF;
            float variance = wB * wF * (mB - mF) * (mB - mF);

            if (variance > maxVariance)
            {
                maxVariance = variance;
                threshold = i;
            }
        }

        // Apply threshold
        var result = new SKBitmap(source.Width, source.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                var pixelValue = source.GetPixel(x, y).Red;
                var binaryValue = pixelValue > threshold ? (byte)255 : (byte)0;
                result.SetPixel(x, y, new SKColor(binaryValue, binaryValue, binaryValue));
            }
        }

        return result;
    }

    private SKBitmap ApplyAdaptiveThreshold(SKBitmap source)
    {
        var result = new SKBitmap(source.Width, source.Height, SKColorType.Gray8, SKAlphaType.Opaque);
        const int blockSize = 15; // Must be odd
        const int C = 10; // Constant subtracted from mean

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                // Calculate local mean in blockSize x blockSize neighborhood
                int sum = 0;
                int count = 0;

                int halfBlock = blockSize / 2;
                for (int dy = -halfBlock; dy <= halfBlock; dy++)
                {
                    for (int dx = -halfBlock; dx <= halfBlock; dx++)
                    {
                        int nx = x + dx;
                        int ny = y + dy;

                        if (nx >= 0 && nx < source.Width && ny >= 0 && ny < source.Height)
                        {
                            sum += source.GetPixel(nx, ny).Red;
                            count++;
                        }
                    }
                }

                int threshold = (sum / count) - C;
                var pixelValue = source.GetPixel(x, y).Red;
                var binaryValue = pixelValue > threshold ? (byte)255 : (byte)0;

                result.SetPixel(x, y, new SKColor(binaryValue, binaryValue, binaryValue));
            }
        }

        return result;
    }

    private SKBitmap ScaleToOptimalDpi(SKBitmap source, int targetDpi)
    {
        // Assume original is 96 DPI (common screen DPI for screenshots)
        const int sourceDpi = 96;

        // Calculate scaling factor to reach target DPI
        float scale = (float)targetDpi / sourceDpi;

        // Always scale to reach target DPI - tesseract needs at least 300 DPI
        int newWidth = (int)(source.Width * scale);
        int newHeight = (int)(source.Height * scale);

        // If scale is very close to 1.0, just return a copy
        if (Math.Abs(scale - 1.0f) < 0.01f)
        {
            var copy = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);
            using var canvas = new SKCanvas(copy);
            canvas.DrawBitmap(source, 0, 0);
            return copy;
        }

        var result = new SKBitmap(newWidth, newHeight, source.ColorType, source.AlphaType);

        using var resultCanvas = new SKCanvas(result);
        using var paint = new SKPaint
        {
            FilterQuality = SKFilterQuality.High,
            IsAntialias = false // Disable antialiasing for sharper text
        };

        resultCanvas.Clear(SKColors.White);
        resultCanvas.Scale(scale, scale);
        resultCanvas.DrawBitmap(source, 0, 0, paint);

        return result;
    }
}

internal static class SKBitmapExtensions
{
    public static SKBitmap Apply(this SKBitmap bitmap, Action<SKBitmap> action)
    {
        action(bitmap);
        return bitmap;
    }
}
