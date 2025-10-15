using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SnapDescribe.App.Models;

namespace SnapDescribe.App.Services;

public class SettingsService
{
    private const string DefaultPromptZh = "请用简洁的语言描述这张截图的关键信息，并给出一个可能的解答或操作建议。";
    private const string DefaultPromptEn = "Describe the key information shown in this screenshot and provide a concise answer or actionable suggestion.";

    private static readonly string[] SupportedLanguages = { "en-US", "zh-CN" };

    private static readonly PromptRuleTemplate[] DefaultPromptRules =
    {
        new("Acrobat.exe", null,
            "你是一名OCR助手，请识别截图中的PDF文档内容，准确转写主要文字，保留层级结构，并将表格内容转换为 Markdown 表格。若存在公式或特殊符号，请尽量还原。",
            "You are an OCR assistant. Transcribe the PDF content in the screenshot accurately, preserve headings, convert tables to Markdown, and reproduce formulas or special characters as faithfully as possible."),
        new("AcroRd32.exe", null,
            "你是一名OCR助手，请识别截图中的PDF文档内容，准确转写主要文字，保留层级结构，并将表格内容转换为 Markdown 表格。若存在公式或特殊符号，请尽量还原。",
            "You are an OCR assistant. Transcribe the PDF content in the screenshot accurately, preserve headings, convert tables to Markdown, and reproduce formulas or special characters as faithfully as possible."),
        new("FoxitPDFReader.exe", null,
            "你是一名OCR助手，请识别截图中的PDF文档内容，准确转写主要文字，保留层级结构，并将表格内容转换为 Markdown 表格。若存在公式或特殊符号，请尽量还原。",
            "You are an OCR assistant. Transcribe the PDF content in the screenshot accurately, preserve headings, convert tables to Markdown, and reproduce formulas or special characters as faithfully as possible."),
        new("FoxitReader.exe", null,
            "你是一名OCR助手，请识别截图中的PDF文档内容，准确转写主要文字，保留层级结构，并将表格内容转换为 Markdown 表格。若存在公式或特殊符号，请尽量还原。",
            "You are an OCR assistant. Transcribe the PDF content in the screenshot accurately, preserve headings, convert tables to Markdown, and reproduce formulas or special characters as faithfully as possible."),
        new("SumatraPDF.exe", null,
            "你是一名OCR助手，请识别截图中的PDF文档内容，准确转写主要文字，保留层级结构，并将表格内容转换为 Markdown 表格。若存在公式或特殊符号，请尽量还原。",
            "You are an OCR assistant. Transcribe the PDF content in the screenshot accurately, preserve headings, convert tables to Markdown, and reproduce formulas or special characters as faithfully as possible."),
        new("PDFXCview.exe", null,
            "你是一名OCR助手，请识别截图中的PDF文档内容，准确转写主要文字，保留层级结构，并将表格内容转换为 Markdown 表格。若存在公式或特殊符号，请尽量还原。",
            "You are an OCR assistant. Transcribe the PDF content in the screenshot accurately, preserve headings, convert tables to Markdown, and reproduce formulas or special characters as faithfully as possible."),
        new("chrome.exe", null,
            "你是一名网页总结助手，请基于截图中的网页内容提炼主题、核心观点与关键数据，分条给出摘要，并额外提供一条可执行的操作建议。",
            "You are a web summarization assistant. Summarize the webpage captured in the screenshot with key points and data, and provide one actionable suggestion."),
        new("msedge.exe", null,
            "你是一名网页总结助手，请基于截图中的网页内容提炼主题、核心观点与关键数据，分条给出摘要，并额外提供一条可执行的操作建议。",
            "You are a web summarization assistant. Summarize the webpage captured in the screenshot with key points and data, and provide one actionable suggestion."),
        new("tuitui.exe", null,
            "你是一名即时通讯助手。先简要概述聊天上下文，识别消息的发送者和接收者，然后代替当前账号撰写一条自然、有礼貌且贴合语境的回复消息，并说明回复意图。",
            "You are a messaging assistant. Briefly summarize the chat context, identify the sender and receiver, draft a natural and polite reply on behalf of the current user, and explain the intent of the reply."),
        new("WeChat.exe", null,
            "你是一名即时通讯助手。先简要概述聊天上下文，识别消息的发送者和接收者，然后代替当前账号撰写一条自然、有礼貌且贴合语境的回复消息，并说明回复意图。",
            "You are a messaging assistant. Briefly summarize the chat context, identify the sender and receiver, draft a natural and polite reply on behalf of the current user, and explain the intent of the reply."),
        new("et.exe", null,
            "你是一名数据分析助手，请阅读截图中的电子表格，概括表格结构，提取关键指标，分析趋势或异常，并给出数据驱动的建议。必要时使用条理清晰的列表列出结论。",
            "You are a data analysis assistant. Review the spreadsheet in the screenshot, outline its structure, highlight key metrics, analyze trends or anomalies, and offer data-driven suggestions using clear bullet points when necessary."),
        new("wps.exe", null,
            "你是一名中文写作润色助手，请阅读截图中的文档内容，指出存在的语言问题并给出润色后的版本，保持原意同时使表达更清晰、流畅且书面化。",
            "You are a writing refinement assistant. Review the document text, point out language issues, and provide an improved version that keeps the original meaning while making the wording clearer, smoother, and more formal.")
    };

