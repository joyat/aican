using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

public sealed record ApiHealthResponse(string Status);

public static class DemoDefaults
{
    public const string ServerUrl = "http://sungas-ubuntulab.tail6932f9.ts.net:5000";
    public const string Email = "user@example.test";
    public const string DisplayName = "Demo User";
    public const string BotName = "AiCan Assistant";
    public const string Department = "General";
    public const string PreferredLanguage = "en";
}

public sealed class M365AuthService
{
    private const string PlaceholderClientId = "00000000-0000-0000-0000-000000000000";
    private static readonly string[] Scopes = ["User.Read"];

    private static string ClientId =>
        Environment.GetEnvironmentVariable("AICAN_M365_CLIENT_ID")?.Trim()
        ?? PlaceholderClientId;

    private static string Authority
    {
        get
        {
            var tenantId = Environment.GetEnvironmentVariable("AICAN_M365_TENANT_ID")?.Trim();
            return string.IsNullOrWhiteSpace(tenantId)
                ? "https://login.microsoftonline.com/common"
                : $"https://login.microsoftonline.com/{tenantId}";
        }
    }

    public bool IsConfigured => !string.Equals(ClientId, PlaceholderClientId, StringComparison.Ordinal);

    public async Task<M365AuthResult> AcquireAsync(string loginHint)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Microsoft 365 sign-in is not configured. Set AICAN_M365_CLIENT_ID first, then retry.");
        }

        var application = PublicClientApplicationBuilder
            .Create(ClientId)
            .WithAuthority(Authority)
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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    // 180 s gives a slow local LLM endpoint enough time to respond
    // without leaving the UI locked indefinitely.
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(180)
    };

    public async Task<bool> GetHealthAsync(string serverUrl)
    {
        var response = await _httpClient.GetAsync($"{serverUrl.TrimEnd('/')}/healthz");
        return response.IsSuccessStatusCode;
    }

    public async Task<SystemStatusResponse> GetSystemStatusAsync(string serverUrl)
    {
        using var response = await _httpClient.GetAsync($"{serverUrl.TrimEnd('/')}/system/status");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SystemStatusResponse>(JsonOptions)
            ?? new SystemStatusResponse(Array.Empty<ServiceHealthDto>(), DateTimeOffset.UtcNow);
    }

    public async Task<SessionExchangeResponse> ExchangeSessionAsync(string serverUrl, SessionExchangeRequest request)
    {
        var response = await _httpClient.PostAsync(
            $"{serverUrl.TrimEnd('/')}/session/exchange",
            JsonContent.Create(request, options: JsonOptions));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SessionExchangeResponse>(JsonOptions) ?? throw new InvalidOperationException("Missing session response.");
    }

    public async Task<AssistantProfileDto> UpdateProfileAsync(string serverUrl, string sessionToken, UpdateAssistantProfileRequest request)
    {
        using var message = CreateAuthorized(HttpMethod.Patch, $"{serverUrl.TrimEnd('/')}/assistant/profile", sessionToken, request);
        using var response = await _httpClient.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssistantProfileDto>(JsonOptions) ?? throw new InvalidOperationException("Missing profile response.");
    }

    public async Task<IReadOnlyList<HistoryMessageDto>> GetHistoryAsync(string serverUrl, string sessionToken)
    {
        using var message = CreateAuthorized(HttpMethod.Get, $"{serverUrl.TrimEnd('/')}/assistant/history", sessionToken);
        using var response = await _httpClient.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<HistoryMessageDto>>(JsonOptions) ?? Array.Empty<HistoryMessageDto>();
    }

    public async Task<ChatResponse> ChatAsync(string serverUrl, string sessionToken, ChatRequest request)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"{serverUrl.TrimEnd('/')}/assistant/chat", sessionToken, request);
        using var response = await _httpClient.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions) ?? throw new InvalidOperationException("Missing chat response.");
    }

    public async Task<IntakeRegisterResponse> RegisterFileAsync(string serverUrl, string sessionToken, IntakeRegisterRequest request)
    {
        using var message = CreateAuthorized(HttpMethod.Post, $"{serverUrl.TrimEnd('/')}/documents/intake/register", sessionToken, request);
        using var response = await _httpClient.SendAsync(message);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<IntakeRegisterResponse>(JsonOptions) ?? throw new InvalidOperationException("Missing intake response.");
    }

    private static HttpRequestMessage CreateAuthorized<T>(HttpMethod method, string url, string sessionToken, T payload)
    {
        var message = CreateAuthorized(method, url, sessionToken);
        message.Content = JsonContent.Create(payload, options: JsonOptions);
        return message;
    }

    private static HttpRequestMessage CreateAuthorized(HttpMethod method, string url, string sessionToken)
    {
        var message = new HttpRequestMessage(method, url);
        message.Headers.Add("X-AiCan-Session", sessionToken);
        return message;
    }
}

