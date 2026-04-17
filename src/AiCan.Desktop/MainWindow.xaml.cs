using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AiCan.Contracts;
using Microsoft.Win32;

namespace AiCan.Desktop;

public sealed class ConversationEntry
{
    public required string Speaker { get; init; }
    public required string Text { get; init; }
    public required string Meta { get; init; }
    public required HorizontalAlignment Alignment { get; init; }
    public required Brush BubbleBrush { get; init; }
    public required Brush BorderBrush { get; init; }
    public required Brush TextBrush { get; init; }
    public required Brush HeaderBrush { get; init; }
    public required Brush MetaBrush { get; init; }
}

public partial class MainWindow : Window
{
    private static readonly Brush AssistantBubble = new SolidColorBrush(Color.FromRgb(236, 248, 244));
    private static readonly Brush AssistantBorder = new SolidColorBrush(Color.FromRgb(148, 208, 184));
    private static readonly Brush UserBubble = new SolidColorBrush(Color.FromRgb(245, 196, 81));
    private static readonly Brush UserBorder = new SolidColorBrush(Color.FromRgb(231, 177, 38));
    private static readonly Brush SystemBubble = new SolidColorBrush(Color.FromRgb(22, 42, 71));
    private static readonly Brush SystemBorder = new SolidColorBrush(Color.FromRgb(60, 92, 138));
    private static readonly Brush DarkInk = new SolidColorBrush(Color.FromRgb(20, 33, 61));
    private static readonly Brush SoftMeta = new SolidColorBrush(Color.FromRgb(181, 199, 222));
    private static readonly Brush LightText = Brushes.White;

    private readonly DesktopApiClient _apiClient = new();
    private readonly DesktopCacheService _cache = new();
    private readonly ObservableCollection<ConversationEntry> _messages = new();
    private FolderWatcherService? _watcher;
    private SessionExchangeResponse? _session;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        MessagesListBox.ItemsSource = _messages;

