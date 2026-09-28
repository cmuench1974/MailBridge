using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using MailBridge.App.Localization;
using MailBridge.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
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
        AppWindow.Resize(new SizeInt32 { Width = 900, Height = 620 });

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

        // WM_MOUSEWHEEL is routed to the focused window, not the hovered one.
        // Keep focus inside THIS window (on activation and when the pointer
        // hovers a list) so wheel input can reach the lists at all.
        FolderList.PointerEntered += (_, _) => FocusList(FolderList);
        MessageList.PointerEntered += (_, _) => FocusList(MessageList);
        Activated += (w, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                FocusList(FolderList);
            }
        };

        // WinUI wheel workaround: intercept wheel events at the root, even
        // when an inner ScrollViewer marks them handled, and scroll manually.
        if (Content is FrameworkElement root)
        {
            root.AddHandler(UIElement.PointerWheelChangedEvent,
                new PointerEventHandler(Root_PointerWheel), handledEventsToo: true);
            root.Loaded += (_, _) => FocusList(FolderList);
            LogWheel($"handler registered on {root.GetType().Name}");
        }

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

    private void Root_PointerWheel(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement root)
        {
            return;
        }

        var point = e.GetCurrentPoint(root);
        if (point.Properties.IsHorizontalMouseWheel)
        {
            return;
        }

        var target = ContainsPointer(FolderList, point.Position) ? FolderList
            : ContainsPointer(MessageList, point.Position) ? MessageList
            : null;
        LogWheel($"wheel delta={point.Properties.MouseWheelDelta} target={(target?.Name ?? "none")} handledBefore={e.Handled}");
        if (e.Handled || target is null)
        {
            return;
        }

        var scrollViewer = FindDescendant<ScrollViewer>(target);
        if (scrollViewer is null)
        {
            return;
        }

        var offset = scrollViewer.VerticalOffset - point.Properties.MouseWheelDelta;
        scrollViewer.ChangeView(null, (float)offset, null, disableAnimation: false);
        e.Handled = true;
    }

    private void FocusList(Control list)
    {
        if (list.Focus(FocusState.Programmatic))
        {
            LogWheel($"focus set to {list.Name}");
        }
    }

    private static void LogWheel(string message)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "mailbridge-wheel.log");
            var line = $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}";
            if (File.Exists(path) && new FileInfo(path).Length > 32_768)
            {
                File.WriteAllText(path, line);
            }
            else
            {
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // diagnostics only
        }
    }

    private bool ContainsPointer(FrameworkElement element, Point position)
    {
        if (Content is not UIElement root)
        {
            return false;
        }

        var bounds = element.TransformToVisual(root)
            .TransformBounds(new Rect(new Point(0, 0), new Size(element.ActualWidth, element.ActualHeight)));
        return bounds.Contains(position);
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