public sealed class FolderWatcherService : IDisposable
{
    private readonly DesktopApiClient _client = new();
    private readonly string _serverUrl;
    private readonly string _sessionToken;
    private readonly string _department;
    private readonly string _ownerEmail;
    private readonly string _folderPath;
    private readonly Action<string> _status;
    private readonly HashSet<string> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private FileSystemWatcher? _watcher;

    public FolderWatcherService(
        string serverUrl,
        string sessionToken,
        string department,
        string ownerEmail,
        string folderPath,
        Action<string> status)
    {
        _serverUrl = serverUrl;
        _sessionToken = sessionToken;
        _department = department;
        _ownerEmail = ownerEmail;
        _folderPath = folderPath;
        _status = status;
    }

    public void Start()
    {
        Directory.CreateDirectory(_folderPath);

        _watcher = new FileSystemWatcher(_folderPath)
        {
            EnableRaisingEvents = true,
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size
        };
        _watcher.Created += OnCreated;
        _watcher.Changed += OnCreated;
    }

    private async void OnCreated(object sender, FileSystemEventArgs e)
    {
        if (Directory.Exists(e.FullPath))
        {
            return;
        }

        lock (_sync)
        {
            if (!_inFlight.Add(e.FullPath))
            {
                return;
            }
        }

        try
        {
            await Task.Delay(1200);
            var request = await DesktopFileRequestBuilder.CreateAsync(e.FullPath, _department, _ownerEmail, VisibilityScope.Private);
            var response = await _client.RegisterFileAsync(_serverUrl, _sessionToken, request);
            _status($"Registered {request.FileName} to {response.RepositoryPath}");
        }
        catch (Exception ex)
        {
            _status($"Folder watcher error for {e.Name}: {ex.Message}");
        }
        finally
        {
            lock (_sync)
            {
                _inFlight.Remove(e.FullPath);
            }
        }
    }

    public void Dispose()
    {
        if (_watcher is not null)
        {
            _watcher.Created -= OnCreated;
            _watcher.Changed -= OnCreated;
            _watcher.Dispose();
        }
    }
}

public static class DesktopFileRequestBuilder
{
    private const int MaxInlineBytes = 5 * 1024 * 1024;

    public static async Task<IntakeRegisterRequest> CreateAsync(
        string filePath,
        string department,
        string ownerEmail,
        VisibilityScope visibility,
        string? declaredCategory = null,
        string? customerName = null)
    {
        var safeFileName = Path.GetFileName(filePath);
        var fileInfo = new FileInfo(filePath);
        string? base64 = null;
        if (fileInfo.Exists && fileInfo.Length <= MaxInlineBytes)
        {
            base64 = Convert.ToBase64String(await ReadAllBytesWithRetryAsync(filePath));
        }

        var extractedText = await TryExtractTextAsync(filePath);
        return new IntakeRegisterRequest(
            filePath,
            safeFileName,
            department,
            visibility,
            declaredCategory,
            ownerEmail,
            customerName,
            base64,
            extractedText);
    }

    private static async Task<byte[]> ReadAllBytesWithRetryAsync(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return await File.ReadAllBytesAsync(path);
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(400);
            }
        }

        return await File.ReadAllBytesAsync(path);
    }

    private static async Task<string?> TryExtractTextAsync(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".txt" or ".md" or ".csv" or ".json"))
        {
            return null;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return await File.ReadAllTextAsync(path, Encoding.UTF8);
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(400);
            }
        }

        return null;
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
