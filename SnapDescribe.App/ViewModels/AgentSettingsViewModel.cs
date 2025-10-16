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
    private AgentProfile? selectedProfile;

    [ObservableProperty]
    private AgentTool? selectedTool;

    [ObservableProperty]
    private bool isDirty;

    public AgentSettingsViewModel(SettingsService settingsService, LocalizationService localization)
    {
        _settingsService = settingsService;
        _localization = localization;

        Agent = CloneSettings(settingsService.Current.Agent);

        AddProfileCommand = new RelayCommand(AddProfile);
        RemoveProfileCommand = new RelayCommand(RemoveSelectedProfile, CanRemoveProfile);
        DuplicateProfileCommand = new RelayCommand(DuplicateSelectedProfile, () => SelectedProfile is not null);

        AddToolCommand = new RelayCommand(AddTool, () => SelectedProfile is not null);
        RemoveToolCommand = new RelayCommand(RemoveSelectedTool, () => SelectedTool is not null);
        DuplicateToolCommand = new RelayCommand(DuplicateSelectedTool, () => SelectedTool is not null);

        ResetCommand = new RelayCommand(Reset);
        SaveCommand = new RelayCommand(Save, () => IsDirty);

        SelectedProfile = Agent.Profiles.FirstOrDefault();
        SelectedTool = SelectedProfile?.Tools.FirstOrDefault();

        AttachHandlers();
    }

    public IRelayCommand AddProfileCommand { get; }

    public IRelayCommand RemoveProfileCommand { get; }

    public IRelayCommand DuplicateProfileCommand { get; }

    public IRelayCommand AddToolCommand { get; }

    public IRelayCommand RemoveToolCommand { get; }

    public IRelayCommand DuplicateToolCommand { get; }

    public IRelayCommand ResetCommand { get; }

    public IRelayCommand SaveCommand { get; }

    public string ProfileListEmptyHint => _localization.GetString("Agent.ProfileListEmptyHint");

    public string ToolListEmptyHint => _localization.GetString("Agent.ToolsEmptyHint");

    public event EventHandler? Saved;

    partial void OnSelectedProfileChanged(AgentProfile? oldValue, AgentProfile? newValue)
    {
        RemoveProfileCommand?.NotifyCanExecuteChanged();
        DuplicateProfileCommand?.NotifyCanExecuteChanged();
        AddToolCommand?.NotifyCanExecuteChanged();

        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnProfilePropertyChanged;
            if (oldValue.Tools is not null)
            {
                oldValue.Tools.CollectionChanged -= OnToolsCollectionChanged;
                foreach (var tool in oldValue.Tools)
                {
                    tool.PropertyChanged -= OnToolPropertyChanged;
                }
            }
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnProfilePropertyChanged;
            EnsureToolsCollection(newValue);
            newValue.Tools.CollectionChanged += OnToolsCollectionChanged;
            foreach (var tool in newValue.Tools)
            {
                tool.PropertyChanged += OnToolPropertyChanged;
            }
        }

        SelectedTool = newValue?.Tools?.FirstOrDefault();
    }

    partial void OnSelectedToolChanged(AgentTool? oldValue, AgentTool? newValue)
    {
        RemoveToolCommand?.NotifyCanExecuteChanged();
        DuplicateToolCommand?.NotifyCanExecuteChanged();
    }

    private void AttachHandlers()
    {
        Agent.PropertyChanged += AgentOnPropertyChanged;
        Agent.Profiles.CollectionChanged += OnProfilesCollectionChanged;
        foreach (var profile in Agent.Profiles)
        {
            profile.PropertyChanged += OnProfilePropertyChanged;
            EnsureToolsCollection(profile);
            profile.Tools.CollectionChanged += OnToolsCollectionChanged;
            foreach (var tool in profile.Tools)
            {
                tool.PropertyChanged += OnToolPropertyChanged;
            }
        }
    }

    private void DetachHandlers()
    {
        Agent.PropertyChanged -= AgentOnPropertyChanged;
        Agent.Profiles.CollectionChanged -= OnProfilesCollectionChanged;
        foreach (var profile in Agent.Profiles)
        {
            profile.PropertyChanged -= OnProfilePropertyChanged;
            if (profile.Tools is not null)
            {
                profile.Tools.CollectionChanged -= OnToolsCollectionChanged;
                foreach (var tool in profile.Tools)
                {
                    tool.PropertyChanged -= OnToolPropertyChanged;
                }
            }
        }
    }

    private void AgentOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkDirty();
    }

    private void OnProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (AgentProfile profile in e.OldItems)
            {
                profile.PropertyChanged -= OnProfilePropertyChanged;
                if (profile.Tools is not null)
                {
                    profile.Tools.CollectionChanged -= OnToolsCollectionChanged;
                    foreach (var tool in profile.Tools)
                    {
                        tool.PropertyChanged -= OnToolPropertyChanged;
                    }
                }
            }
        }

        if (e.NewItems is not null)
        {
            foreach (AgentProfile profile in e.NewItems)
            {
                if (profile is null)
                {
                    continue;
                }

                profile.PropertyChanged += OnProfilePropertyChanged;
                EnsureToolsCollection(profile);
                profile.Tools.CollectionChanged += OnToolsCollectionChanged;
                foreach (var tool in profile.Tools)
                {
                    tool.PropertyChanged += OnToolPropertyChanged;
                }
            }
        }

        if (Agent.Profiles.Count > 0 && SelectedProfile is null)
        {
            SelectedProfile = Agent.Profiles[0];
        }

        RemoveProfileCommand.NotifyCanExecuteChanged();
        DuplicateProfileCommand.NotifyCanExecuteChanged();
        AddToolCommand.NotifyCanExecuteChanged();
        MarkDirty();
    }

    private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e) => MarkDirty();

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

        RemoveToolCommand.NotifyCanExecuteChanged();
        DuplicateToolCommand.NotifyCanExecuteChanged();
        MarkDirty();
    }

    private void OnToolPropertyChanged(object? sender, PropertyChangedEventArgs e) => MarkDirty();

    private bool CanRemoveProfile() => SelectedProfile is not null && Agent.Profiles.Count > 1;

    private void AddProfile()
    {
        var profile = CreateDefaultProfile();
        Agent.Profiles.Add(profile);
        SelectedProfile = profile;
        MarkDirty();
    }

    private void RemoveSelectedProfile()
    {
        if (!CanRemoveProfile() || SelectedProfile is null)
        {
            return;
        }

        var index = Agent.Profiles.IndexOf(SelectedProfile);
        Agent.Profiles.RemoveAt(index);
        SelectedProfile = Agent.Profiles.Count == 0 ? null : Agent.Profiles[Math.Clamp(index - 1, 0, Agent.Profiles.Count - 1)];
        MarkDirty();
    }

    private void DuplicateSelectedProfile()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var clone = CloneProfile(SelectedProfile);
        clone.Id = Guid.NewGuid().ToString("N");
        clone.Name = SelectedProfile.Name + _localization.GetString("Agent.ProfileCopySuffix");
        Agent.Profiles.Add(clone);
        SelectedProfile = clone;
        MarkDirty();
    }

    private void AddTool()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var tool = CreateDefaultTool();
        SelectedProfile.Tools.Add(tool);
        SelectedTool = tool;
        MarkDirty();
    }

    private void RemoveSelectedTool()
    {
        if (SelectedProfile is null || SelectedTool is null)
        {
            return;
        }

        var tools = SelectedProfile.Tools;
        var index = tools.IndexOf(SelectedTool);
        if (index >= 0)
        {
            tools.RemoveAt(index);
            SelectedTool = tools.Count == 0 ? null : tools[Math.Clamp(index - 1, 0, tools.Count - 1)];
            MarkDirty();
        }
    }

    private void DuplicateSelectedTool()
    {
        if (SelectedProfile is null || SelectedTool is null)
        {
            return;
        }

        var clone = CloneTool(SelectedTool);
        clone.Id = Guid.NewGuid().ToString("N");
        clone.Name = SelectedTool.Name + _localization.GetString("Agent.ToolCopySuffix");
        SelectedProfile.Tools.Add(clone);
        SelectedTool = clone;
        MarkDirty();
    }

    private void Reset()
    {
        DetachHandlers();
        Agent = CloneSettings(_settingsService.Current.Agent);
        AttachHandlers();
        SelectedProfile = Agent.Profiles.FirstOrDefault();
        SelectedTool = SelectedProfile?.Tools.FirstOrDefault();
        IsDirty = false;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void Save()
    {
        if (Agent.Profiles.Count == 0)
        {
            var profile = CreateDefaultProfile();
            Agent.Profiles.Add(profile);
            SelectedProfile = profile;
        }

        Agent.DefaultProfileId = SelectedProfile?.Id ?? Agent.Profiles[0].Id;

        var snapshot = CloneSettings(Agent);
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

    private AgentProfile CreateDefaultProfile()
    {
        return new AgentProfile
        {
            Name = _localization.GetString("Agent.ProfileDefaultName"),
            Description = string.Empty,
            SystemPrompt = _localization.GetString("Agent.SystemPromptTemplate"),
            RunToolsBeforeModel = true,
            IncludeToolOutputInResponse = true
        };
    }

    private AgentTool CreateDefaultTool()
    {
        return new AgentTool
        {
            Name = _localization.GetString("Agent.ToolDefaultName"),
            Description = _localization.GetString("Agent.ToolDefaultDescription"),
            Command = string.Empty,
            ArgumentsTemplate = string.Empty,
            AutoRun = true,
            TimeoutSeconds = 30
        };
    }

    private static AgentSettings CloneSettings(AgentSettings source)
    {
        var clone = new AgentSettings
        {
            IsEnabled = source?.IsEnabled ?? false,
            DefaultProfileId = source?.DefaultProfileId,
            Profiles = new ObservableCollection<AgentProfile>()
        };

        if (source?.Profiles is not null)
        {
            foreach (var profile in source.Profiles)
            {
                if (profile is null)
                {
                    continue;
                }

                clone.Profiles.Add(CloneProfile(profile));
            }
        }

        if (!clone.Profiles.Any())
        {
            clone.Profiles.Add(new AgentProfile());
        }

        if (string.IsNullOrWhiteSpace(clone.DefaultProfileId) || clone.Profiles.All(p => !string.Equals(p.Id, clone.DefaultProfileId, StringComparison.OrdinalIgnoreCase)))
        {
            clone.DefaultProfileId = clone.Profiles[0].Id;
        }

        return clone;
    }

    private static AgentProfile CloneProfile(AgentProfile source)
    {
        var clone = new AgentProfile
        {
            Id = string.IsNullOrWhiteSpace(source.Id) ? Guid.NewGuid().ToString("N") : source.Id,
            Name = string.IsNullOrWhiteSpace(source.Name) ? "Agent" : source.Name,
            Description = source.Description,
            SystemPrompt = source.SystemPrompt,
            RunToolsBeforeModel = source.RunToolsBeforeModel,
            IncludeToolOutputInResponse = source.IncludeToolOutputInResponse,
            Tools = new ObservableCollection<AgentTool>()
        };

        if (source.Tools is not null)
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

    private static void EnsureToolsCollection(AgentProfile? profile)
    {
        if (profile is null)
        {
            return;
        }

        if (profile.Tools is null)
        {
            profile.Tools = new ObservableCollection<AgentTool>();
        }
    }
}
