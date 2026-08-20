using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    private int _currentPage;
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
        EmptyState.SetContent("No calls yet", "Your call history will show up here once you make or receive calls.", "IconHistory");
        SearchField.SearchRequested += async (_, _) =>
        {
            _hasLoaded = false;
            SearchHighlightQuery = SearchField.Query.Trim();
            await RefreshAsync();
        };
    }

    private void SyncSearchHighlightQuery() =>
        SearchHighlightQuery = SearchField.Query.Trim();

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
            SearchField.Query = string.Empty;
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
        _hasLoaded = false;
        UpdateFilterUi();
        await EnsureLoadedAsync();
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
        SyncSearchHighlightQuery();
        SetLoading(true);
        SetStatus("Loading call history...");
        try
        {
            _currentPage = 1;
            var wrapped = await _callHistoryService.GetCallHistoryAsync(
                _extension,
                _currentPage,
                SearchField.NormalizedQuery);
            var result = wrapped.Result;

            _allCalls.Clear();
            _allCalls.AddRange(result.Items);

            _hasMore = result.HasMore;
            UpdateLoadMore(result);
            ApplyFilterToDisplay();
        UpdateStickyDateHeader();
            if (wrapped.IsOffline)
            {
                SetStatus($"Showing cached history from {wrapped.CachedUtc:yyyy-MM-dd HH:mm} UTC — API offline.");
            }
            else
            {
                SetStatusMessage(result);
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
            EmptyState.Visibility = Visibility.Collapsed;
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
                SearchField.NormalizedQuery);
            var result = wrapped.Result;

            _allCalls.AddRange(result.Items);
            _currentPage = nextPage;
            _hasMore = result.HasMore;
            UpdateLoadMore(result);
            ApplyFilterToDisplay();
            UpdateStickyDateHeader();
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
        UpdateStickyDateHeader();
    }

    private void HistoryScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e) =>
        UpdateStickyDateHeader();

    private void UpdateStickyDateHeader()
    {
        if (_displayItems.Count == 0 || HistoryList.Visibility != Visibility.Visible)
        {
            StickyDateHeaderBar.Visibility = Visibility.Collapsed;
            return;
        }

        var scrollOffset = HistoryScrollViewer.VerticalOffset;
        if (scrollOffset < 8)
        {
            StickyDateHeaderBar.Visibility = Visibility.Collapsed;
            return;
        }

        var viewportTop = scrollOffset + 4;
        string? currentHeader = null;

        for (var i = 0; i < HistoryList.Items.Count; i++)
        {
            if (HistoryList.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
            {
                continue;
            }

            if (container.DataContext is not HistoryListItem { IsHeader: true } headerItem)
            {
                continue;
            }

            var top = container.TranslatePoint(new Point(0, 0), HistoryList).Y;
            if (top <= viewportTop)
            {
                currentHeader = headerItem.HeaderText;
            }
            else
            {
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(currentHeader))
        {
            StickyDateHeaderBar.Visibility = Visibility.Collapsed;
            return;
        }

        StickyDateHeaderText.Text = currentHeader;
        StickyDateHeaderBar.Visibility = Visibility.Visible;
    }

    private int FilteredCount() => _displayItems.Count(i => !i.IsHeader);

    private void SetLoading(bool isLoading)
    {
        HistorySkeleton.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
        if (isLoading)
        {
            EmptyState.Visibility = Visibility.Collapsed;
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
        EmptyState.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = showEmpty || !string.IsNullOrWhiteSpace(_lastError)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (!showEmpty)
        {
            return;
        }

        var query = SearchField.NormalizedQuery;
        if (!string.IsNullOrWhiteSpace(query))
        {
            EmptyState.SetContent(
                "No results found",
                $"No calls match \"{SearchField.Query.Trim()}\". Try a different number or name.",
                "IconHistory");
            return;
        }

        EmptyState.SetContent(
            _activeFilter switch
            {
                CallHistoryFilter.Answered => "No answered calls",
                CallHistoryFilter.Missed => "No missed calls",
                _ => "No calls yet"
            },
            _activeFilter switch
            {
                CallHistoryFilter.Answered => "Answered calls will appear here.",
                CallHistoryFilter.Missed => "Missed and busy calls will appear here.",
                _ => "Your call history will show up here once you make or receive calls."
            },
            "IconHistory");
    }

    private void SetStatusMessage(PagedResult<CallRecord> result)
    {
        if (FilteredCount() == 0)
        {
            var query = SearchField.NormalizedQuery;
            if (!string.IsNullOrWhiteSpace(query))
            {
                SetStatus($"No calls match \"{SearchField.Query.Trim()}\".");
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

        SetStatus($"{FilteredCount()} of {result.Total} calls shown.");
    }

    private void UpdateFilterUi()
    {
        HeaderTitleText.Text = CallRecordAnalytics.GetFilterTitle(_activeFilter);
        ClearFilterButton.Visibility = _activeFilter == CallHistoryFilter.All
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

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

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _hasLoaded = false;
        await RefreshAsync();
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        _hasLoaded = false;
        await RefreshAsync();
    }

    private async void LoadMoreButton_Click(object sender, RoutedEventArgs e) =>
        await LoadMoreAsync();

    private async void ClearFilterButton_Click(object sender, RoutedEventArgs e)
    {
        _activeFilter = CallHistoryFilter.All;
        _hasLoaded = false;
        UpdateFilterUi();
        await RefreshAsync();
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        _hasLoaded = false;
        _lastError = null;
        await RefreshAsync();
    }

    private void DialRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CallRecord record } && !string.IsNullOrWhiteSpace(record.DialNumber))
        {
            DialRequested?.Invoke(this, record.DialNumber);
        }
    }

    private void CopyRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CallRecord record } && !string.IsNullOrWhiteSpace(record.DialNumber))
        {
            Clipboard.SetText(record.DialNumber);
            SetStatus($"Copied {record.DialNumber}");
        }
    }

    private void MessageRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: CallRecord record } && !string.IsNullOrWhiteSpace(record.DialNumber))
        {
            MessageRequested?.Invoke(this, record.DialNumber);
        }
    }
}
