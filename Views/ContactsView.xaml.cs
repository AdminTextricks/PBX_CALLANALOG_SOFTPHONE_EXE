using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CallAnalog.Softphone.Models;
using CallAnalog.Softphone.Services;
using CallAnalog.Softphone.Views.Panels;

namespace CallAnalog.Softphone.Views;

public partial class ContactsView : UserControl
{
    public static readonly DependencyProperty SearchHighlightQueryProperty =
        DependencyProperty.Register(
            nameof(SearchHighlightQuery),
            typeof(string),
            typeof(ContactsView),
            new PropertyMetadata(string.Empty));

    private readonly ObservableCollection<Contact> _contacts = [];
    private ContactsService? _contactsService;
    private string _extension = string.Empty;
    private int _currentPage;
    private bool _hasMore;
    private bool _isBusy;
    private bool _hasLoaded;
    private Func<string, string, string, Task<ContactFormResult?>>? _showContactForm;
    private Func<string, string, Task<bool>>? _confirm;

    public string SearchHighlightQuery
    {
        get => (string)GetValue(SearchHighlightQueryProperty);
        set => SetValue(SearchHighlightQueryProperty, value);
    }

    public event EventHandler<string>? DialRequested;
    public event EventHandler<string>? MessageRequested;

    public ContactsView()
    {
        InitializeComponent();
        ContactsList.ItemsSource = _contacts;
        EmptyState.SetContent("No contacts", "Add a contact or sync from your PBX to get started.", "IconContacts");
        SearchField.SearchRequested += async (_, _) =>
        {
            _hasLoaded = false;
            SearchHighlightQuery = SearchField.Query.Trim();
            await RefreshAsync();
        };
    }

    private void SyncSearchHighlightQuery() =>
        SearchHighlightQuery = SearchField.Query.Trim();

    public void Initialize(
        ContactsService contactsService,
        string extension,
        Func<string, string, string, Task<ContactFormResult?>>? showContactForm = null,
        Func<string, string, Task<bool>>? confirm = null)
    {
        var extensionChanged = !string.Equals(_extension, extension, StringComparison.OrdinalIgnoreCase);
        _contactsService = contactsService;
        _extension = extension;
        _showContactForm = showContactForm;
        _confirm = confirm;

        if (extensionChanged)
        {
            _hasLoaded = false;
            _currentPage = 0;
            _hasMore = false;
            _contacts.Clear();
            SearchField.Query = string.Empty;
        }
    }

