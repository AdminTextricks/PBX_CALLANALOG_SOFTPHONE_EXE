using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CallAnalog.Softphone.Helpers;
using CallAnalog.Softphone.Models;
using CallAnalog.Softphone.Services;

namespace CallAnalog.Softphone.Views;

public partial class DashboardView : UserControl
{
    private const int MaxDashboardRecentCalls = 10;
    private const int MaxHistoryPagesForToday = 10;

    private CallHistoryService? _callHistoryService;
    private UserSettingsService? _userSettingsService;
    private string _extension = string.Empty;
    private bool _isRefreshing;
    private List<CallRecord> _cachedRecentCalls = [];
    private List<CallRecord> _todayCalls = [];

    public event EventHandler<string>? ComingSoonRequested;
    public event EventHandler<string>? VoicemailDialRequested;
    public event EventHandler? OpenDialpadRequested;
    public event EventHandler<CallHistoryFilter>? ViewHistoryFilterRequested;
    public event EventHandler<string>? DialRecentRequested;

    public DashboardView()
    {
        InitializeComponent();
        RecentCallsList.ItemsSource = new ObservableCollection<CallRecord>();
    }

    public void Initialize(CallHistoryService callHistoryService, UserSettingsService userSettingsService, string extension)
    {
        _callHistoryService = callHistoryService;
        _userSettingsService = userSettingsService;
        _extension = extension;
        ApplyToggleStates();
    }

    public void SetExtension(string extension)
    {
        _extension = extension;
        WelcomeText.Text = $"{ThemeManager.GetGreeting()}, Extension {extension}";
    }

    public void SetDndOverlayVisible(bool visible) =>
        DndOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void ApplyRecentCallsToList()
    {
        if (RecentCallsList.ItemsSource is not ObservableCollection<CallRecord> recent)
        {
            return;
        }

        recent.Clear();
        foreach (var call in _cachedRecentCalls.Take(MaxDashboardRecentCalls))
        {
            recent.Add(call);
        }

        var isEmpty = recent.Count == 0;
        RecentCallsEmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        RecentCallsScroll.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        RecentCallsList.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyToggleStates()
    {
        if (_userSettingsService is null)
        {
            return;
        }

        var settings = _userSettingsService.Settings;
        UpdateDndPill(settings.DndEnabled);
        UpdateAutoAnswerPill(settings.AutoAnswerEnabled);
    }

    private void UpdateDndPill(bool isActive)
    {
        DndButton.Style = (Style)FindResource(isActive ? "DashboardDndPillActive" : "DashboardDndPill");
        DndStatusText.Text = isActive ? "ON" : "OFF";
        DndStatusChip.Background = isActive
            ? (Brush)FindResource("PhoneHangupRedBrush")
            : (Brush)FindResource("PhoneChipBgBrush");
        DndStatusText.Foreground = isActive
            ? Brushes.White
            : (Brush)FindResource("PhoneTextSecondaryBrush");
    }

    private void UpdateAutoAnswerPill(bool isActive)
    {
        AutoAnswerButton.Style = (Style)FindResource(isActive ? "DashboardAutoAnswerPillActive" : "DashboardAutoAnswerPill");
        AutoAnswerStatusText.Text = isActive ? "ON" : "OFF";
        AutoAnswerStatusChip.Background = isActive
            ? (Brush)FindResource("PhoneCallGreenBrush")
            : (Brush)FindResource("PhoneChipBgBrush");
        AutoAnswerStatusText.Foreground = isActive
            ? Brushes.White
            : (Brush)FindResource("PhoneTextSecondaryBrush");
    }

    private async Task SaveToggleStatesAsync()
    {
        if (_userSettingsService is null)
        {
            return;
        }

        var settings = _userSettingsService.Settings;
        await _userSettingsService.SaveDashboardTogglesAsync(settings.DndEnabled, settings.AutoAnswerEnabled);
    }

    public async Task RefreshAsync()
    {
        ApplyToggleStates();

        if (_isRefreshing || _callHistoryService is null || string.IsNullOrWhiteSpace(_extension))
        {
            return;
        }

        _isRefreshing = true;
        SetRecentCallsLoading(true);
        try
        {
            var firstPageWrapped = await _callHistoryService.GetCallHistoryAsync(_extension, 1);
            var firstPage = firstPageWrapped.Result;
            _cachedRecentCalls = firstPage.Items.ToList();
            _todayCalls = await LoadTodayCallsAsync(firstPage);
            ApplyTodayCounts();

            ApplyRecentCallsToList();
        }
        catch
        {
            MadeCountText.Text = "—";
            AnsweredCountText.Text = "—";
            MissedCountText.Text = "—";
        }
        finally
        {
            _isRefreshing = false;
            SetRecentCallsLoading(false);
        }
    }

    private void SetRecentCallsLoading(bool isLoading)
    {
        RecentCallsSkeleton.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        RecentCallsList.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
        if (isLoading)
        {
            RecentCallsEmptyState.Visibility = Visibility.Collapsed;
            RecentCallsScroll.Visibility = Visibility.Collapsed;
        }
    }

    private async Task<List<CallRecord>> LoadTodayCallsAsync(PagedResult<CallRecord> firstPage)
    {
        var todayCalls = firstPage.Items.Where(CallRecordAnalytics.IsToday).ToList();

        if (!firstPage.HasMore || firstPage.Items.Count == 0)
        {
            return todayCalls;
        }

        if (firstPage.Items.All(c => !CallRecordAnalytics.IsToday(c)))
        {
            return todayCalls;
        }

        var page = 2;
        while (page <= MaxHistoryPagesForToday)
        {
            var wrapped = await _callHistoryService!.GetCallHistoryAsync(_extension, page);
            var result = wrapped.Result;
            if (result.Items.Count == 0)
            {
                break;
            }

            foreach (var call in result.Items)
            {
                if (CallRecordAnalytics.IsToday(call))
                {
                    todayCalls.Add(call);
                }
            }

            if (!result.HasMore || !CallRecordAnalytics.IsToday(result.Items[^1]))
            {
                break;
            }

            page++;
        }

        return todayCalls;
    }

    private async void DndButton_Click(object sender, RoutedEventArgs e) =>
        await ToggleDndAsync();

    private async void TurnOffDndButton_Click(object sender, RoutedEventArgs e)
    {
        if (_userSettingsService?.Settings.DndEnabled != true)
        {
            SetDndOverlayVisible(false);
            return;
        }

        await ToggleDndAsync();
    }

    private async Task ToggleDndAsync()
    {
        if (_userSettingsService is null)
        {
            return;
        }

        _userSettingsService.Settings.DndEnabled = !_userSettingsService.Settings.DndEnabled;
        ApplyToggleStates();
        SetDndOverlayVisible(_userSettingsService.Settings.DndEnabled);
        App.TrayIcon.SetDndEnabled(_userSettingsService.Settings.DndEnabled);
        await SaveToggleStatesAsync();
    }

    private async void AutoAnswerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_userSettingsService is null)
        {
            return;
        }