        HookLiveUpdates();
        LoadCachedState();
        RefreshBotIdentity();
        SetStatus("Connect the bot to begin.");
        AddSystemMessage("AiCan is ready. Connect a demo user or use Microsoft 365 if a real Entra app has been configured.");
        RefreshActionState();
    }

    private void HookLiveUpdates()
    {
        BotNameTextBox.TextChanged += (_, _) => RefreshBotIdentity();
        DisplayNameTextBox.TextChanged += (_, _) => RefreshBotIdentity();
        ToneComboBox.SelectionChanged += (_, _) => RefreshBotIdentity();
        WorkStyleComboBox.SelectionChanged += (_, _) => RefreshBotIdentity();
    }

    private void LoadCachedState()
    {
        var settings = _cache.Load();
        ServerUrlTextBox.Text = settings?.ServerUrl ?? DemoDefaults.ServerUrl;
        EmailTextBox.Text = settings?.Email ?? DemoDefaults.Email;
        DisplayNameTextBox.Text = settings?.DisplayName ?? DemoDefaults.DisplayName;
        BotNameTextBox.Text = settings?.BotName ?? DemoDefaults.BotName;
        DepartmentTextBox.Text = settings?.Department ?? DemoDefaults.Department;
        LanguageTextBox.Text = settings?.PreferredLanguage ?? DemoDefaults.PreferredLanguage;
        WatchedFolderTextBox.Text = settings?.WatchedFolder
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiCan", "Inbox");
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        await ConnectAsync(useM365: false);
    }

    private async void M365Button_Click(object sender, RoutedEventArgs e)
    {
        await ConnectAsync(useM365: true);
    }

    private async Task ConnectAsync(bool useM365)
    {
        try
        {
            SetBusy(true, useM365 ? "Starting Microsoft 365 sign-in..." : "Checking server connection...");

            if (!await _apiClient.GetHealthAsync(ServerUrlTextBox.Text))
            {
                throw new InvalidOperationException("The AiCan server is not reachable. Check the URL and network path first.");
            }

            string? accessToken = null;
            if (useM365)
            {
                var authService = new M365AuthService();
                var authResult = await authService.AcquireAsync(EmailTextBox.Text);
                accessToken = authResult.AccessToken;
            }

            _session = await _apiClient.ExchangeSessionAsync(
                ServerUrlTextBox.Text,
                new SessionExchangeRequest(
                    EmailTextBox.Text.Trim(),
                    DisplayNameTextBox.Text.Trim(),
                    BotNameTextBox.Text.Trim(),
                    DepartmentTextBox.Text.Trim(),
                    accessToken));

            await SaveProfileInternalAsync(emitTimelineEvent: false);
            await LoadHistoryAsync();

            AddAssistantMessage(
                $"{_session.BotName} is connected. I’ll stay within your authorized workspace and cite what I use.",
                "Bot online");
            SetStatus($"Connected as {_session.Email} in {_session.Department}. Role: {_session.Role}.");
        }
        catch (Exception ex)
        {
            _session = null;
            AddSystemMessage($"Connection failed: {ex.Message}");
            SetStatus($"Connection failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadHistoryAsync()
    {
        _messages.Clear();

        if (_session is null)
        {
            return;
        }

        var history = await _apiClient.GetHistoryAsync(ServerUrlTextBox.Text, _session.SessionToken);
        foreach (var item in history)
        {
            switch (item.Role)
            {
                case MessageRole.User:
                    AddUserMessage(item.Content, item.CreatedAtUtc.LocalDateTime.ToString("MMM d, HH:mm"));
                    break;
                case MessageRole.Assistant:
                    AddAssistantMessage(item.Content, item.CreatedAtUtc.LocalDateTime.ToString("MMM d, HH:mm"));
                    break;
                default:
                    AddSystemMessage(item.Content);
                    break;
            }
        }

        if (_messages.Count == 0)
        {
            AddSystemMessage("No prior conversation yet. Try one of the starter prompts above.");
        }
    }

    private async void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true, "Saving bot personality...");
            await SaveProfileInternalAsync(emitTimelineEvent: true);
        }
        catch (Exception ex)
        {
            AddSystemMessage($"Save failed: {ex.Message}");
            SetStatus($"Save failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SaveProfileInternalAsync(bool emitTimelineEvent)
    {
        if (_session is null)
        {
            throw new InvalidOperationException("Connect the bot before saving the profile.");
        }

        var update = new UpdateAssistantProfileRequest(
            BotNameTextBox.Text.Trim(),
            SelectedText(ToneComboBox),
            SelectedText(WorkStyleComboBox),
            LanguageTextBox.Text.Trim());

        var profile = await _apiClient.UpdateProfileAsync(ServerUrlTextBox.Text, _session.SessionToken, update);
        _cache.Save(new DesktopCacheState(
            ServerUrlTextBox.Text.Trim(),
            EmailTextBox.Text.Trim(),
            DisplayNameTextBox.Text.Trim(),
            profile.BotName,
            DepartmentTextBox.Text.Trim(),
            profile.PreferredLanguage,
            WatchedFolderTextBox.Text.Trim()));

        BotNameTextBox.Text = profile.BotName;
        RefreshBotIdentity();
        SetStatus($"Profile saved for {profile.BotName}.");
        if (emitTimelineEvent)
        {
            AddSystemMessage($"Saved personality settings for {profile.BotName}.");
        }
    }

    private void WatchFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            AddSystemMessage("Connect the bot before enabling the watcher.");
            SetStatus("Connect the bot before enabling the watcher.");
            return;
        }

        _watcher?.Dispose();
        _watcher = new FolderWatcherService(
            ServerUrlTextBox.Text.Trim(),
            _session.SessionToken,
            DepartmentTextBox.Text.Trim(),
            _session.Email,
            WatchedFolderTextBox.Text.Trim(),
            message => Dispatcher.Invoke(() =>
            {
                AddSystemMessage(message);
                SetStatus(message);
            }));

        _watcher.Start();
        AddSystemMessage($"Watching {WatchedFolderTextBox.Text.Trim()} for new files.");
        SetStatus($"Watching {WatchedFolderTextBox.Text.Trim()}.");
    }

    private async void UploadFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            AddSystemMessage("Connect the bot before uploading files.");
            SetStatus("Connect the bot before uploading files.");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Choose a file for AiCan intake",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            SetBusy(true, $"Uploading {Path.GetFileName(dialog.FileName)}...");
            var request = await DesktopFileRequestBuilder.CreateAsync(
                dialog.FileName,
                DepartmentTextBox.Text.Trim(),
                _session.Email,
                VisibilityScope.Private);

            var response = await _apiClient.RegisterFileAsync(ServerUrlTextBox.Text.Trim(), _session.SessionToken, request);
            var message = $"Uploaded {request.FileName} to {response.RepositoryPath}.";
            AddSystemMessage(message);
            SetStatus(message);
        }
        catch (Exception ex)
        {
            AddSystemMessage($"Upload failed: {ex.Message}");
            SetStatus($"Upload failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendCurrentMessageAsync();
    }

    private async void PromptButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string prompt)
        {
            return;
        }

        MessageTextBox.Text = prompt;
        await SendCurrentMessageAsync();
    }

    private async Task SendCurrentMessageAsync()
    {
        if (_session is null)
        {
            AddSystemMessage("Connect the bot before sending messages.");
            SetStatus("Connect the bot before sending messages.");
            return;
        }

        var userMessage = MessageTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return;
        }

        try
        {
            SetBusy(true, "RafiBot is thinking...");
            AddUserMessage(userMessage, "You");
            MessageTextBox.Clear();

            var response = await _apiClient.ChatAsync(ServerUrlTextBox.Text.Trim(), _session.SessionToken, new ChatRequest(userMessage));
            AddAssistantMessage(response.Message, BotNameTextBox.Text.Trim());
            foreach (var citation in response.Citations)
            {
                AddSystemMessage($"Citation: {citation.Title} -> {citation.RepositoryPath}");
            }

            SetStatus($"Last reply received from {BotNameTextBox.Text.Trim()}.");
        }
        catch (Exception ex)
        {
            AddSystemMessage($"Chat failed: {ex.Message}");
            SetStatus($"Chat failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RefreshBotIdentity()
    {
        var botName = string.IsNullOrWhiteSpace(BotNameTextBox.Text) ? DemoDefaults.BotName : BotNameTextBox.Text.Trim();
        var displayName = string.IsNullOrWhiteSpace(DisplayNameTextBox.Text) ? DemoDefaults.DisplayName : DisplayNameTextBox.Text.Trim();

        BotDisplayNameTextBlock.Text = botName;
        ConversationTitleTextBlock.Text = $"Talk to {botName}";
        BotInitialsTextBlock.Text = string.Concat(botName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])));
        BotMoodTextBlock.Text = $"{botName} supports {displayName} with a {SelectedText(ToneComboBox)} tone and {SelectedText(WorkStyleComboBox)} work style.";
    }

    private void SetBusy(bool isBusy, string? status = null)
    {
        _isBusy = isBusy;
        if (!string.IsNullOrWhiteSpace(status))
        {
            SetStatus(status);
        }

        RefreshActionState();
    }

    private void RefreshActionState()
    {
        var connected = _session is not null;
        SendButton.IsEnabled = connected && !_isBusy;
        ConnectButton.IsEnabled = !_isBusy;
        M365Button.IsEnabled = !_isBusy;
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }

    private void AddUserMessage(string text, string meta)
    {
        _messages.Add(new ConversationEntry
        {
            Speaker = "You",
            Text = text,
            Meta = meta,
            Alignment = HorizontalAlignment.Right,
            BubbleBrush = UserBubble,
            BorderBrush = UserBorder,
            TextBrush = DarkInk,
            HeaderBrush = DarkInk,
            MetaBrush = SoftMeta
        });
        ScrollMessagesToEnd();
    }

    private void AddAssistantMessage(string text, string meta)
    {
        _messages.Add(new ConversationEntry
        {
            Speaker = string.IsNullOrWhiteSpace(BotNameTextBox.Text) ? DemoDefaults.BotName : BotNameTextBox.Text.Trim(),
            Text = text,
            Meta = meta,
            Alignment = HorizontalAlignment.Left,
            BubbleBrush = AssistantBubble,
            BorderBrush = AssistantBorder,
            TextBrush = DarkInk,
            HeaderBrush = DarkInk,
            MetaBrush = SoftMeta
        });
        ScrollMessagesToEnd();
    }

    private void AddSystemMessage(string text)
    {
        _messages.Add(new ConversationEntry
        {
            Speaker = "System",
            Text = text,
            Meta = "AiCan",
            Alignment = HorizontalAlignment.Left,
            BubbleBrush = SystemBubble,
            BorderBrush = SystemBorder,
            TextBrush = LightText,
            HeaderBrush = LightText,
            MetaBrush = SoftMeta
        });
        ScrollMessagesToEnd();
    }

    private void ScrollMessagesToEnd()
    {
        if (_messages.Count > 0)
        {
            MessagesListBox.ScrollIntoView(_messages[^1]);
        }
    }

    private static string SelectedText(ComboBox comboBox)
    {
        return (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
    }
}