    public async Task EnsureLoadedAsync()
    {
        if (_hasLoaded || _contactsService is null || string.IsNullOrWhiteSpace(_extension))
        {
            return;
        }

        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_contactsService is null || string.IsNullOrWhiteSpace(_extension))
        {
            SetStatus("Sign in to load contacts.");
            return;
        }

        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        SyncSearchHighlightQuery();
        SetLoading(true);
        SetStatus("Loading contacts...");
        try
        {
            _currentPage = 1;
            var wrapped = await _contactsService.GetContactsAsync(
                _extension,
                _currentPage,
                SearchField.NormalizedQuery);
            var result = wrapped.Result;

            _contacts.Clear();
            foreach (var contact in result.Items)
            {
                _contacts.Add(contact);
            }

            _hasMore = result.HasMore;
            UpdateLoadMore(result);
            if (wrapped.IsOffline)
            {
                SetStatus($"Showing cached contacts from {wrapped.CachedUtc:yyyy-MM-dd HH:mm} UTC — API offline.");
            }
            else
            {
                SetStatus(_contacts.Count == 0
                    ? string.IsNullOrWhiteSpace(SearchField.NormalizedQuery)
                        ? "No contacts found."
                        : string.Empty
                    : $"{result.Total} contact(s) on your PBX.");
            }
            _hasLoaded = true;
        }
        catch (Exception ex)
        {
            SetStatus($"Could not load contacts — {ex.Message}");
            EmptyState.SetContent("Could not load contacts", ex.Message, "IconContacts");
            EmptyState.Visibility = Visibility.Visible;
            ContactsList.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _isBusy = false;
            SetLoading(false);
            UpdateEmptyState();
        }
    }

    private void SetLoading(bool isLoading)
    {
        LoadingPanel.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        ContactsList.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
        if (isLoading)
        {
            EmptyState.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateEmptyState()
    {
        if (_isBusy)
        {
            return;
        }

        var showEmpty = _hasLoaded && _contacts.Count == 0;
        EmptyState.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;
        ContactsList.Visibility = showEmpty ? Visibility.Collapsed : Visibility.Visible;

        if (!showEmpty)
        {
            return;
        }

        var query = SearchField.NormalizedQuery;
        if (!string.IsNullOrWhiteSpace(query))
        {
            EmptyState.SetContent(
                "No results found",
                $"No contacts match \"{SearchField.Query.Trim()}\". Try a different name or number.",
                "IconContacts");
            return;
        }

        EmptyState.SetContent(
            "No contacts",
            "Add a contact or sync from your PBX to get started.",
            "IconContacts");
    }

    private async Task LoadMoreAsync()
    {
        if (!_hasMore || _isBusy || _contactsService is null)
        {
            return;
        }

        _isBusy = true;
        try
        {
            var nextPage = _currentPage + 1;
            var wrapped = await _contactsService.GetContactsAsync(
                _extension,
                nextPage,
                SearchField.NormalizedQuery);
            var result = wrapped.Result;

            foreach (var contact in result.Items)
            {
                _contacts.Add(contact);
            }

            _currentPage = nextPage;
            _hasMore = result.HasMore;
            UpdateLoadMore(result);
            SetStatus($"{_contacts.Count} of {result.Total} contacts loaded.");
            UpdateEmptyState();
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

    private void UpdateLoadMore(PagedResult<Contact> result)
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

    private async void AddContactButton_Click(object sender, RoutedEventArgs e)
    {
        if (_contactsService is null || _showContactForm is null)
        {
            return;
        }

        var form = await _showContactForm("Add Contact", string.Empty, string.Empty);
        if (form is null)
        {
            return;
        }

        try
        {
            SetStatus("Adding contact...");
            await _contactsService.CreateContactAsync(_extension, form.Name, form.Number);
            _hasLoaded = false;
            await RefreshAsync();
            SetStatus($"Contact {form.Name} added.");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not add contact — {ex.Message}");
        }
    }

    private async void EditContactButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Contact contact } || _contactsService is null || _showContactForm is null)
        {
            return;
        }

        var form = await _showContactForm("Edit Contact", contact.Name, contact.Number);
        if (form is null)
        {
            return;
        }

        try
        {
            SetStatus("Saving contact...");
            await _contactsService.UpdateContactAsync(_extension, contact.Id, form.Name, form.Number);
            _hasLoaded = false;
            await RefreshAsync();
            SetStatus($"Contact {form.Name} updated.");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not update contact — {ex.Message}");
        }
    }

    private async void DeleteContactButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Contact contact } || _contactsService is null)
        {
            return;
        }

        if (_confirm is not null)
        {
            var confirmed = await _confirm("Delete contact?", $"Delete {contact.Name} from your contact list?");
            if (!confirmed)
            {
                return;
            }
        }

        try
        {
            SetStatus("Deleting contact...");
            await _contactsService.DeleteContactAsync(contact.Id);
            _contacts.Remove(contact);
            SetStatus($"Deleted {contact.Name}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not delete contact — {ex.Message}");
        }
    }

    private void DialContactButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Contact contact } && !string.IsNullOrWhiteSpace(contact.Number))
        {
            DialRequested?.Invoke(this, contact.Number);
        }
    }

    private void CopyContactButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Contact contact } && !string.IsNullOrWhiteSpace(contact.Number))
        {
            Clipboard.SetText(contact.Number);
            SetStatus($"Copied {contact.Number}");
        }
    }

    private void MessageContactButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Contact contact } && !string.IsNullOrWhiteSpace(contact.Number))
        {
            MessageRequested?.Invoke(this, contact.Number);
        }
    }
}