        _userSettingsService.Settings.AutoAnswerEnabled = !_userSettingsService.Settings.AutoAnswerEnabled;
        ApplyToggleStates();
        await SaveToggleStatesAsync();
    }

    private void SmsButton_Click(object sender, RoutedEventArgs e) =>
        ComingSoonRequested?.Invoke(this, "SMS");

    private void VoicemailButton_Click(object sender, RoutedEventArgs e)
    {
        var code = _userSettingsService?.Settings.VoicemailDialCode;
        if (string.IsNullOrWhiteSpace(code))
        {
            code = "*97";
        }

        VoicemailDialRequested?.Invoke(this, code);
    }

    private void DialpadButton_Click(object sender, RoutedEventArgs e) =>
        OpenDialpadRequested?.Invoke(this, EventArgs.Empty);

    private void MadeButton_Click(object sender, RoutedEventArgs e) =>
        ViewHistoryFilterRequested?.Invoke(this, CallHistoryFilter.All);

    private void AnsweredButton_Click(object sender, RoutedEventArgs e) =>
        ViewHistoryFilterRequested?.Invoke(this, CallHistoryFilter.Answered);

    private void MissedButton_Click(object sender, RoutedEventArgs e) =>
        ViewHistoryFilterRequested?.Invoke(this, CallHistoryFilter.Missed);

    private void CallRecordRow_DialRequested(object? sender, string number) =>
        DialRecentRequested?.Invoke(this, number);

    public void UpsertLiveCall(CallRecord live)
    {
        ReplaceOrInsert(_cachedRecentCalls, live);
        ReplaceOrInsert(_todayCalls, live);
        ApplyTodayCounts();

        if (RecentCallsList.ItemsSource is not ObservableCollection<CallRecord> recent)
        {
            ApplyRecentCallsToList();
            return;
        }

        var existingIndex = -1;
        for (var i = 0; i < recent.Count; i++)
        {
            if (recent[i].Id == live.Id)
            {
                existingIndex = i;
                break;
            }
        }

        if (existingIndex >= 0)
        {
            if (!string.IsNullOrWhiteSpace(recent[existingIndex].CallDate))
            {
                live.CallDate = recent[existingIndex].CallDate;
            }

            live.ContactName = CallRecordAnalytics.PreferContactName(
                recent[existingIndex].ContactName,
                live.ContactName,
                live.DialNumber);

            recent[existingIndex] = live;
        }
        else
        {
            recent.Insert(0, live);
            while (recent.Count > MaxDashboardRecentCalls)
            {
                recent.RemoveAt(recent.Count - 1);
            }
        }

        var isEmpty = recent.Count == 0;
        RecentCallsEmptyState.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        RecentCallsScroll.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        RecentCallsList.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
        RecentCallsSkeleton.Visibility = Visibility.Collapsed;
    }

    private void ApplyTodayCounts()
    {
        MadeCountText.Text = _todayCalls.Count(c => c.IsOutbound).ToString();
        AnsweredCountText.Text = _todayCalls.Count(c => !c.IsOutbound && CallRecordAnalytics.IsAttended(c)).ToString();
        MissedCountText.Text = _todayCalls.Count(CallRecordAnalytics.IsMissed).ToString();
    }

    private static void ReplaceOrInsert(List<CallRecord> list, CallRecord live)
    {
        var index = list.FindIndex(c => c.Id == live.Id);
        if (index >= 0)
        {
            if (!string.IsNullOrWhiteSpace(list[index].CallDate))
            {
                live.CallDate = list[index].CallDate;
            }

            live.ContactName = CallRecordAnalytics.PreferContactName(
                list[index].ContactName,
                live.ContactName,
                live.DialNumber);

            list[index] = live;
            return;
        }

        list.Insert(0, live);
    }
}
