using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CallAnalog.Softphone.Helpers;
using CallAnalog.Softphone.Models;
using CallAnalog.Softphone.Services;

namespace CallAnalog.Softphone.Views;

public partial class HistoryView : UserControl
{
    public static readonly DependencyProperty SearchHighlightQueryProperty =
        DependencyProperty.Register(
            nameof(SearchHighlightQuery),
            typeof(string),
            typeof(HistoryView),
            new PropertyMetadata(string.Empty));

    private readonly ObservableCollection<HistoryListItem> _displayItems = [];
    private readonly List<CallRecord> _allCalls = [];
    private CallHistoryService? _callHistoryService;
    private string _extension = string.Empty;
    private CallHistoryFilter _activeFilter = CallHistoryFilter.All;
    private string _submittedSearch = string.Empty;
    private int _currentPage;
    private int _total;
    private bool _hasMore;
    private bool _isBusy;
    private bool _hasLoaded;
    private string? _lastError;

    public string SearchHighlightQuery
    {
        get => (string)GetValue(SearchHighlightQueryProperty);
        set => SetValue(SearchHighlightQueryProperty, value);
    }

    public event EventHandler<string>? DialRequested;
    public event EventHandler<string>? MessageRequested;

    public HistoryView()
    {
        InitializeComponent();
        HistoryList.ItemsSource = _displayItems;
    }

    public void Initialize(CallHistoryService callHistoryService, string extension)
    {
        var extensionChanged = !string.Equals(_extension, extension, StringComparison.OrdinalIgnoreCase);
        _callHistoryService = callHistoryService;
        _extension = extension;

        if (extensionChanged)
        {
            _hasLoaded = false;
            _currentPage = 0;
            _hasMore = false;
            _displayItems.Clear();
            _allCalls.Clear();
            _submittedSearch = string.Empty;
            HistorySearchBox.Text = string.Empty;
        }
    }

    public async Task EnsureLoadedAsync()
    {
        if (_hasLoaded || _callHistoryService is null || string.IsNullOrWhiteSpace(_extension))
        {
            return;
        }

        await RefreshAsync();
    }

    public async Task NavigateWithFilterAsync(CallHistoryFilter filter)
    {
        _activeFilter = filter;
        UpdateFilterChips();
        if (!_hasLoaded)
        {
            await EnsureLoadedAsync();
            return;
        }

        ApplyFilterToDisplay();
        UpdateStatusFromFilter();
    }

    public async Task RefreshAsync()
    {
        if (_callHistoryService is null || string.IsNullOrWhiteSpace(_extension))
        {
            SetStatus("Sign in to load call history.");
            return;
        }

        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        _lastError = null;
        SearchHighlightQuery = _submittedSearch;
        SetLoading(_allCalls.Count == 0);
        SetStatus("Loading call history...");
        try
        {
            _currentPage = 1;
            var wrapped = await _callHistoryService.GetCallHistoryAsync(
                _extension,
                _currentPage,
                string.IsNullOrWhiteSpace(_submittedSearch) ? null : _submittedSearch);
            var result = wrapped.Result;

            _allCalls.Clear();
            _allCalls.AddRange(result.Items);

            _total = result.Total;
            _hasMore = result.HasMore;
            UpdateLoadMore(result);
            ApplyFilterToDisplay();
            if (wrapped.IsOffline)
            {
                SetStatus($"Showing cached history from {wrapped.CachedUtc:yyyy-MM-dd HH:mm} UTC — API offline.");
            }
            else
            {
                UpdateStatusFromFilter();
            }

            _hasLoaded = true;
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            SetStatus($"Could not load call history — {ex.Message}");
            ErrorText.Text = ex.Message;
            ErrorPanel.Visibility = Visibility.Visible;
            HistoryList.Visibility = Visibility.Collapsed;
            EmptyStatePanel.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _isBusy = false;
            SetLoading(false);
            UpdateEmptyState();
        }
    }

    private async Task LoadMoreAsync()
    {
        if (!_hasMore || _isBusy || _callHistoryService is null)
        {
            return;
        }

        _isBusy = true;
        try
        {
            var nextPage = _currentPage + 1;
            var wrapped = await _callHistoryService.GetCallHistoryAsync(
                _extension,
                nextPage,
                string.IsNullOrWhiteSpace(_submittedSearch) ? null : _submittedSearch);
            var result = wrapped.Result;

            var existing = _allCalls.Select(c => c.Id).ToHashSet();
            _allCalls.AddRange(result.Items.Where(c => !existing.Contains(c.Id)));
            _currentPage = nextPage;
            _total = result.Total;
            _hasMore = result.HasMore;
            UpdateLoadMore(result);
            ApplyFilterToDisplay();
            SetStatus($"{FilteredCount()} calls shown ({_allCalls.Count} loaded).");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not load more — {ex.Message}");
        }
        finally
        {
            _isBusy = false;
        }
    }

    private void ApplyFilterToDisplay()
    {
        _displayItems.Clear();
        var filtered = _allCalls.Where(c => CallRecordAnalytics.MatchesFilter(c, _activeFilter)).ToList();
        foreach (var item in HistoryListItem.GroupByDate(filtered))
        {
            _displayItems.Add(item);
        }

        UpdateEmptyState();
    }

    public void UpsertLiveCall(CallRecord live)
    {
        if (!_hasLoaded)
        {
            return;
        }

        var existingIndex = _allCalls.FindIndex(c => c.Id == live.Id);
        if (existingIndex < 0)
        {
            _allCalls.Insert(0, live);
            _total++;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(_allCalls[existingIndex].CallDate))
            {
                live.CallDate = _allCalls[existingIndex].CallDate;
            }

            _allCalls[existingIndex] = live;
        }

