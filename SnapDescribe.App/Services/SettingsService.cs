using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Linq;
using SnapDescribe.App.Models;

namespace SnapDescribe.App.Services;

public class SettingsService
{
    private static readonly PromptRuleTemplate[] DefaultPromptRules =
    {
        new("Acrobat.exe", null, "你是一名OCR助手，请识别截图中的PDF文档内容，准确转写主要文字，保留层级结构，并将表格内容转换为 Markdown 表格。若存在公式或特殊符号，请尽量还原。"),
        new("AcroRd32.exe", null, "你是一名OCR助手，请识别截图中的PDF文档内容，准确转写主要文字，保留层级结构，并将表格内容转换为 Markdown 表格。若存在公式或特殊符号，请尽量还原。"),
        new("FoxitPDFReader.exe", null, "你是一名OCR助手，请识别截图中的PDF文档内容，准确转写主要文字，保留层级结构，并将表格内容转换为 Markdown 表格。若存在公式或特殊符号，请尽量还原。"),
        new("FoxitReader.exe", null, "你是一名OCR助手，请识别截图中的PDF文档内容，准确转写主要文字，保留层级结构，并将表格内容转换为 Markdown 表格。若存在公式或特殊符号，请尽量还原。"),
        new("SumatraPDF.exe", null, "你是一名OCR助手，请识别截图中的PDF文档内容，准确转写主要文字，保留层级结构，并将表格内容转换为 Markdown 表格。若存在公式或特殊符号，请尽量还原。"),
        new("PDFXCview.exe", null, "你是一名OCR助手，请识别截图中的PDF文档内容，准确转写主要文字，保留层级结构，并将表格内容转换为 Markdown 表格。若存在公式或特殊符号，请尽量还原。"),
        new("chrome.exe", null, "你是一名网页总结助手，请基于截图中的网页内容提炼主题、核心观点与关键数据，分条给出摘要，并额外提供一条可执行的操作建议。"),
        new("msedge.exe", null, "你是一名网页总结助手，请基于截图中的网页内容提炼主题、核心观点与关键数据，分条给出摘要，并额外提供一条可执行的操作建议。"),
        new("tuitui.exe", null, "你是一名即时通讯助手。先简要概述聊天上下文，识别消息的发送者和接收者，然后代替当前账号撰写一条自然、有礼貌且贴合语境的回复消息，并说明回复意图。"),
        new("WeChat.exe", null, "你是一名即时通讯助手。先简要概述聊天上下文，识别消息的发送者和接收者，然后代替当前账号撰写一条自然、有礼貌且贴合语境的回复消息，并说明回复意图。"),
        new("et.exe", null, "你是一名数据分析助手，请阅读截图中的电子表格，概括表格结构，提取关键指标，分析趋势或异常，并给出数据驱动的建议。必要时使用条理清晰的列表列出结论。"),
        new("wps.exe", null, "你是一名中文写作润色助手，请阅读截图中的文档内容，指出存在的语言问题并给出润色后的版本，保持原意同时使表达更清晰、流畅且书面化。")
    };

    private readonly string _settingsPath;
    private AppSettings _current;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var settingsFolder = Path.Combine(appData, "SnapDescribe");
        Directory.CreateDirectory(settingsFolder);

        _settingsPath = Path.Combine(settingsFolder, "settings.json");
        _current = LoadSettings(_settingsPath) ?? new AppSettings();
        EnsurePromptRules();
        EnsureOutputDirectory();
    }

    public AppSettings Current => _current;

    public void Update(Action<AppSettings> applyChanges)
    {
        applyChanges(_current);
        EnsurePromptRules();
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
        EnsurePromptRules();
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

    private void EnsurePromptRules()
    {
        if (_current.PromptRules is null)
        {
            _current.PromptRules = new ObservableCollection<PromptRule>();
        }

        EnsureDefaultPromptRules();

        foreach (var rule in _current.PromptRules)
        {
            if (string.IsNullOrWhiteSpace(rule.Id))
            {
                rule.Id = Guid.NewGuid().ToString("N");
            }
        }
    }

    private void EnsureDefaultPromptRules()
    {
        if (_current.HasSeededDefaultPromptRules)
        {
            return;
        }

        var changed = false;
        foreach (var template in DefaultPromptRules)
        {
            var templateWindowTitle = template.WindowTitle ?? string.Empty;

            var exists = _current.PromptRules.Any(rule =>
                string.Equals(rule.ProcessName, template.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rule.WindowTitle ?? string.Empty, templateWindowTitle, StringComparison.OrdinalIgnoreCase));

            if (exists)
            {
                continue;
            }

            _current.PromptRules.Add(new PromptRule
            {
                ProcessName = template.ProcessName,
                WindowTitle = templateWindowTitle,
                Prompt = template.Prompt
            });
            changed = true;
        }

        if (!_current.HasSeededDefaultPromptRules)
        {
            _current.HasSeededDefaultPromptRules = true;
            changed = true;
        }

        if (changed)
        {
            Save();
        }
    }

    private readonly record struct PromptRuleTemplate(string ProcessName, string? WindowTitle, string Prompt);
}