    private readonly string _settingsPath;
    private readonly string _appDataRoot;
    private readonly string _picturesRoot;
    private AppSettings _current;

    public SettingsService(string? appDataPath = null, string? picturesPath = null)
    {
        _appDataRoot = !string.IsNullOrWhiteSpace(appDataPath)
            ? appDataPath
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (string.IsNullOrWhiteSpace(_appDataRoot))
        {
            throw new InvalidOperationException("Unable to resolve application data directory.");
        }

        _picturesRoot = !string.IsNullOrWhiteSpace(picturesPath)
            ? picturesPath
            : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

        if (string.IsNullOrWhiteSpace(_picturesRoot))
        {
            throw new InvalidOperationException("Unable to resolve pictures directory.");
        }

        var settingsFolder = Path.Combine(_appDataRoot, "SnapDescribe");
        Directory.CreateDirectory(settingsFolder);

        _settingsPath = Path.Combine(settingsFolder, "settings.json");
        var settingsExists = File.Exists(_settingsPath);
        _current = LoadSettings(_settingsPath) ?? new AppSettings();

        if (!settingsExists)
        {
            _current.OutputDirectory = Path.Combine(_picturesRoot, "SnapDescribe");
        }

        EnsureLanguage(!settingsExists);
        EnsurePromptRules();
        EnsureOutputDirectory();
    }

    public AppSettings Current => _current;

    public void Update(Action<AppSettings> applyChanges)
    {
        applyChanges(_current);
        EnsureLanguage();
        EnsurePromptRules();
        EnsureOutputDirectory();
    }

    public void EnsureOutputDirectory()
    {
        if (string.IsNullOrWhiteSpace(_current.OutputDirectory))
        {
            _current.OutputDirectory = Path.Combine(_picturesRoot, "SnapDescribe");
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
        EnsureLanguage();
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
                Prompt = template.GetPromptFor(_current.Language)
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

    private void EnsureLanguage(bool isNewProfile = false)
    {
        var targetLanguage = isNewProfile
            ? NormalizeLanguage(null)
            : NormalizeLanguage(_current.Language);

        var changed = !string.Equals(_current.Language, targetLanguage, StringComparison.OrdinalIgnoreCase);
        _current.Language = targetLanguage;

        if (string.Equals(_current.Language, "zh-CN", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(_current.DefaultPrompt, DefaultPromptEn, StringComparison.Ordinal))
            {
                _current.DefaultPrompt = DefaultPromptZh;
                changed = true;
            }
        }
        else
        {
            if (string.Equals(_current.DefaultPrompt, DefaultPromptZh, StringComparison.Ordinal))
            {
                _current.DefaultPrompt = DefaultPromptEn;
                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }

    private static string NormalizeLanguage(string? language)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            foreach (var supported in SupportedLanguages)
            {
                if (string.Equals(language, supported, StringComparison.OrdinalIgnoreCase))
                {
                    return supported;
                }
            }
        }

        var culture = CultureInfo.CurrentUICulture;
        if (culture?.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "zh-CN";
        }

        return "en-US";
    }

    private readonly record struct PromptRuleTemplate(string ProcessName, string? WindowTitle, string PromptZh, string PromptEn)
    {
        public string GetPromptFor(string? language)
        {
            return string.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase) ? PromptZh : PromptEn;
        }
    }
}
