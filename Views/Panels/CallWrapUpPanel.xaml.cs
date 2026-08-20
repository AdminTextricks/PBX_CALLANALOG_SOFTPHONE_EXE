using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CallAnalog.Softphone.Helpers;
using CallAnalog.Softphone.Models;
using CallAnalog.Softphone.Services;
namespace CallAnalog.Softphone.Views.Panels;

public partial class CallWrapUpPanel : UserControl, IShellPanel
{
    private const int InactivityTimeoutSeconds = 30;

    private readonly CallNoteService _callNoteService;
    private readonly CallHistoryService _callHistoryService;
    private readonly string _extension;
    private readonly CallEndedEventArgs _callInfo;
    private readonly DispatcherTimer _inactivityTimer;
    private int _selectedRating;
    private readonly List<Button> _ratingButtons = [];
    private bool _isClosing;

    public event EventHandler<ShellPanelResult>? CloseRequested;

    public CallWrapUpPanel(
        CallNoteService callNoteService,
        CallHistoryService callHistoryService,
        string extension,
        CallEndedEventArgs callInfo)
    {
        InitializeComponent();
        _callNoteService = callNoteService;
        _callHistoryService = callHistoryService;
        _extension = extension;
        _callInfo = callInfo;

        var direction = callInfo.IsOutbound ? "Outbound" : "Inbound";
        SummaryText.Text = $"{direction} call with {callInfo.RemoteParty ?? "unknown"}";
        BuildRatingButtons();

        _inactivityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(InactivityTimeoutSeconds)
        };
        _inactivityTimer.Tick += (_, _) => DismissAsSkip();

        NoteBox.TextChanged += (_, _) => ResetInactivityTimer();
        PreviewMouseDown += (_, _) => ResetInactivityTimer();
        PreviewKeyDown += (_, _) => ResetInactivityTimer();
        Loaded += (_, _) =>
        {
            ResetInactivityTimer();
            _inactivityTimer.Start();
        };
        Unloaded += (_, _) => _inactivityTimer.Stop();
    }

    public static async Task ShowAsync(
        MainWindow host,
        CallNoteService callNoteService,
        CallHistoryService callHistoryService,
        string extension,
        CallEndedEventArgs callInfo,
        CancellationToken cancellationToken = default)
    {
        if (!callInfo.WasConnected)
        {
            return;
        }

        var panel = new CallWrapUpPanel(callNoteService, callHistoryService, extension, callInfo);
        await using var registration = cancellationToken.Register(() =>
        {
            host.Dispatcher.Invoke(() => panel.DismissAsSkip());
        });

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await host.ShowShellPanelAsync<bool>(panel);
    }

    private void BuildRatingButtons()
    {
        for (var rating = 1; rating <= 5; rating++)
        {
            var value = rating;
            var button = new Button
            {
                Content = "★",
                Width = 40,
                Height = 40,
                Margin = new Thickness(2, 0, 2, 0),
                Tag = value,
                Style = (Style)FindResource("StarRatingButton")
            };
            button.Click += (_, _) =>
            {
                SelectRating(value);
                ResetInactivityTimer();
            };
            _ratingButtons.Add(button);
            RatingPanel.Children.Add(button);
        }
    }

    private void SelectRating(int rating)
    {
        _selectedRating = rating;
        for (var i = 0; i < _ratingButtons.Count; i++)
        {
            var button = _ratingButtons[i];
            var isSelected = i < rating;
            button.Foreground = isSelected
                ? (Brush)FindResource("PhoneCallGreenBrush")
                : (Brush)FindResource("PhoneTextTertiaryBrush");
            button.FontSize = isSelected ? 22 : 18;
        }
    }

    private void ResetInactivityTimer()
    {
        _inactivityTimer.Stop();
        _inactivityTimer.Start();
    }

    private void DismissAsSkip()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        _inactivityTimer.Stop();
        CloseRequested?.Invoke(this, new ShellPanelResult { Result = false });
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        SkipButton.IsEnabled = false;
        _inactivityTimer.Stop();
        StatusMessageHelper.Apply(StatusText, "Saving...", StatusMessageKind.Progress);

        try
        {
            var callId = SipCallIdHelper.Normalize(_callInfo.SipCallId);
            if (string.IsNullOrWhiteSpace(callId))
            {
                StatusMessageHelper.Apply(StatusText, "Could not resolve call ID for this call.", StatusMessageKind.Error);
                SaveButton.IsEnabled = true;
                SkipButton.IsEnabled = true;
                ResetInactivityTimer();
                return;
            }

            await _callNoteService.SaveCallNoteAsync(callId, NoteBox.Text.Trim(), _selectedRating > 0 ? _selectedRating : null);
            _isClosing = true;
            CloseRequested?.Invoke(this, new ShellPanelResult { Result = true });
        }
        catch (Exception ex)
        {
            StatusMessageHelper.Apply(StatusText, ex.Message, StatusMessageKind.Error);
            SaveButton.IsEnabled = true;
            SkipButton.IsEnabled = true;
            ResetInactivityTimer();
        }
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e) =>
        DismissAsSkip();
}
