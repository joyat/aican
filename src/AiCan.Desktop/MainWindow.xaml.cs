using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
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
    private enum ServiceLampState
    {
        Wait,
        Live,
        Warn,
        Down
    }

    private enum PresenceState
    {
        Offline,
        Ready,
        Busy,
        Watch,
        Error
    }

    private static readonly Brush AssistantBubble = new SolidColorBrush(Color.FromRgb(233, 247, 242));
    private static readonly Brush AssistantBorder = new SolidColorBrush(Color.FromRgb(133, 201, 179));
    private static readonly Brush UserBubble = new SolidColorBrush(Color.FromRgb(244, 201, 91));
    private static readonly Brush UserBorder = new SolidColorBrush(Color.FromRgb(228, 177, 37));
    private static readonly Brush SystemBubble = new SolidColorBrush(Color.FromRgb(22, 46, 82));
    private static readonly Brush SystemBorder = new SolidColorBrush(Color.FromRgb(61, 97, 142));
    private static readonly Brush DarkInk = new SolidColorBrush(Color.FromRgb(15, 27, 55));
    private static readonly Brush SoftMeta = new SolidColorBrush(Color.FromRgb(184, 202, 226));
    private static readonly Brush LightText = Brushes.White;

    private readonly DesktopApiClient _apiClient = new();
    private readonly DesktopCacheService _cache = new();
    private readonly ObservableCollection<ConversationEntry> _messages = new();
    private readonly DispatcherTimer _serviceStatusTimer = new() { Interval = TimeSpan.FromSeconds(20) };
    private FolderWatcherService? _watcher;
    private SessionExchangeResponse? _session;
    private bool _isBusy;
    private bool _serviceDeckRefreshInFlight;
    private bool _watcherActive;

    public MainWindow()
    {
        InitializeComponent();
        MessagesListBox.ItemsSource = _messages;

        InitializeServiceDeck();
        HookLiveUpdates();
        LoadCachedState();
        RefreshBotIdentity();
        SetStatus("Connect the bot to begin.");
        SetPresence(PresenceState.Offline);
        AddSystemMessage("AiCan is ready. Connect JoBot and start from the quick starters.");
        RefreshActionState();
    }

    private void HookLiveUpdates()
    {
        BotNameTextBox.TextChanged += (_, _) => RefreshBotIdentity();
        DisplayNameTextBox.TextChanged += (_, _) => RefreshBotIdentity();
    }

    private void InitializeServiceDeck()
    {
        SetServiceCardState(ApiServiceLed, ApiServiceStateText, ServiceLampState.Wait, "WAIT");
        SetServiceCardState(LlmServiceLed, LlmServiceStateText, ServiceLampState.Wait, "WAIT");
        SetServiceCardState(WorkerServiceLed, WorkerServiceStateText, ServiceLampState.Wait, "WAIT");
        SetServiceCardState(QdrantServiceLed, QdrantServiceStateText, ServiceLampState.Wait, "WAIT");

        UpdateLocalServiceDeck();

        _serviceStatusTimer.Tick += async (_, _) => await RefreshServiceDeckAsync();
        Loaded += async (_, _) =>
        {
            await RefreshServiceDeckAsync();
            _serviceStatusTimer.Start();
        };
        Closed += (_, _) => _serviceStatusTimer.Stop();
    }

    private void LoadCachedState()
    {
        var settings = _cache.Load();
        ServerUrlTextBox.Text = settings?.ServerUrl ?? DemoDefaults.ServerUrl;
        EmailTextBox.Text = settings?.Email ?? DemoDefaults.Email;
        DisplayNameTextBox.Text = NormalizeDisplayName(settings?.DisplayName);
        BotNameTextBox.Text = NormalizeBotName(settings?.BotName);
        DepartmentTextBox.Text = settings?.Department ?? DemoDefaults.Department;
        WatchedFolderTextBox.Text = settings?.WatchedFolder
            ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiCan", "Inbox");

        SetLanguage(settings?.PreferredLanguage ?? DemoDefaults.PreferredLanguage);
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

            if (!await _apiClient.GetHealthAsync(ServerUrlTextBox.Text.Trim()))
            {
                throw new InvalidOperationException("The AiCan server is not reachable. Check the URL and network path first.");
            }

            string? accessToken = null;
            if (useM365)
            {
                var authService = new M365AuthService();
                var authResult = await authService.AcquireAsync(EmailTextBox.Text.Trim());
                accessToken = authResult.AccessToken;
            }

            _session = await _apiClient.ExchangeSessionAsync(
                ServerUrlTextBox.Text.Trim(),
                new SessionExchangeRequest(
                    EmailTextBox.Text.Trim(),
                    DisplayNameTextBox.Text.Trim(),
                    BotNameTextBox.Text.Trim(),
                    DepartmentTextBox.Text.Trim(),
                    accessToken));

            await SaveProfileInternalAsync(emitTimelineEvent: false);
            await LoadHistoryAsync();

            AddAssistantMessage($"{CurrentBotName()} is connected. I’ll keep the tone personal, grounded, and within your authorized workspace.", "Bot online");
            SetStatus($"Connected as {_session.Email} in {_session.Department}. Role: {_session.Role}.");
            SetPresence(_watcherActive ? PresenceState.Watch : PresenceState.Ready);
        }
        catch (Exception ex)
        {
            _session = null;
            SetPresence(PresenceState.Error);
            AddSystemMessage($"Connection failed: {ex.Message}");
            SetStatus($"Connection failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }

        await RefreshServiceDeckAsync();
    }

    private async Task LoadHistoryAsync()
    {
        _messages.Clear();

        if (_session is null)
        {
            return;
        }

        var history = await _apiClient.GetHistoryAsync(ServerUrlTextBox.Text.Trim(), _session.SessionToken);
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
            AddSystemMessage("No prior conversation yet. Use a starter prompt or ask JoBot something directly.");
        }
    }

    private async void SaveProfileButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true, "Applying personality ribbon...");
            await SaveProfileInternalAsync(emitTimelineEvent: true);
        }
        catch (Exception ex)
        {
            SetPresence(PresenceState.Error);
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
            throw new InvalidOperationException("Connect the bot before saving the personality.");
        }

        var update = new UpdateAssistantProfileRequest(
            BotNameTextBox.Text.Trim(),
            SelectedTone(),
            SelectedWorkStyle(),
            SelectedLanguage());

        var profile = await _apiClient.UpdateProfileAsync(ServerUrlTextBox.Text.Trim(), _session.SessionToken, update);
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
        SetStatus($"Personality applied for {profile.BotName}.");
        if (emitTimelineEvent)
        {
            AddSystemMessage($"Updated JoBot's ribbon settings: tone {SelectedTone()}, work style {SelectedWorkStyle()}, language {SelectedLanguage()}.");
        }
    }

    private void WatchFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            SetPresence(PresenceState.Error);
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
                _watcherActive = true;
                SetPresence(PresenceState.Watch);
                UpdateLocalServiceDeck();
                AddSystemMessage(message);
                SetStatus(message);
            }));

        _watcher.Start();
        _watcherActive = true;
        SetPresence(PresenceState.Watch);
        UpdateLocalServiceDeck();
        AddSystemMessage($"Watching {WatchedFolderTextBox.Text.Trim()} for new files.");
        SetStatus($"Watching {WatchedFolderTextBox.Text.Trim()}.");
    }

    private async void UploadFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            SetPresence(PresenceState.Error);
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
            SetBusy(true, $"Uploading {System.IO.Path.GetFileName(dialog.FileName)}...");
            var request = await DesktopFileRequestBuilder.CreateAsync(
                dialog.FileName,
                DepartmentTextBox.Text.Trim(),
                _session.Email,
                VisibilityScope.Private);

            var response = await _apiClient.RegisterFileAsync(ServerUrlTextBox.Text.Trim(), _session.SessionToken, request);
            var message = $"Uploaded {request.FileName} to {response.RepositoryPath}.";
            AddSystemMessage(message);
            SetStatus(message);
            SetPresence(_watcherActive ? PresenceState.Watch : PresenceState.Ready);
            await RefreshServiceDeckAsync();
        }
        catch (Exception ex)
        {
            SetPresence(PresenceState.Error);
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
            SetPresence(PresenceState.Error);
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
            SetBusy(true, $"{CurrentBotName()} is thinking...");
            AddUserMessage(userMessage, "You");
            MessageTextBox.Clear();

            var response = await _apiClient.ChatAsync(ServerUrlTextBox.Text.Trim(), _session.SessionToken, new ChatRequest(userMessage));
            var displayText = FormatResponseWithCitations(response);
            AddAssistantMessage(displayText, CurrentBotName());

            SetStatus($"Last reply received from {CurrentBotName()}.");
            SetPresence(_watcherActive ? PresenceState.Watch : PresenceState.Ready);
        }
        catch (TaskCanceledException)
        {
            SetPresence(PresenceState.Error);
            var message = "Chat timed out before the assistant replied. The server path is reachable, but the bot runtime took too long.";
            AddSystemMessage(message);
            SetStatus(message);
        }
        catch (HttpRequestException ex)
        {
            SetPresence(PresenceState.Error);
            AddSystemMessage($"Chat failed: {ex.Message}");
            SetStatus($"Chat failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            SetPresence(PresenceState.Error);
            AddSystemMessage($"Chat failed: {ex.Message}");
            SetStatus($"Chat failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RibbonChanged(object sender, RoutedEventArgs e)
    {
        RefreshBotIdentity();
    }

    private async void RefreshServicesButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshServiceDeckAsync();
    }

    private void RefreshBotIdentity()
    {
        if (BotDisplayNameTextBlock is null
            || ConversationTitleTextBlock is null
            || BotInitialsTextBlock is null
            || BotMoodTextBlock is null
            || BotNameTextBox is null
            || DisplayNameTextBox is null)
        {
            return;
        }

        var botName = CurrentBotName();
        var displayName = CurrentDisplayName();
        BotDisplayNameTextBlock.Text = botName;
        ConversationTitleTextBlock.Text = $"Talk to {botName}";
        BotInitialsTextBlock.Text = BuildInitials(botName);
        BotMoodTextBlock.Text = $"{botName} supports {displayName} with a {SelectedTone()} tone and a helpful office-focused style.";
    }

    private void SetBusy(bool isBusy, string? status = null)
    {
        _isBusy = isBusy;
        if (!string.IsNullOrWhiteSpace(status))
        {
            SetStatus(status);
        }

        if (isBusy)
        {
            SetPresence(PresenceState.Busy);
        }
        else if (_session is null)
        {
            SetPresence(PresenceState.Offline);
        }
        else if (_watcherActive)
        {
            SetPresence(PresenceState.Watch);
        }
        else
        {
            SetPresence(PresenceState.Ready);
        }

        RefreshActionState();
        UpdateLocalServiceDeck();
    }

    private void RefreshActionState()
    {
        var connected = _session is not null;
        SendButton.IsEnabled = connected && !_isBusy;
        ConnectButton.IsEnabled = !_isBusy;
        M365Button.IsEnabled = !_isBusy;
        SaveRibbonButton.IsEnabled = connected && !_isBusy;
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }

    private void SetPresence(PresenceState state)
    {
        (StatusWordTextBlock.Text, StatusLedEllipse.Fill) = state switch
        {
            PresenceState.Offline => ("OFFLINE", new SolidColorBrush(Color.FromRgb(127, 139, 152))),
            PresenceState.Ready => ("READY", new SolidColorBrush(Color.FromRgb(74, 222, 128))),
            PresenceState.Busy => ("BUSY", new SolidColorBrush(Color.FromRgb(244, 201, 91))),
            PresenceState.Watch => ("WATCH", new SolidColorBrush(Color.FromRgb(78, 205, 196))),
            PresenceState.Error => ("ERROR", new SolidColorBrush(Color.FromRgb(248, 113, 113))),
            _ => ("READY", new SolidColorBrush(Color.FromRgb(74, 222, 128)))
        };

        UpdateLocalServiceDeck();
    }

    private async Task RefreshServiceDeckAsync()
    {
        UpdateLocalServiceDeck();

        if (_serviceDeckRefreshInFlight)
        {
            return;
        }

        var serverUrl = ServerUrlTextBox?.Text.Trim();
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            SetServiceCardState(ApiServiceLed, ApiServiceStateText, ServiceLampState.Down, "DOWN");
            SetServiceCardState(LlmServiceLed, LlmServiceStateText, ServiceLampState.Wait, "WAIT");
            SetServiceCardState(WorkerServiceLed, WorkerServiceStateText, ServiceLampState.Wait, "WAIT");
            SetServiceCardState(QdrantServiceLed, QdrantServiceStateText, ServiceLampState.Wait, "WAIT");
            return;
        }

        _serviceDeckRefreshInFlight = true;
        try
        {
            var status = await _apiClient.GetSystemStatusAsync(serverUrl);
            ApplyRemoteServiceState("api", ApiServiceLed, ApiServiceStateText, status.Services);
            ApplyRemoteServiceState("llm", LlmServiceLed, LlmServiceStateText, status.Services);
            ApplyRemoteServiceState("worker", WorkerServiceLed, WorkerServiceStateText, status.Services);
            ApplyRemoteServiceState("qdrant", QdrantServiceLed, QdrantServiceStateText, status.Services);
        }
        catch
        {
            SetServiceCardState(ApiServiceLed, ApiServiceStateText, ServiceLampState.Down, "DOWN");
            SetServiceCardState(LlmServiceLed, LlmServiceStateText, ServiceLampState.Down, "DOWN");
            SetServiceCardState(WorkerServiceLed, WorkerServiceStateText, ServiceLampState.Down, "DOWN");
            SetServiceCardState(QdrantServiceLed, QdrantServiceStateText, ServiceLampState.Down, "DOWN");
        }
        finally
        {
            _serviceDeckRefreshInFlight = false;
        }
    }

    private void ApplyRemoteServiceState(
        string key,
        Ellipse led,
        TextBlock stateText,
        IReadOnlyList<ServiceHealthDto> services)
    {
        var service = services.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
        if (service is null)
        {
            SetServiceCardState(led, stateText, ServiceLampState.Wait, "WAIT");
            return;
        }

        var word = string.Equals(service.State, "live", StringComparison.OrdinalIgnoreCase) ? "LIVE" : "DOWN";
        var lamp = string.Equals(service.State, "live", StringComparison.OrdinalIgnoreCase)
            ? ServiceLampState.Live
            : ServiceLampState.Down;

        SetServiceCardState(led, stateText, lamp, word);
    }

    private void UpdateLocalServiceDeck()
    {
        if (JoBotServiceLed is null
            || JoBotServiceStateText is null
            || WatchServiceLed is null
            || WatchServiceStateText is null)
        {
            return;
        }

        if (_session is null)
        {
            SetServiceCardState(JoBotServiceLed, JoBotServiceStateText, ServiceLampState.Down, "DOWN");
        }
        else if (_isBusy)
        {
            SetServiceCardState(JoBotServiceLed, JoBotServiceStateText, ServiceLampState.Warn, "BUSY");
        }
        else
        {
            SetServiceCardState(JoBotServiceLed, JoBotServiceStateText, ServiceLampState.Live, "LIVE");
        }

        if (_watcherActive)
        {
            SetServiceCardState(WatchServiceLed, WatchServiceStateText, ServiceLampState.Live, "LIVE");
        }
        else
        {
            SetServiceCardState(WatchServiceLed, WatchServiceStateText, ServiceLampState.Wait, "IDLE");
        }
    }

    private static void SetServiceCardState(Ellipse led, TextBlock stateText, ServiceLampState state, string word)
    {
        stateText.Text = word;
        led.Fill = state switch
        {
            ServiceLampState.Live => new SolidColorBrush(Color.FromRgb(74, 222, 128)),
            ServiceLampState.Warn => new SolidColorBrush(Color.FromRgb(244, 201, 91)),
            ServiceLampState.Down => new SolidColorBrush(Color.FromRgb(248, 113, 113)),
            _ => new SolidColorBrush(Color.FromRgb(127, 139, 152))
        };
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
            Speaker = CurrentBotName(),
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

    /// <summary>
    /// Appends a compact sources line to the LLM reply so citations appear inside the
    /// bot bubble rather than as separate system messages.
    /// Example:  "…email body…\n\n── Sources: invoice.txt · printer-summary.md"
    /// </summary>
    private static string FormatResponseWithCitations(ChatResponse response)
    {
        if (response.Citations.Count == 0)
        {
            return response.Message;
        }

        var titles = string.Join("  ·  ", response.Citations.Select(c => c.Title));
        return $"{response.Message.TrimEnd()}\n\n\u2500\u2500 Sources: {titles}";
    }

    private string CurrentBotName()
    {
        return NormalizeBotName(BotNameTextBox.Text.Trim());
    }

    private string CurrentDisplayName()
    {
        return NormalizeDisplayName(DisplayNameTextBox.Text.Trim());
    }

    private string SelectedTone()
    {
        if (ToneFormalRadio is not null && ToneFormalRadio.IsChecked == true)
        {
            return "FormalBusiness";
        }

        return "WarmProfessional";
    }

    private string SelectedWorkStyle()
    {
        return "HelpfulAndConcise";
    }

    private string SelectedLanguage()
    {
        return LanguageBanglaRadio is not null && LanguageBanglaRadio.IsChecked == true ? "bn" : "en";
    }

    private void SetLanguage(string language)
    {
        if (LanguageBanglaRadio is null || LanguageEnglishRadio is null)
        {
            return;
        }

        if (string.Equals(language, "bn", StringComparison.OrdinalIgnoreCase))
        {
            LanguageBanglaRadio.IsChecked = true;
            return;
        }

        LanguageEnglishRadio.IsChecked = true;
    }

    private static string NormalizeBotName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "RafiBot", StringComparison.OrdinalIgnoreCase))
        {
            return DemoDefaults.BotName;
        }

        return value.Trim();
    }

    private static string NormalizeDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "Nadia Islam", StringComparison.OrdinalIgnoreCase))
        {
            return DemoDefaults.DisplayName;
        }

        return value.Trim();
    }

    private static string BuildInitials(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "JB";
        }

        return string.Concat(parts.Take(2).Select(part => char.ToUpperInvariant(part[0])));
    }
}
