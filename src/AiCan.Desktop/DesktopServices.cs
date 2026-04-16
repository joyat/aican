using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AiCan.Contracts;
using Microsoft.Identity.Client;

namespace AiCan.Desktop;

public sealed record DesktopCacheState(
    string ServerUrl,
    string Email,
    string DisplayName,
    string BotName,
    string Department,
    string PreferredLanguage,
    string WatchedFolder);

public sealed record M365AuthResult(string AccessToken);

public sealed class M365AuthService
{
    private const string ClientId = "00000000-0000-0000-0000-000000000000";
    private static readonly string[] Scopes = ["User.Read"];

    public async Task<M365AuthResult> AcquireAsync(string loginHint)
    {
        var application = PublicClientApplicationBuilder
            .Create(ClientId)
            .WithDefaultRedirectUri()
            .Build();

        var result = await application.AcquireTokenInteractive(Scopes)
            .WithLoginHint(loginHint)
            .ExecuteAsync();

        return new M365AuthResult(result.AccessToken);
    }
}

public sealed class DesktopApiClient
{
    private readonly HttpClient _httpClient = new();

    public async Task<SessionExchangeResponse> ExchangeSessionAsync(string serverUrl, SessionExchangeRequest request)
    {
        var response = await _httpClient.PostAsync(
            $"{serverUrl.TrimEnd('/')}/session/exchange",
            JsonContent.Create(request));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SessionExchangeResponse>() ?? throw new InvalidOperationException("Missing session response.");
    }

    public async Task<AssistantProfileDto> UpdateProfileAsync(string serverUrl, string sessionToken, UpdateAssistantProfileRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Patch, $"{serverUrl.TrimEnd('/')}/assistant/profile")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-AiCan-Session", sessionToken);

        using var response = await _httpClient.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssistantProfileDto>() ?? throw new InvalidOperationException("Missing profile response.");
    }

    public async Task<ChatResponse> ChatAsync(string serverUrl, string sessionToken, ChatRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{serverUrl.TrimEnd('/')}/assistant/chat")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-AiCan-Session", sessionToken);

        using var response = await _httpClient.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatResponse>() ?? throw new InvalidOperationException("Missing chat response.");
    }

    public async Task<IntakeRegisterResponse> RegisterFileAsync(string serverUrl, string sessionToken, IntakeRegisterRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{serverUrl.TrimEnd('/')}/documents/intake/register")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.Add("X-AiCan-Session", sessionToken);

        using var response = await _httpClient.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IntakeRegisterResponse>() ?? throw new InvalidOperationException("Missing intake response.");
    }
}

public sealed class FolderWatcherService : IDisposable
{
    private readonly DesktopApiClient _client = new();
    private readonly string _serverUrl;
    private readonly string _sessionToken;
    private readonly string _department;
    private readonly string _folderPath;
    private readonly Action<string> _status;
    private FileSystemWatcher? _watcher;

    public FolderWatcherService(string serverUrl, string sessionToken, string department, string folderPath, Action<string> status)
    {
        _serverUrl = serverUrl;
        _sessionToken = sessionToken;
        _department = department;
        _folderPath = folderPath;
        _status = status;
    }

    public void Start()
    {
        Directory.CreateDirectory(_folderPath);

        _watcher = new FileSystemWatcher(_folderPath)
        {
            EnableRaisingEvents = true,
            IncludeSubdirectories = false
        };
        _watcher.Created += OnCreated;
    }

    private async void OnCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            var request = new IntakeRegisterRequest(
                e.FullPath,
                Path.GetFileName(e.FullPath),
                _department,
                VisibilityScope.Private,
                null,
                null,
                null);

            var response = await _client.RegisterFileAsync(_serverUrl, _sessionToken, request);
            _status($"Registered {request.FileName} to {response.RepositoryPath}");
        }
        catch (Exception ex)
        {
            _status($"Folder watcher error for {e.Name}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_watcher is not null)
        {
            _watcher.Created -= OnCreated;
            _watcher.Dispose();
        }
    }
}

public sealed class DesktopCacheService
{
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AiCan");
    private static readonly string CachePath = Path.Combine(CacheDirectory, "desktop-cache.bin");

    public void Save(DesktopCacheState state)
    {
        Directory.CreateDirectory(CacheDirectory);
        var json = JsonSerializer.Serialize(state);
        var bytes = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(CachePath, encrypted);
    }

    public DesktopCacheState? Load()
    {
        if (!File.Exists(CachePath))
        {
            return null;
        }

        var encrypted = File.ReadAllBytes(CachePath);
        var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<DesktopCacheState>(Encoding.UTF8.GetString(bytes));
    }
}
