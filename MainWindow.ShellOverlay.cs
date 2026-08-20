using System.Windows;
using System.Windows.Controls;

namespace CallAnalog.Softphone;

public partial class MainWindow
{
    private TaskCompletionSource<object?>? _overlayCompletion;
    private IShellPanel? _activeShellPanel;
    private EventHandler<ShellPanelResult>? _activeShellPanelHandler;
    private CancellationTokenSource? _wrapUpCts;
    private readonly HashSet<string> _dismissedWrapUpCallIds = new(StringComparer.Ordinal);

    internal async Task<T?> ShowShellPanelAsync<T>(UserControl panel)
    {
        var tcs = new TaskCompletionSource<object?>();
        _overlayCompletion = tcs;

        void Handler(object? sender, ShellPanelResult e)
        {
            DismissActiveShellPanel(e.Result);
        }

        if (panel is IShellPanel shellPanel)
        {
            _activeShellPanel = shellPanel;
            _activeShellPanelHandler = Handler;
            shellPanel.CloseRequested += Handler;
        }

        ShellOverlayContent.Content = panel;
        ShellOverlay.Visibility = Visibility.Visible;

        var result = await tcs.Task;
        return result is T typed ? typed : default;
    }

    internal void DismissActiveShellPanel(object? result = null)
    {
        if (_activeShellPanel is not null && _activeShellPanelHandler is not null)
        {
            _activeShellPanel.CloseRequested -= _activeShellPanelHandler;
            _activeShellPanel = null;
            _activeShellPanelHandler = null;
        }

        _overlayCompletion?.TrySetResult(result);
        _overlayCompletion = null;
        HideShellPanelInternal();
    }

    internal void ShowShellPanel(UserControl panel)
    {
        ShellOverlayContent.Content = panel;
        ShellOverlay.Visibility = Visibility.Visible;
    }

    internal void HideShellPanel() => HideShellPanelInternal();

    private void HideShellPanelInternal()
    {
        ShellOverlayContent.Content = null;
        ShellOverlay.Visibility = Visibility.Collapsed;
    }

    internal async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Yes", string cancelText = "Cancel")
    {
        var panel = new Views.Panels.ConfirmPanel(title, message, confirmText, cancelText);
        var result = await ShowShellPanelAsync<bool>(panel);
        return result == true;
    }
}

public interface IShellPanel
{
    event EventHandler<ShellPanelResult>? CloseRequested;
}

public sealed class ShellPanelResult : EventArgs
{
    public object? Result { get; init; }
}
