using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SnapDescribe.App.ViewModels;

namespace SnapDescribe.App.Views;

public partial class AgentSettingsWindow : Window
{
    private readonly AgentSettingsViewModel? _viewModel;

    public AgentSettingsWindow()
    {
        InitializeComponent();
    }

    public AgentSettingsWindow(AgentSettingsViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.Saved += OnViewModelSaved;
    }

    private void OnViewModelSaved(object? sender, EventArgs e)
    {
        Close();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Saved -= OnViewModelSaved;
        }

        base.OnClosed(e);
    }
}
