using AiCan.Contracts;
using System.Windows;
using System.Windows.Controls;

namespace AiCan.Desktop;

public partial class MainWindow : Window
{
    private readonly DesktopApiClient _apiClient = new();
    private readonly DesktopCacheService _cache = new();
    private FolderWatcherService? _watcher;
    private SessionExchangeResponse? _session;

    public MainWindow()
    {
        InitializeComponent();
        LoadCachedState();
    }

    private void LoadCachedState()
    {
        var settings = _cache.Load();
        if (settings is null)
        {
            return;
        }

        ServerUrlTextBox.Text = settings.ServerUrl;
        EmailTextBox.Text = settings.Email;
        DisplayNameTextBox.Text = settings.DisplayName;
        BotNameTextBox.Text = settings.BotName;
        DepartmentTextBox.Text = settings.Department;
        LanguageTextBox.Text = settings.PreferredLanguage;
        WatchedFolderTextBox.Text = settings.WatchedFolder;
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var authService = new M365AuthService();
            var authResult = await authService.AcquireAsync(EmailTextBox.Text);
            _session = await _apiClient.ExchangeSessionAsync(
                ServerUrlTextBox.Text,
                new SessionExchangeRequest(
                    EmailTextBox.Text,
                    DisplayNameTextBox.Text,
                    BotNameTextBox.Text,
                    DepartmentTextBox.Text,
                    authResult.AccessToken));

            await SaveProfileInternalAsync();
            MessagesListBox.Items.Add($"{_session.BotName}: Signed in for {_session.DisplayName}.");
            StatusTextBlock.Text = $"Connected as {_session.Email} with role {_session.Role}.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Sign-in failed: {ex.Message}";
        }
    }

    private async void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SaveProfileInternalAsync();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Save failed: {ex.Message}";
        }
    }

    private async Task SaveProfileInternalAsync()
    {
        if (_session is null)
        {
            throw new InvalidOperationException("Sign in first.");
        }

        var update = new UpdateAssistantProfileRequest(
            BotNameTextBox.Text,
            SelectedText(ToneComboBox),
            SelectedText(WorkStyleComboBox),
            LanguageTextBox.Text);

        var profile = await _apiClient.UpdateProfileAsync(ServerUrlTextBox.Text, _session.SessionToken, update);
        _cache.Save(new DesktopCacheState(
            ServerUrlTextBox.Text,
            EmailTextBox.Text,
            DisplayNameTextBox.Text,
            profile.BotName,
            DepartmentTextBox.Text,
            profile.PreferredLanguage,
            WatchedFolderTextBox.Text));

        StatusTextBlock.Text = $"Profile saved for {profile.BotName}.";
    }

    private void WatchFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            StatusTextBlock.Text = "Sign in before enabling the folder watcher.";
            return;
        }

        _watcher?.Dispose();
        _watcher = new FolderWatcherService(
            ServerUrlTextBox.Text,
            _session.SessionToken,
            DepartmentTextBox.Text,
            WatchedFolderTextBox.Text,
            message => Dispatcher.Invoke(() => StatusTextBlock.Text = message));

        _watcher.Start();
        StatusTextBlock.Text = $"Watching {WatchedFolderTextBox.Text}.";
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            StatusTextBlock.Text = "Sign in before chatting.";
            return;
        }

        var userMessage = MessageTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return;
        }

        MessagesListBox.Items.Add($"You: {userMessage}");
        MessageTextBox.Clear();

        try
        {
            var response = await _apiClient.ChatAsync(ServerUrlTextBox.Text, _session.SessionToken, new ChatRequest(userMessage));
            MessagesListBox.Items.Add($"{BotNameTextBox.Text}: {response.Message}");
            foreach (var citation in response.Citations)
            {
                MessagesListBox.Items.Add($"Citation: {citation.Title} -> {citation.RepositoryPath}");
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Chat failed: {ex.Message}";
        }
    }

    private static string SelectedText(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
    }
}
