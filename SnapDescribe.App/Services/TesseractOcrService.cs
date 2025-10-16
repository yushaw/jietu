using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SnapDescribe.App.Models;
using InteropDotNet;
using Tesseract;

namespace SnapDescribe.App.Services;

public sealed class TesseractOcrService : IOcrService
{
    private readonly SettingsService _settingsService;
    private static readonly object NativeInitLock = new();
    private static bool _nativeInitialized;

    public TesseractOcrService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        EnsureNativeInitialization();
    }

    public bool IsAvailable
    {
        get
        {
            var path = GetDataPath();
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return false;
            }

            var nativePath = GetNativeLibraryDirectory();
            return !string.IsNullOrWhiteSpace(nativePath) && Directory.Exists(nativePath);
        }
    }

    public Task<OcrResult> RecognizeAsync(byte[] imageBytes, string? languages, CancellationToken cancellationToken = default)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            throw new ArgumentException("Image data cannot be empty.", nameof(imageBytes));
        }

        return Task.Run(() => RecognizeInternal(imageBytes, languages, cancellationToken), cancellationToken);
    }

    private OcrResult RecognizeInternal(byte[] imageBytes, string? languages, CancellationToken cancellationToken)
    {
        var dataPath = GetDataPath();
        if (string.IsNullOrWhiteSpace(dataPath) || !Directory.Exists(dataPath))
        {
            throw new InvalidOperationException("Configure the tessdata directory in Settings before running OCR.");
        }

        var effectiveLanguages = GetLanguages(languages);
        if (string.IsNullOrWhiteSpace(effectiveLanguages))
        {
            throw new InvalidOperationException("No OCR language configured. Provide one in settings or rule parameters.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var pix = Pix.LoadFromMemory(imageBytes);
            using var engine = new TesseractEngine(dataPath, effectiveLanguages, EngineMode.LstmOnly);
            using var page = engine.Process(pix);

            var segments = ExtractSegments(page, cancellationToken);
            return new OcrResult(segments, effectiveLanguages);
        }
        catch (TesseractException tex)
        {
            DiagnosticLogger.Log("Tesseract OCR failed.", tex);
            throw new InvalidOperationException("OCR engine failed while processing the screenshot.", tex);
        }
    }

    private static IReadOnlyList<OcrSegment> ExtractSegments(Page page, CancellationToken cancellationToken)
    {
        var segments = new List<OcrSegment>();
        using var iterator = page.GetIterator();
        iterator.Begin();

        var index = 1;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = iterator.GetText(PageIteratorLevel.TextLine);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            text = Normalize(text);

            OcrBoundingBox? bounds = null;
            if (iterator.TryGetBoundingBox(PageIteratorLevel.TextLine, out var rect))
            {
                bounds = OcrBoundingBox.FromPixels(rect.X1, rect.Y1, rect.Width, rect.Height);
            }

            var confidence = iterator.GetConfidence(PageIteratorLevel.TextLine);
            segments.Add(new OcrSegment(index++, text, bounds, confidence));
        }
        while (iterator.Next(PageIteratorLevel.TextLine));

        if (segments.Count == 0)
        {
            var plain = Normalize(page.GetText());
            if (!string.IsNullOrWhiteSpace(plain))
            {
                segments.Add(new OcrSegment(1, plain, bounds: null, confidence: page.GetMeanConfidence()));
            }
        }

        return segments;
    }

    private string GetLanguages(string? languages)
    {
        if (!string.IsNullOrWhiteSpace(languages))
        {
            return NormalizeLanguages(languages);
        }

        var defaults = _settingsService.Current.OcrDefaultLanguages;
        if (!string.IsNullOrWhiteSpace(defaults))
        {
            return NormalizeLanguages(defaults);
        }

        return string.Empty;
    }

    private static string Normalize(string text)
        => string.Join(Environment.NewLine,
            text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()))
           .Trim();

    private static string NormalizeLanguages(string languages)
    {
        var parts = languages
            .Split(new[] { ',', ';', '+', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .Where(part => part.Length > 0)
            .ToArray();

        return parts.Length == 0 ? string.Empty : string.Join("+", parts);
    }

    private string GetDataPath()
    {
        var configured = _settingsService.Current.OcrTessDataPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var appDirectory = AppContext.BaseDirectory;
        var defaultPath = Path.Combine(appDirectory, "tessdata");
        return Directory.Exists(defaultPath) ? defaultPath : configured;
    }

    private string? GetNativeLibraryDirectory()
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return null;
        }

        var architecture = Environment.Is64BitProcess ? "x64" : "x86";
        var nativeDirectory = Path.Combine(baseDirectory, architecture);
        return Directory.Exists(nativeDirectory) ? nativeDirectory : null;
    }

    private void EnsureNativeInitialization()
    {
        if (_nativeInitialized)
        {
            return;
        }

        lock (NativeInitLock)
        {
            if (_nativeInitialized)
            {
                return;
            }

            var nativeDirectory = GetNativeLibraryDirectory();
            if (!string.IsNullOrWhiteSpace(nativeDirectory))
            {
                var loader = LibraryLoader.Instance;
                var desiredPath = Path.GetDirectoryName(nativeDirectory) ?? nativeDirectory;
                if (!string.Equals(loader.CustomSearchPath, desiredPath, StringComparison.OrdinalIgnoreCase))
                {
                    loader.CustomSearchPath = desiredPath;
                    DiagnosticLogger.Log($"Tesseract native search path set to {desiredPath}");
                }
            }

            var tessData = GetDataPath();
            if (!string.IsNullOrWhiteSpace(tessData))
            {
                Environment.SetEnvironmentVariable("TESSDATA_PREFIX", tessData);
            }

            _nativeInitialized = true;
        }
    }
}
