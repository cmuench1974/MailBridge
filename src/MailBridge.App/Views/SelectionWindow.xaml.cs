using System.Collections.ObjectModel;
using System.ComponentModel;
using MailBridge.App.Localization;
using MailBridge.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace MailBridge.App.Views;

/// <summary>
/// Picker window for backup/restore selection. Opens immediately; if no
/// folders are loaded yet it runs the owner's reload action itself (with
/// its own progress ring and status line). Edits the shared
/// SelectableFolder items in place, so the owning view model's
/// BuildSelection() and SelectionSummary see every change immediately.
/// Cancel restores the state captured when the window was opened.
/// </summary>
public sealed partial class SelectionWindow : Window
{
    private readonly ObservableCollection<SelectableFolder> _folders;
    private readonly Func<SelectableFolder, Task>? _loadFolderMessages;
    private readonly Func<Task>? _reload;
    private readonly Func<string> _buildSummary;
    private readonly Func<string>? _statusProvider;
    private readonly Dictionary<INotifyPropertyChanged, PropertyChangedEventHandler> _subscriptions = new();
    private readonly List<(SelectableFolder Folder, bool? FolderState, List<(SelectableMessage Message, bool State)> Messages)> _snapshot = new();
    private bool _accepted;

    public SelectionWindow(
        string title,
        ObservableCollection<SelectableFolder> folders,
        Func<SelectableFolder, Task>? loadFolderMessages,
        Func<Task>? reload,
        Func<string> buildSummary,
        Func<string>? statusProvider = null)
    {
        InitializeComponent();
        Title = title;
        AppWindow.Resize(new SizeInt32 { Width = 820, Height = 560 });

        _folders = folders;
        _loadFolderMessages = loadFolderMessages;
        _reload = reload;
        _buildSummary = buildSummary;
        _statusProvider = statusProvider;

        FolderList.ItemsSource = _folders;
        ReloadButton.Content = Strings.Get("common.reload");
        CancelButton.Content = Strings.Get("common.cancel");
        OkButton.Content = Strings.Get("common.ok");
        ReloadButton.Visibility = reload is null ? Visibility.Collapsed : Visibility.Visible;

        TakeSnapshot();
        HookFolders();
        RefreshSummary();

        if (_folders.Count == 0 && _reload is not null)
        {
            _ = ReloadAsync();
        }

        Closed += (_, _) =>
        {
            if (!_accepted)
            {
                RestoreSnapshot();
            }

            UnhookAll();
        };
    }

    private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FolderList.SelectedItem is not SelectableFolder folder)
        {
            MessageList.ItemsSource = null;
            return;
        }

        MessageList.ItemsSource = folder.Messages;
        _ = ShowFolderMessagesAsync(folder);
    }

    private async Task ShowFolderMessagesAsync(SelectableFolder folder)
    {
        if (folder.MessagesLoaded || _loadFolderMessages is null)
        {
            return;
        }

        LoadRing.IsActive = true;
        try
        {
            await _loadFolderMessages(folder);
        }
        finally
        {
            LoadRing.IsActive = false;
            HookMessages(folder);
            RefreshSummary();
        }
    }

    private async void ReloadButton_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        if (_reload is null)
        {
            return;
        }

        ReloadButton.IsEnabled = false;
        LoadRing.IsActive = true;
        try
        {
            FolderList.SelectedItem = null;
            MessageList.ItemsSource = null;
            await _reload();
            HookFolders();
            SummaryText.Text = _folders.Count > 0
                ? _buildSummary()
                : _statusProvider?.Invoke() ?? _buildSummary();
        }
        finally
        {
            LoadRing.IsActive = false;
            ReloadButton.IsEnabled = true;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _accepted = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void TakeSnapshot()
    {
        _snapshot.Clear();
        foreach (var folder in _folders)
        {
            _snapshot.Add((folder, folder.IsChecked, folder.Messages.Select(m => (m, m.IsChecked)).ToList()));
        }
    }

    private void RestoreSnapshot()
    {
        foreach (var (folder, folderState, messages) in _snapshot)
        {
            folder.IsChecked = folderState;
            foreach (var (message, state) in messages)
            {
                message.IsChecked = state;
            }
        }
    }

    private void HookFolders()
    {
        UnhookAll();
        foreach (var folder in _folders)
        {
            Hook(folder);
            if (folder.MessagesLoaded)
            {
                HookMessages(folder);
            }
        }
    }

    private void HookMessages(SelectableFolder folder)
    {
        foreach (var message in folder.Messages)
        {
            Hook(message);
        }
    }

    private void Hook(INotifyPropertyChanged source)
    {
        if (_subscriptions.ContainsKey(source))
        {
            return;
        }

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName is null or "IsChecked")
            {
                RefreshSummary();
            }
        };
        _subscriptions[source] = handler;
        source.PropertyChanged += handler;
    }

    private void UnhookAll()
    {
        foreach (var (source, handler) in _subscriptions)
        {
            source.PropertyChanged -= handler;
        }

        _subscriptions.Clear();
    }

    private void RefreshSummary() => SummaryText.Text = _buildSummary();
}
