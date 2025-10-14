using System;
using System.IO;
using System.Text;
using System.Text.Json;
using SnapDescribe.App.Models;

namespace SnapDescribe.App.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private AppSettings _current;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var settingsFolder = Path.Combine(appData, "SnapDescribe");
        Directory.CreateDirectory(settingsFolder);

        _settingsPath = Path.Combine(settingsFolder, "settings.json");
        _current = LoadSettings(_settingsPath) ?? new AppSettings();

        EnsureOutputDirectory();
    }

    public AppSettings Current => _current;

    public void Update(Action<AppSettings> applyChanges)
    {
        applyChanges(_current);
        EnsureOutputDirectory();
    }

    public void EnsureOutputDirectory()
    {
        if (string.IsNullOrWhiteSpace(_current.OutputDirectory))
        {
            _current.OutputDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SnapDescribe");
        }

        Directory.CreateDirectory(_current.OutputDirectory);
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(_current, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(_settingsPath, json, Encoding.UTF8);
    }

    public void Replace(AppSettings settings)
    {
        _current = settings;
        EnsureOutputDirectory();
        Save();
    }

    private static AppSettings? LoadSettings(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return settings ?? new AppSettings();
        }
        catch
        {
            // ignore malformed file, fall back to default settings
            return new AppSettings();
        }
    }
}