        if (!CallRecordAnalytics.MatchesFilter(live, _activeFilter))
        {
            ApplyFilterToDisplay();
            UpdateStatusFromFilter();
            return;
        }

        var displayIndex = -1;
        for (var i = 0; i < _displayItems.Count; i++)
        {
            if (!_displayItems[i].IsHeader && _displayItems[i].Record?.Id == live.Id)
            {
                displayIndex = i;
                break;
            }
        }

        if (displayIndex >= 0)
        {
            _displayItems[displayIndex] = new HistoryListItem { IsHeader = false, Record = live };
        }
        else
        {
            ApplyFilterToDisplay();
        }

        UpdateStatusFromFilter();
        UpdateEmptyState();
    }

    private int FilteredCount() => _displayItems.Count(i => !i.IsHeader);

    private void SetLoading(bool isLoading)
    {
        HistorySkeleton.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
        if (isLoading)
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateEmptyState()
    {
        if (_isBusy)
        {
            return;
        }

        ErrorPanel.Visibility = string.IsNullOrWhiteSpace(_lastError) ? Visibility.Collapsed : Visibility.Visible;

        var showEmpty = _hasLoaded && FilteredCount() == 0 && string.IsNullOrWhiteSpace(_lastError);
        EmptyStatePanel.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = showEmpty || !string.IsNullOrWhiteSpace(_lastError)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (!showEmpty)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_submittedSearch))
        {
            EmptyTitleText.Text = "No results found";
            EmptySubtitleText.Text = $"No calls match \"{_submittedSearch}\". Try a different number or name.";
            return;
        }

        EmptyTitleText.Text = _activeFilter switch
        {
            CallHistoryFilter.Answered => "No answered calls",
            CallHistoryFilter.Missed => "No missed calls",
            _ => "No calls yet"
        };
        EmptySubtitleText.Text = _activeFilter switch
        {
            CallHistoryFilter.Answered => "Answered calls will appear here.",
            CallHistoryFilter.Missed => "Missed and busy calls will appear here.",
            _ => "Your call history will show up here once you make or receive calls."
        };
    }

    private void UpdateStatusFromFilter()
    {
        if (FilteredCount() == 0)
        {
            if (!string.IsNullOrWhiteSpace(_submittedSearch))
            {
                SetStatus($"No calls match \"{_submittedSearch}\".");
                return;
            }

            SetStatus(_activeFilter switch
            {
                CallHistoryFilter.Answered => "No answered calls found.",
                CallHistoryFilter.Missed => "No missed calls found.",
                _ => "No calls found."
            });
            return;
        }

        SetStatus($"{FilteredCount()} of {_total} calls shown.");
    }

    private void UpdateFilterChips()
    {
        FilterAllButton.Style = ChipStyle(_activeFilter == CallHistoryFilter.All);
        FilterAnsweredButton.Style = ChipStyle(_activeFilter == CallHistoryFilter.Answered);
        FilterMissedButton.Style = ChipStyle(_activeFilter == CallHistoryFilter.Missed);
    }

    private Style ChipStyle(bool selected) =>
        (Style)FindResource(selected ? "HistoryFilterChipActive" : "HistoryFilterChip");

    private void UpdateLoadMore(PagedResult<CallRecord> result)
    {
        if (_hasMore)
        {
            LoadMoreButton.Content = $"{result.LoadedCount} of {result.Total} — Load more";
            LoadMoreButton.Visibility = Visibility.Visible;
        }
        else
        {
            LoadMoreButton.Visibility = Visibility.Collapsed;
        }
    }

    private void SetStatus(string message) => StatusText.Text = message;

    private void ApplyFilter(CallHistoryFilter filter)
    {
        _activeFilter = filter;
        UpdateFilterChips();
        ApplyFilterToDisplay();
        UpdateStatusFromFilter();
    }

    private async Task SubmitSearchAsync()
    {
        _submittedSearch = HistorySearchBox.Text.Trim();
        SearchHighlightQuery = _submittedSearch;
        _hasLoaded = false;
        await RefreshAsync();
    }

    private async void HistorySearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SubmitSearchAsync();
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _hasLoaded = false;
        await RefreshAsync();
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e) =>
        await SubmitSearchAsync();

    private async void LoadMoreButton_Click(object sender, RoutedEventArgs e) =>
        await LoadMoreAsync();

    private void FilterAllButton_Click(object sender, RoutedEventArgs e) =>
        ApplyFilter(CallHistoryFilter.All);

    private void FilterAnsweredButton_Click(object sender, RoutedEventArgs e) =>
        ApplyFilter(CallHistoryFilter.Answered);

    private void FilterMissedButton_Click(object sender, RoutedEventArgs e) =>
        ApplyFilter(CallHistoryFilter.Missed);

    private async void ClearFilterButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyFilter(CallHistoryFilter.All);
        await Task.CompletedTask;
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        _hasLoaded = false;
        _lastError = null;
        await RefreshAsync();
    }

    private void CallRecordRow_DialRequested(object? sender, string number) =>
        DialRequested?.Invoke(this, number);

    private void MessageRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CallRecord record } && !string.IsNullOrWhiteSpace(record.DialNumber))
        {
            MessageRequested?.Invoke(this, record.DialNumber);
        }
    }
}
