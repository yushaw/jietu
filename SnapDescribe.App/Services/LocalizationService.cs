using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

namespace SnapDescribe.App.Services;

public class LocalizationService
{
    private const string DefaultLanguage = "en-US";
    private static readonly LocalizationService FallbackInstance = new(true);
    private static LocalizationService? _instance;
    private readonly Dictionary<string, ResourceDictionary> _cache = new(StringComparer.OrdinalIgnoreCase);
    private ResourceDictionary? _currentDictionary;
    private string _currentLanguage = DefaultLanguage;
    private readonly bool _isFallback;

    public LocalizationService() : this(false)
    {
    }

    private LocalizationService(bool isFallback)
    {
        _isFallback = isFallback;
        if (!_isFallback)
        {
            _instance = this;
        }
    }

    public static LocalizationService Instance => _instance ?? FallbackInstance;

    public event EventHandler? LanguageChanged;

    public string CurrentLanguage => _currentLanguage;

    public void ApplyLanguage(string language)
    {
        var resolvedLanguage = NormalizeLanguage(language);
        var dictionary = LoadDictionary(resolvedLanguage) ?? LoadDictionary(DefaultLanguage);
        if (dictionary is null)
        {
            return;
        }

        var app = Application.Current;
        if (app is null || _isFallback)
        {
            _currentDictionary = dictionary;
            _currentLanguage = resolvedLanguage;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_currentDictionary is not null)
        {
            app.Resources.MergedDictionaries.Remove(_currentDictionary);
        }

        app.Resources.MergedDictionaries.Add(dictionary);
        _currentDictionary = dictionary;
        _currentLanguage = resolvedLanguage;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GetString(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        var dictionary = _currentDictionary;
        if (dictionary is not null && dictionary.TryGetResource(key, ThemeVariant.Default, out var value))
        {
            return value?.ToString() ?? key;
        }

        var fallback = LoadDictionary(DefaultLanguage);
        if (fallback is not null && fallback.TryGetResource(key, ThemeVariant.Default, out var fallbackValue))
        {
            return fallbackValue?.ToString() ?? key;
        }

        return key;
    }

    public string GetString(string key, params object[] args)
    {
        var format = GetString(key);
        return args is { Length: > 0 }
            ? string.Format(format, args)
            : format;
    }

    private ResourceDictionary? LoadDictionary(string language)
    {
        if (_cache.TryGetValue(language, out var cached))
        {
            return cached;
        }

        try
        {
            var baseUri = new Uri("avares://SnapDescribe/");
            var include = new ResourceInclude(baseUri)
            {
                Source = new Uri($"Resources/StringResources.{language}.axaml", UriKind.Relative)
            };
            if (include.Loaded is ResourceDictionary dictionary)
            {
                _cache[language] = dictionary;
                return dictionary;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"Failed to load language resources: {language}", ex);
        }

        return null;
    }

    private static string NormalizeLanguage(string? language)
    {
        if (string.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "zh", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-CN";
        }

        if (string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase)
            || string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            return "en-US";
        }

        return DefaultLanguage;
    }
}
