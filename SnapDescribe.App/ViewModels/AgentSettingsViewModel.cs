using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnapDescribe.App.Models;
using SnapDescribe.App.Services;

namespace SnapDescribe.App.ViewModels;

public partial class AgentSettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly LocalizationService _localization;

    [ObservableProperty]
    private AgentSettings agent;

    [ObservableProperty]
    private AgentTool? selectedTool;

    [ObservableProperty]
    private bool isDirty;

    public AgentSettingsViewModel(SettingsService settingsService, LocalizationService localization)
    {
        _settingsService = settingsService;
        _localization = localization;
        Agent = CloneAgent(settingsService.Current.Agent);

        AddToolCommand = new RelayCommand(AddTool);
        RemoveToolCommand = new RelayCommand(RemoveSelectedTool, () => SelectedTool is not null);
        DuplicateToolCommand = new RelayCommand(DuplicateSelectedTool, () => SelectedTool is not null);
        ResetCommand = new RelayCommand(Reset);
        SaveCommand = new RelayCommand(Save, () => IsDirty);

        AttachAgentHandlers();
    }

    public IRelayCommand AddToolCommand { get; }

    public IRelayCommand RemoveToolCommand { get; }

    public IRelayCommand DuplicateToolCommand { get; }

    public IRelayCommand ResetCommand { get; }

    public IRelayCommand SaveCommand { get; }

    public string ToolListEmptyHint => _localization.GetString("Agent.ToolsEmptyHint");

    public event EventHandler? Saved;

    partial void OnSelectedToolChanged(AgentTool? oldValue, AgentTool? newValue)
    {
        RemoveToolCommand.NotifyCanExecuteChanged();
        DuplicateToolCommand.NotifyCanExecuteChanged();
    }

    private void AttachAgentHandlers()
    {
        Agent.PropertyChanged += AgentOnPropertyChanged;
        Agent.Tools.CollectionChanged += OnToolsCollectionChanged;
        foreach (var tool in Agent.Tools)
        {
            tool.PropertyChanged += OnToolPropertyChanged;
        }
    }

    private void DetachAgentHandlers()
    {
        Agent.PropertyChanged -= AgentOnPropertyChanged;
        Agent.Tools.CollectionChanged -= OnToolsCollectionChanged;
        foreach (var tool in Agent.Tools)
        {
            tool.PropertyChanged -= OnToolPropertyChanged;
        }
    }

    private void AgentOnPropertyChanged(object? sender, PropertyChangedEventArgs e) => MarkDirty();

    private void OnToolsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (AgentTool tool in e.OldItems)
            {
                tool.PropertyChanged -= OnToolPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (AgentTool tool in e.NewItems)
            {
                tool.PropertyChanged += OnToolPropertyChanged;
            }
        }

        MarkDirty();
    }

    private void OnToolPropertyChanged(object? sender, PropertyChangedEventArgs e) => MarkDirty();

    private void AddTool()
    {
        var tool = new AgentTool
        {
            Name = _localization.GetString("Agent.ToolDefaultName"),
            Description = _localization.GetString("Agent.ToolDefaultDescription"),
            Command = string.Empty,
            TimeoutSeconds = 30,
            AutoRun = true
        };

        Agent.Tools.Add(tool);
        SelectedTool = tool;
        MarkDirty();
    }

    private void RemoveSelectedTool()
    {
        if (SelectedTool is null)
        {
            return;
        }

        var index = Agent.Tools.IndexOf(SelectedTool);
        if (index >= 0)
        {
            Agent.Tools.RemoveAt(index);
            SelectedTool = Agent.Tools.Count > 0
                ? Agent.Tools[Math.Min(index, Agent.Tools.Count - 1)]
                : null;
            MarkDirty();
        }
    }

    private void DuplicateSelectedTool()
    {
        if (SelectedTool is null)
        {
            return;
        }

        var clone = CloneTool(SelectedTool);
        clone.Id = Guid.NewGuid().ToString("N");
        clone.Name = SelectedTool.Name + " (Copy)";
        Agent.Tools.Add(clone);
        SelectedTool = clone;
        MarkDirty();
    }

    private void Reset()
    {
        DetachAgentHandlers();
        Agent = CloneAgent(_settingsService.Current.Agent);
        AttachAgentHandlers();
        SelectedTool = Agent.Tools.FirstOrDefault();
        IsDirty = false;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void Save()
    {
        var snapshot = CloneAgent(Agent);
        _settingsService.Update(settings =>
        {
            settings.Agent = snapshot;
        });
        _settingsService.Save();
        IsDirty = false;
        SaveCommand.NotifyCanExecuteChanged();
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void MarkDirty()
    {
        IsDirty = true;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private static AgentSettings CloneAgent(AgentSettings source)
    {
        var clone = new AgentSettings
        {
            IsEnabled = source?.IsEnabled ?? false,
            SystemPrompt = source?.SystemPrompt ?? string.Empty,
            RunToolsBeforeModel = source?.RunToolsBeforeModel ?? true,
            IncludeToolOutputInResponse = source?.IncludeToolOutputInResponse ?? true,
            Tools = new ObservableCollection<AgentTool>()
        };

        if (source?.Tools is not null)
        {
            foreach (var tool in source.Tools)
            {
                clone.Tools.Add(CloneTool(tool));
            }
        }

        return clone;
    }

    private static AgentTool CloneTool(AgentTool source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Description = source.Description,
        Command = source.Command,
        ArgumentsTemplate = source.ArgumentsTemplate,
        AutoRun = source.AutoRun,
        TimeoutSeconds = source.TimeoutSeconds
    };
}
