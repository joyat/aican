using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiCan.Contracts;
using Microsoft.Extensions.Options;

namespace AiCan.Api;

public sealed class AiCanOptions
{
    public string RepositoryRoot { get; set; } = ".runtime/repository";
    public string StateRoot { get; set; } = ".runtime/state";
    public string LmStudioBaseUrl { get; set; } = "http://127.0.0.1:1234/v1";
    public string LmStudioModel { get; set; } = "local-model";
    public OpenClawOptions OpenClaw { get; set; } = new();
    public List<SeedUserOptions> SeedUsers { get; set; } = new();
    public List<SeedDocumentOptions> SeedDocuments { get; set; } = new();
}

public sealed class OpenClawOptions
{
    public bool Enabled { get; set; }
    public string Command { get; set; } = "openclaw";
    public string AgentId { get; set; } = "aican";
    public bool UseLocalRuntime { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 30;
    public string? WorkingDirectory { get; set; }
    public string? StateDir { get; set; }
    public string? ConfigPath { get; set; }
}

public sealed class SeedUserOptions
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Department { get; set; } = "General";
    public string Role { get; set; } = nameof(UserRole.User);
}

public sealed class SeedDocumentOptions
{
    public string Title { get; set; } = string.Empty;
    public string Department { get; set; } = "General";
    public VisibilityScope Visibility { get; set; } = VisibilityScope.CommonShared;
    public string Classification { get; set; } = "general";
    public string RepositoryPathSegment { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface IUserDirectory
{
    SessionExchangeResponse Exchange(SessionExchangeRequest request);
    SessionExchangeResponse RequireSession(HttpRequest request);
}

public interface IAssistantProfileStore
{
    void UpsertFromSession(SessionExchangeResponse session, SessionExchangeRequest request);
    AssistantProfileDto? Get(Guid userId);
    AssistantProfileDto Update(Guid userId, UpdateAssistantProfileRequest update);
}

public interface IConversationStore
{
    IReadOnlyList<HistoryMessageDto> GetHistory(Guid userId);
    void Append(Guid userId, MessageRole role, string content);
}

public interface IDocumentCatalog
{
    CatalogDocument Add(CatalogDocument document);
    IReadOnlyList<CatalogDocument> Search(Guid userId, string department, string query);
    CatalogDocument? Get(Guid documentId);
    void MarkSuggestion(Guid documentId, string category, string reason);
}

public interface IAuditLog
{
    void Write(Guid actorId, string action, string target, DateTimeOffset occurredAtUtc);
}

public interface IRetrievalService
{
    Task<IReadOnlyList<CitationDto>> RetrieveAsync(SessionExchangeResponse session, string query, CancellationToken cancellationToken);
}

public interface IClassificationService
{
    ClassificationResult Classify(IntakeRegisterRequest request);
}

public interface IDocumentIntakeService
{
    Task<IntakeRegisterResponse> RegisterAsync(SessionExchangeResponse session, IntakeRegisterRequest request, CancellationToken cancellationToken);
}

public interface IAssistantOrchestrator
{
    Task<ChatResponse> RespondAsync(SessionExchangeResponse session, ChatRequest request, CancellationToken cancellationToken);
}

public interface IAssistantRuntime
{
    Task<ChatResponse> RespondAsync(SessionExchangeResponse session, ChatRequest request, CancellationToken cancellationToken);
}

public interface ILLMProvider
{
    Task<string> GenerateResponseAsync(SessionExchangeResponse session, AssistantProfileDto profile, string prompt, CancellationToken cancellationToken);
}

public interface IEmbeddingProvider
{
    Task<float[]> EmbedAsync(string content, CancellationToken cancellationToken);
}

public interface IOcrProvider
{
    Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken);
}

public interface IParserProvider
{
    Task<string> ParseAsync(string filePath, CancellationToken cancellationToken);
}

public interface IVectorStore
{
    Task UpsertAsync(Guid documentId, float[] vector, CancellationToken cancellationToken);
}

public interface IOpenClawRunner
{
    Task<string> RunAsync(SessionExchangeResponse session, AssistantProfileDto profile, string prompt, CancellationToken cancellationToken);
}

public sealed record ClassificationResult(string Category, string CustomerName, string RepositoryPathSegment);

public sealed class CatalogDocument
{
    public Guid Id { get; init; }
    public Guid OwnerUserId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Department { get; init; } = string.Empty;
    public VisibilityScope Visibility { get; init; }
    public string Classification { get; init; } = string.Empty;
    public string RepositoryPath { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string ExtractedText { get; init; } = string.Empty;
    public string? Suggestion { get; set; }
}

public sealed class RuntimePathProvider
{
    public string RepositoryRoot { get; }
    public string StateRoot { get; }

    public RuntimePathProvider(IOptions<AiCanOptions> options)
    {
        RepositoryRoot = ResolvePath(options.Value.RepositoryRoot, ".runtime/repository");
        StateRoot = ResolvePath(options.Value.StateRoot, ".runtime/state");

        Directory.CreateDirectory(RepositoryRoot);
        Directory.CreateDirectory(StateRoot);
    }

    public string GetStateFile(string fileName) => Path.Combine(StateRoot, fileName);

    private static string ResolvePath(string configuredPath, string fallbackPath)
    {
        var value = string.IsNullOrWhiteSpace(configuredPath) ? fallbackPath : configuredPath;
        return Path.GetFullPath(value);
    }
}

internal static class JsonStateFile
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static T LoadOrDefault<T>(string path, T fallback)
    {
        if (!File.Exists(path))
        {
            return fallback;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, Options) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
    }
}

public sealed class SessionContextAccessor
{
    public const string SessionHeader = "X-AiCan-Session";
}

public sealed class InMemoryUserDirectory : IUserDirectory
{
    private readonly ConcurrentDictionary<string, SessionExchangeResponse> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Guid> _userIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SeedUserOptions> _seedUsers;
    private readonly string _userIdsPath;
    private readonly string _sessionsPath;
    private readonly object _sync = new();

    public InMemoryUserDirectory(IOptions<AiCanOptions> options, RuntimePathProvider paths)
    {
        _seedUsers = options.Value.SeedUsers.ToDictionary(x => x.Email, x => x, StringComparer.OrdinalIgnoreCase);
        _userIdsPath = paths.GetStateFile("user-ids.json");
        _sessionsPath = paths.GetStateFile("sessions.json");

        foreach (var pair in JsonStateFile.LoadOrDefault(_userIdsPath, new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)))
        {
            _userIds[pair.Key] = pair.Value;
        }

        foreach (var pair in JsonStateFile.LoadOrDefault(_sessionsPath, new Dictionary<string, SessionExchangeResponse>(StringComparer.OrdinalIgnoreCase)))
        {
            _sessions[pair.Key] = pair.Value;
        }
    }

    public SessionExchangeResponse Exchange(SessionExchangeRequest request)
    {
        if (!_seedUsers.TryGetValue(request.Email, out var seed))
        {
            seed = new SeedUserOptions
            {
                Email = request.Email,
                DisplayName = request.DisplayName,
                Department = request.Department ?? "General",
                Role = nameof(UserRole.User)
            };
        }

        var userId = _userIds.GetOrAdd(request.Email, _ => Guid.NewGuid());
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? seed.DisplayName : request.DisplayName;
        var department = string.IsNullOrWhiteSpace(request.Department) ? seed.Department : request.Department;

        var response = new SessionExchangeResponse(
            userId,
            request.Email,
            displayName,
            string.IsNullOrWhiteSpace(request.BotName) ? $"{displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Ai"}Bot" : request.BotName,
            department,
            Enum.Parse<UserRole>(seed.Role, ignoreCase: true),
            Convert.ToBase64String(Guid.NewGuid().ToByteArray()));

        _sessions[response.SessionToken] = response;
        Persist();
        return response;
    }

    public SessionExchangeResponse RequireSession(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(SessionContextAccessor.SessionHeader, out var values))
        {
            throw new BadHttpRequestException("Missing session header.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var token = values.ToString();
        if (!_sessions.TryGetValue(token, out var session))
        {
            throw new BadHttpRequestException("Unknown session.", statusCode: StatusCodes.Status401Unauthorized);
        }

        return session;
    }

    private void Persist()
    {
        lock (_sync)
        {
            JsonStateFile.Save(_userIdsPath, _userIds.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase));
            JsonStateFile.Save(_sessionsPath, _sessions.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase));
        }
    }
}

public sealed class InMemoryAssistantProfileStore : IAssistantProfileStore
{
    private readonly ConcurrentDictionary<Guid, AssistantProfileDto> _profiles = new();
    private readonly string _profilesPath;
    private readonly object _sync = new();

    public InMemoryAssistantProfileStore(RuntimePathProvider paths)
    {
        _profilesPath = paths.GetStateFile("profiles.json");
        foreach (var pair in JsonStateFile.LoadOrDefault(_profilesPath, new Dictionary<Guid, AssistantProfileDto>()))
        {
            _profiles[pair.Key] = pair.Value;
        }
    }

    public void UpsertFromSession(SessionExchangeResponse session, SessionExchangeRequest request)
    {
        _profiles.AddOrUpdate(
            session.UserId,
            _ => new AssistantProfileDto(
                session.UserId,
                session.Email,
                session.DisplayName,
                session.BotName,
                session.Department,
                "WarmProfessional",
                "HelpfulAndConcise",
                "en",
                session.Role),
            (_, existing) => existing with
            {
                BotName = string.IsNullOrWhiteSpace(request.BotName) ? existing.BotName : request.BotName,
                DisplayName = session.DisplayName,
                Department = session.Department
            });

        Persist();
    }

    public AssistantProfileDto? Get(Guid userId) => _profiles.TryGetValue(userId, out var profile) ? profile : null;

    public AssistantProfileDto Update(Guid userId, UpdateAssistantProfileRequest update)
    {
        var profile = _profiles.AddOrUpdate(
            userId,
            _ => throw new KeyNotFoundException("Profile does not exist."),
            (_, existing) => existing with
            {
                BotName = update.BotName,
                Tone = update.Tone,
                WorkStyle = update.WorkStyle,
                PreferredLanguage = update.PreferredLanguage
            });

        Persist();
        return profile;
    }

    private void Persist()
    {
        lock (_sync)
        {
            JsonStateFile.Save(_profilesPath, _profiles.ToDictionary(x => x.Key, x => x.Value));
        }
    }
}

public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<Guid, List<HistoryMessageDto>> _messages = new();
    private readonly string _messagesPath;
    private readonly object _sync = new();

    public InMemoryConversationStore(RuntimePathProvider paths)
    {
        _messagesPath = paths.GetStateFile("conversations.json");
        foreach (var pair in JsonStateFile.LoadOrDefault(_messagesPath, new Dictionary<Guid, List<HistoryMessageDto>>()))
        {
            _messages[pair.Key] = pair.Value;
        }
    }

    public IReadOnlyList<HistoryMessageDto> GetHistory(Guid userId)
    {
        return _messages.TryGetValue(userId, out var messages)
            ? messages.OrderBy(x => x.CreatedAtUtc).ToList()
            : Array.Empty<HistoryMessageDto>();
    }

    public void Append(Guid userId, MessageRole role, string content)
    {
        var list = _messages.GetOrAdd(userId, _ => new List<HistoryMessageDto>());
        lock (list)
        {
            list.Add(new HistoryMessageDto(Guid.NewGuid(), userId, role, content, DateTimeOffset.UtcNow));
        }

        Persist();
    }

    private void Persist()
    {
        lock (_sync)
        {
            JsonStateFile.Save(_messagesPath, _messages.ToDictionary(x => x.Key, x => x.Value));
        }
    }
}

public sealed class InMemoryDocumentCatalog : IDocumentCatalog
{
    private readonly ConcurrentDictionary<Guid, CatalogDocument> _documents = new();
    private readonly string _documentsPath;
    private readonly object _sync = new();
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "are", "you", "your", "who", "what", "when", "where", "did",
        "last", "show", "latest", "with", "from", "this", "that", "have", "about",
        "can", "could", "for", "our", "his", "her", "their", "them", "into"
    };

    public InMemoryDocumentCatalog(IOptions<AiCanOptions> options, RuntimePathProvider paths)
    {
        _documentsPath = paths.GetStateFile("documents.json");
        foreach (var document in JsonStateFile.LoadOrDefault(_documentsPath, new List<CatalogDocument>()))
        {
            _documents[document.Id] = document;
        }

        SeedDocuments(options.Value.SeedDocuments, paths);
    }

    public CatalogDocument Add(CatalogDocument document)
    {
        _documents[document.Id] = document;
        Persist();
        return document;
    }

    public CatalogDocument? Get(Guid documentId) => _documents.TryGetValue(documentId, out var document) ? document : null;

    public void MarkSuggestion(Guid documentId, string category, string reason)
    {
        if (_documents.TryGetValue(documentId, out var existing))
        {
            existing.Suggestion = $"{category}: {reason}";
            Persist();
        }
    }

    public IReadOnlyList<CatalogDocument> Search(Guid userId, string department, string query)
    {
        var terms = query
            .Split(new[] { ' ', '\t', '\r', '\n', ',', '.', '?', '!', ':', ';', '-', '_', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(term => term.Trim().ToLowerInvariant())
            .Where(term => term.Length >= 3)
            .Where(term => !StopWords.Contains(term))
            .Distinct()
            .ToArray();

        return _documents.Values
            .Select(document => new
            {
                Document = document,
                Score = Score(document, query, terms)
            })
            .Where(x => x.Score > 0)
            .Where(document => document.Document.Visibility switch
            {
                VisibilityScope.Private => document.Document.OwnerUserId == userId,
                VisibilityScope.DepartmentShared => string.Equals(document.Document.Department, department, StringComparison.OrdinalIgnoreCase),
                VisibilityScope.CommonShared => true,
                _ => false
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Document.Title)
            .Take(5)
            .Select(x => x.Document)
            .ToList();
    }

    private void SeedDocuments(IEnumerable<SeedDocumentOptions> seeds, RuntimePathProvider paths)
    {
        var changed = false;
        foreach (var seed in seeds)
        {
            if (_documents.Values.Any(document =>
                string.Equals(document.Title, seed.Title, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(document.RepositoryPath, Path.Combine(paths.RepositoryRoot, seed.RepositoryPathSegment).Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var repositoryPath = Path.Combine(paths.RepositoryRoot, seed.RepositoryPathSegment);
            Directory.CreateDirectory(Path.GetDirectoryName(repositoryPath)!);

            if (!string.IsNullOrWhiteSpace(seed.Content) && !File.Exists(repositoryPath))
            {
                File.WriteAllText(repositoryPath, seed.Content);
            }

            var document = new CatalogDocument
            {
                Id = Guid.NewGuid(),
                OwnerUserId = Guid.Empty,
                Title = seed.Title,
                Department = seed.Department,
                Visibility = seed.Visibility,
                Classification = seed.Classification,
                RepositoryPath = repositoryPath.Replace('\\', '/'),
                Summary = seed.Summary,
                ExtractedText = string.IsNullOrWhiteSpace(seed.Content) ? seed.Summary : seed.Content
            };

            _documents[document.Id] = document;
            changed = true;
        }

        if (changed)
        {
            Persist();
        }
    }

    private void Persist()
    {
        lock (_sync)
        {
            JsonStateFile.Save(_documentsPath, _documents.Values.OrderBy(x => x.Title).ToList());
        }
    }

    private static int Score(CatalogDocument document, string query, IReadOnlyList<string> terms)
    {
        var haystack = $"{document.Title} {document.Summary} {document.Classification} {document.ExtractedText}".ToLowerInvariant();
        var loweredQuery = query.ToLowerInvariant();
        var score = 0;

        if (haystack.Contains(loweredQuery, StringComparison.Ordinal))
        {
            score += 8;
        }

        foreach (var term in terms)
        {
            if (haystack.Contains(term, StringComparison.Ordinal))
            {
                score += 3;
            }
        }

        return score;
    }
}

public sealed class InMemoryAuditLog : IAuditLog
{
    private readonly ConcurrentQueue<string> _entries = new();
    private readonly string _auditPath;
    private readonly object _sync = new();

    public InMemoryAuditLog(RuntimePathProvider paths)
    {
        _auditPath = paths.GetStateFile("audit.log");
    }

    public void Write(Guid actorId, string action, string target, DateTimeOffset occurredAtUtc)
    {
        var entry = $"{occurredAtUtc:O}|{actorId}|{action}|{target}";
        _entries.Enqueue(entry);
        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_auditPath)!);
            File.AppendAllLines(_auditPath, [entry]);
        }
    }
}

public sealed class RetrievalService : IRetrievalService
{
    private readonly IDocumentCatalog _catalog;

    public RetrievalService(IDocumentCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<IReadOnlyList<CitationDto>> RetrieveAsync(SessionExchangeResponse session, string query, CancellationToken cancellationToken)
    {
        var citations = _catalog.Search(session.UserId, session.Department, query)
            .Select(document => new CitationDto(document.Id, document.Title, document.RepositoryPath, document.Summary))
            .ToList();
        return Task.FromResult<IReadOnlyList<CitationDto>>(citations);
    }
}

public sealed class FilingClassificationService : IClassificationService
{
    public ClassificationResult Classify(IntakeRegisterRequest request)
    {
        var category = request.DeclaredCategory;
        if (string.IsNullOrWhiteSpace(category))
        {
            var corpus = $"{request.FileName} {request.ExtractedText}";
            category = corpus.Contains("invoice", StringComparison.OrdinalIgnoreCase)
                ? "invoice"
                : corpus.Contains("printer", StringComparison.OrdinalIgnoreCase) || corpus.Contains("purchase", StringComparison.OrdinalIgnoreCase)
                    ? "procurement"
                    : corpus.Contains("hr", StringComparison.OrdinalIgnoreCase) || corpus.Contains("employee", StringComparison.OrdinalIgnoreCase)
                    ? "hr"
                    : "general";
        }

        var customerName = string.IsNullOrWhiteSpace(request.CustomerName)
            ? "unassigned"
            : request.CustomerName.Trim().Replace(' ', '-').ToLowerInvariant();

        var now = DateTimeOffset.UtcNow;
        var pathSegment = $"{category}/{now:yyyy}/{now:MMMM}/{customerName}";
        return new ClassificationResult(category.ToLowerInvariant(), customerName, pathSegment.ToLowerInvariant());
    }
}

public sealed class DocumentIntakeService : IDocumentIntakeService
{
    private readonly IClassificationService _classification;
    private readonly IEmbeddingProvider _embeddings;
    private readonly IVectorStore _vectorStore;
    private readonly IDocumentCatalog _catalog;
    private readonly IOptions<AiCanOptions> _options;
    private readonly RuntimePathProvider _paths;
    private readonly IClock _clock;

    public DocumentIntakeService(
        IClassificationService classification,
        IEmbeddingProvider embeddings,
        IVectorStore vectorStore,
        IDocumentCatalog catalog,
        IOptions<AiCanOptions> options,
        RuntimePathProvider paths,
        IClock clock)
    {
        _classification = classification;
        _embeddings = embeddings;
        _vectorStore = vectorStore;
        _catalog = catalog;
        _options = options;
        _paths = paths;
        _clock = clock;
    }

    public async Task<IntakeRegisterResponse> RegisterAsync(SessionExchangeResponse session, IntakeRegisterRequest request, CancellationToken cancellationToken)
    {
        var classification = _classification.Classify(request);
        var documentId = Guid.NewGuid();
        var fileName = Path.GetFileName(string.IsNullOrWhiteSpace(request.FileName) ? "document.bin" : request.FileName);
        var repositoryPath = Path.Combine(_paths.RepositoryRoot, classification.RepositoryPathSegment, fileName);
        var extractedText = BuildExtractedText(request, fileName);
        var summary = BuildSummary(fileName, classification, extractedText);

        Directory.CreateDirectory(Path.GetDirectoryName(repositoryPath)!);
        PersistBinary(repositoryPath, request);

        _catalog.Add(new CatalogDocument
        {
            Id = documentId,
            OwnerUserId = session.UserId,
            Title = fileName,
            Department = request.Department,
            Visibility = request.Visibility,
            Classification = classification.Category,
            RepositoryPath = repositoryPath.Replace('\\', '/'),
            Summary = summary,
            ExtractedText = extractedText
        });

        var vector = await _embeddings.EmbedAsync($"{fileName} {classification.Category} {classification.CustomerName} {extractedText}", cancellationToken);
        await _vectorStore.UpsertAsync(documentId, vector, cancellationToken);

        return new IntakeRegisterResponse(documentId, repositoryPath.Replace('\\', '/'), classification.Category, request.Visibility, _clock.UtcNow);
    }

    private static string BuildExtractedText(IntakeRegisterRequest request, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(request.ExtractedText))
        {
            return request.ExtractedText;
        }

        if (!string.IsNullOrWhiteSpace(request.FileContentBase64))
        {
            try
            {
                var bytes = Convert.FromBase64String(request.FileContentBase64);
                var extension = Path.GetExtension(fileName).ToLowerInvariant();
                if (extension is ".txt" or ".md" or ".csv" or ".json")
                {
                    return Encoding.UTF8.GetString(bytes);
                }
            }
            catch
            {
            }
        }

        return $"Registered file {fileName} from {request.OriginalFilePath}.";
    }

    private static string BuildSummary(string fileName, ClassificationResult classification, string extractedText)
    {
        var snippet = extractedText.Length > 160 ? extractedText[..160] + "..." : extractedText;
        return $"{fileName} filed under {classification.Category} for {classification.CustomerName}. {snippet}".Trim();
    }

    private static void PersistBinary(string repositoryPath, IntakeRegisterRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.FileContentBase64))
        {
            try
            {
                File.WriteAllBytes(repositoryPath, Convert.FromBase64String(request.FileContentBase64));
                return;
            }
            catch
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(request.OriginalFilePath) && File.Exists(request.OriginalFilePath))
        {
            File.Copy(request.OriginalFilePath, repositoryPath, overwrite: true);
            return;
        }

        File.WriteAllText(repositoryPath, request.ExtractedText ?? string.Empty);
    }
}

public sealed class AssistantOrchestrator : IAssistantOrchestrator
{
    private readonly IAssistantProfileStore _profiles;
    private readonly IConversationStore _conversations;
    private readonly IRetrievalService _retrieval;
    private readonly ILLMProvider _llm;

    public AssistantOrchestrator(
        IAssistantProfileStore profiles,
        IConversationStore conversations,
        IRetrievalService retrieval,
        ILLMProvider llm)
    {
        _profiles = profiles;
        _conversations = conversations;
        _retrieval = retrieval;
        _llm = llm;
    }

    public async Task<ChatResponse> RespondAsync(SessionExchangeResponse session, ChatRequest request, CancellationToken cancellationToken)
    {
        var profile = _profiles.Get(session.UserId) ?? throw new InvalidOperationException("Assistant profile is missing.");
        _conversations.Append(session.UserId, MessageRole.User, request.Message);

        if (TryBuildInstantResponse(profile, request.Message, out var instantResponse))
        {
            _conversations.Append(session.UserId, MessageRole.Assistant, instantResponse);
            return new ChatResponse(
                instantResponse,
                Array.Empty<CitationDto>(),
                new[] { new SuggestedActionDto(SuggestedActionType.RequestAccess, "Request access to a library", null) });
        }

        var citations = await _retrieval.RetrieveAsync(session, request.Message, cancellationToken);
        if (TryBuildCitationGroundedResponse(profile, request.Message, citations, out var groundedResponse))
        {
            _conversations.Append(session.UserId, MessageRole.Assistant, groundedResponse);
            return new ChatResponse(groundedResponse, citations, BuildSuggestedActions(citations));
        }

        var context = citations.Count == 0
            ? "No authorized document citations were found."
            : string.Join(Environment.NewLine, citations.Select(c => $"- {c.Title}: {c.Snippet}"));

        var prompt = $"""
            You are {profile.BotName}, a warm professional office assistant for {profile.DisplayName}.
            Work style: {profile.WorkStyle}.
            Preferred language: {profile.PreferredLanguage}.
            Respond clearly, cite what you know, and never invent access to hidden files.

            User message:
            {request.Message}

            Authorized context:
            {context}
            """;

        var responseText = await _llm.GenerateResponseAsync(session, profile, prompt, cancellationToken);
        _conversations.Append(session.UserId, MessageRole.Assistant, responseText);

        var suggestedActions = BuildSuggestedActions(citations);
        return new ChatResponse(responseText, citations, suggestedActions);
    }

    private static bool TryBuildInstantResponse(AssistantProfileDto profile, string message, out string response)
    {
        var normalized = message.Trim().ToLowerInvariant();
        if (normalized.Contains("who are you", StringComparison.Ordinal) || normalized.Contains("what are you", StringComparison.Ordinal))
        {
            response = $"I’m {profile.BotName}, your friendly workplace assistant for {profile.DisplayName}. I help with document-grounded answers, file intake, and day-to-day office questions while staying inside your authorized workspace.";
            return true;
        }

        if (normalized is "hi" or "hello" or "hey")
        {
            response = $"Hello, I’m {profile.BotName}. I’m here and ready to help.";
            return true;
        }

        response = string.Empty;
        return false;
    }

    private static bool TryBuildCitationGroundedResponse(
        AssistantProfileDto profile,
        string message,
        IReadOnlyList<CitationDto> citations,
        out string response)
    {
        if (citations.Count == 0)
        {
            response = string.Empty;
            return false;
        }

        var primary = citations[0];
        var builder = new StringBuilder();
        builder.Append($"Based on the documents I can access for you, {primary.Snippet}");

        if (citations.Count > 1)
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append("Supporting context: ");
            builder.Append(string.Join(" | ", citations.Skip(1).Take(2).Select(c => c.Snippet)));
        }

        if (LooksLikePurchaseQuestion(message))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append($"Source: {primary.Title}.");
        }
        else
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append($"{profile.BotName} can open the cited document or help reclassify it if needed.");
        }

        response = builder.ToString().Trim();
        return true;
    }

    private static bool LooksLikePurchaseQuestion(string message)
    {
        var normalized = message.Trim().ToLowerInvariant();
        return normalized.Contains("purchase", StringComparison.Ordinal)
            || normalized.Contains("bought", StringComparison.Ordinal)
            || normalized.Contains("invoice", StringComparison.Ordinal)
            || normalized.Contains("last", StringComparison.Ordinal)
            || normalized.Contains("latest", StringComparison.Ordinal)
            || normalized.Contains("when did we", StringComparison.Ordinal);
    }

    private static IReadOnlyList<SuggestedActionDto> BuildSuggestedActions(IReadOnlyList<CitationDto> citations)
    {
        if (citations.Count == 0)
        {
            return new[]
            {
                new SuggestedActionDto(SuggestedActionType.RequestAccess, "Request access to a library", null)
            };
        }

        var first = citations[0];
        return new[]
        {
            new SuggestedActionDto(SuggestedActionType.OpenDocument, "Open document", first.DocumentId.ToString()),
            new SuggestedActionDto(SuggestedActionType.ViewSource, "View source", first.DocumentId.ToString()),
            new SuggestedActionDto(SuggestedActionType.Reclassify, "Suggest reclassification", first.DocumentId.ToString())
        };
    }
}

public sealed class AssistantRuntimeRouter : IAssistantRuntime
{
    private readonly IOptions<AiCanOptions> _options;
    private readonly IAssistantOrchestrator _builtIn;
    private readonly OpenClawAssistantRuntime _openClaw;
    private readonly IRetrievalService _retrieval;

    public AssistantRuntimeRouter(
        IOptions<AiCanOptions> options,
        IAssistantOrchestrator builtIn,
        OpenClawAssistantRuntime openClaw,
        IRetrievalService retrieval)
    {
        _options = options;
        _builtIn = builtIn;
        _openClaw = openClaw;
        _retrieval = retrieval;
    }

    public async Task<ChatResponse> RespondAsync(SessionExchangeResponse session, ChatRequest request, CancellationToken cancellationToken)
    {
        if (!_options.Value.OpenClaw.Enabled)
        {
            return await _builtIn.RespondAsync(session, request, cancellationToken);
        }

        if (ShouldUseBuiltInFastPath(request.Message))
        {
            return await _builtIn.RespondAsync(session, request, cancellationToken);
        }

        var citations = await _retrieval.RetrieveAsync(session, request.Message, cancellationToken);
        if (citations.Count > 0)
        {
            return await _builtIn.RespondAsync(session, request, cancellationToken);
        }

        try
        {
            return await _openClaw.RespondAsync(session, request, cancellationToken);
        }
        catch
        {
            return await _builtIn.RespondAsync(session, request, cancellationToken);
        }
    }

    private static bool ShouldUseBuiltInFastPath(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return true;
        }

        var normalized = message.Trim().ToLowerInvariant();
        string[] fastPathPhrases =
        [
            "who are you",
            "what are you",
            "what can you do",
            "introduce yourself",
            "hello",
            "hi",
            "hey",
            "are you there",
            "thanks",
            "thank you",
            "good morning",
            "good afternoon"
        ];

        return fastPathPhrases.Any(phrase => normalized.Contains(phrase, StringComparison.Ordinal));
    }
}

public sealed class OpenClawAssistantRuntime : IAssistantRuntime
{
    private readonly IAssistantProfileStore _profiles;
    private readonly IConversationStore _conversations;
    private readonly IRetrievalService _retrieval;
    private readonly IOpenClawRunner _runner;

    public OpenClawAssistantRuntime(
        IAssistantProfileStore profiles,
        IConversationStore conversations,
        IRetrievalService retrieval,
        IOpenClawRunner runner)
    {
        _profiles = profiles;
        _conversations = conversations;
        _retrieval = retrieval;
        _runner = runner;
    }

    public async Task<ChatResponse> RespondAsync(SessionExchangeResponse session, ChatRequest request, CancellationToken cancellationToken)
    {
        var profile = _profiles.Get(session.UserId) ?? throw new InvalidOperationException("Assistant profile is missing.");
        _conversations.Append(session.UserId, MessageRole.User, request.Message);

        var citations = await _retrieval.RetrieveAsync(session, request.Message, cancellationToken);
        var context = citations.Count == 0
            ? "No authorized AiCan citations were found."
            : string.Join(Environment.NewLine, citations.Select(c => $"- {c.Title}: {c.Snippet} ({c.RepositoryPath})"));

        var prompt = $"""
            You are {profile.BotName}, the employee-facing AiCan assistant for {profile.DisplayName}.
            Stay warm, professional, and practical.
            You are running inside OpenClaw as the conversation runtime.
            Never claim access beyond the authorized AiCan context below.
            When you need enterprise data or actions, prefer the configured AiCan tools.
            The current employee details are:
            - email: {session.Email}
            - department: {session.Department}
            - role: {session.Role}
            - preferred language: {profile.PreferredLanguage}
            - work style: {profile.WorkStyle}

            Authorized AiCan context:
            {context}

            Employee message:
            {request.Message}
            """;

        var responseText = await _runner.RunAsync(session, profile, prompt, cancellationToken);
        _conversations.Append(session.UserId, MessageRole.Assistant, responseText);

        var suggestedActions = BuildSuggestedActions(citations);
        return new ChatResponse(responseText, citations, suggestedActions);
    }

    private static IReadOnlyList<SuggestedActionDto> BuildSuggestedActions(IReadOnlyList<CitationDto> citations)
    {
        if (citations.Count == 0)
        {
            return new[]
            {
                new SuggestedActionDto(SuggestedActionType.RequestAccess, "Request access to a library", null)
            };
        }

        var first = citations[0];
        return new[]
        {
            new SuggestedActionDto(SuggestedActionType.OpenDocument, "Open document", first.DocumentId.ToString()),
            new SuggestedActionDto(SuggestedActionType.ViewSource, "View source", first.DocumentId.ToString()),
            new SuggestedActionDto(SuggestedActionType.Reclassify, "Suggest reclassification", first.DocumentId.ToString())
        };
    }
}

public sealed class OpenClawRunner : IOpenClawRunner
{
    private readonly OpenClawOptions _options;

    public OpenClawRunner(IOptions<AiCanOptions> options)
    {
        _options = options.Value.OpenClaw;
    }

    public async Task<string> RunAsync(SessionExchangeResponse session, AssistantProfileDto profile, string prompt, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.Command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        if (!string.IsNullOrWhiteSpace(_options.WorkingDirectory))
        {
            startInfo.WorkingDirectory = _options.WorkingDirectory;
        }

        if (!string.IsNullOrWhiteSpace(_options.StateDir))
        {
            startInfo.Environment["OPENCLAW_STATE_DIR"] = _options.StateDir;
        }

        if (!string.IsNullOrWhiteSpace(_options.ConfigPath))
        {
            startInfo.Environment["OPENCLAW_CONFIG_PATH"] = _options.ConfigPath;
        }

        startInfo.ArgumentList.Add("agent");
        startInfo.ArgumentList.Add("--agent");
        startInfo.ArgumentList.Add(_options.AgentId);
        startInfo.ArgumentList.Add("--message");
        startInfo.ArgumentList.Add(prompt);
        startInfo.ArgumentList.Add("--timeout");
        startInfo.ArgumentList.Add(_options.TimeoutSeconds.ToString());

        if (_options.UseLocalRuntime)
        {
            startInfo.ArgumentList.Add("--local");
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("OpenClaw process did not start.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            throw new TimeoutException($"OpenClaw exceeded the {_options.TimeoutSeconds}-second runtime limit.");
        }

        var stdoutText = (await stdout).Trim();
        var stderrText = (await stderr).Trim();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"OpenClaw failed with exit code {process.ExitCode}: {stderrText}");
        }

        if (string.IsNullOrWhiteSpace(stdoutText))
        {
            throw new InvalidOperationException($"OpenClaw returned no output. stderr: {stderrText}");
        }

        return stdoutText;
    }
}

public sealed class LmStudioProvider : ILLMProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiCanOptions _options;

    public LmStudioProvider(IHttpClientFactory httpClientFactory, IOptions<AiCanOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<string> GenerateResponseAsync(SessionExchangeResponse session, AssistantProfileDto profile, string prompt, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(nameof(LmStudioProvider));
        client.BaseAddress = new Uri(_options.LmStudioBaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = new
        {
            model = _options.LmStudioModel,
            temperature = 0.3,
            messages = new[]
            {
                new { role = "system", content = $"You are {profile.BotName}, a friendly but policy-bound workplace assistant." },
                new { role = "user", content = prompt }
            }
        };

        try
        {
            using var response = await client.PostAsync(
                "chat/completions",
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return BuildFallback(profile, prompt);
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return string.IsNullOrWhiteSpace(content) ? BuildFallback(profile, prompt) : content;
        }
        catch
        {
            return BuildFallback(profile, prompt);
        }
    }

    private static string BuildFallback(AssistantProfileDto profile, string prompt)
    {
        return $"{profile.BotName}: I could not reach the configured LLM provider, so I am returning a safe fallback response. I understood your request and preserved the current conversation state. Prompt excerpt: {prompt[..Math.Min(prompt.Length, 180)]}";
    }
}

public sealed class DeterministicEmbeddingProvider : IEmbeddingProvider
{
    public Task<float[]> EmbedAsync(string content, CancellationToken cancellationToken)
    {
        var values = new float[8];
        foreach (var (character, index) in content.Take(64).Select((c, i) => (c, i)))
        {
            values[index % values.Length] += character;
        }

        return Task.FromResult(values);
    }
}

public sealed class NullOcrProvider : IOcrProvider
{
    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
        => Task.FromResult($"OCR placeholder extracted text from {filePath}");
}

public sealed class NullParserProvider : IParserProvider
{
    public Task<string> ParseAsync(string filePath, CancellationToken cancellationToken)
        => Task.FromResult($"Parser placeholder extracted text from {filePath}");
}

public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<Guid, float[]> _vectors = new();

    public Task UpsertAsync(Guid documentId, float[] vector, CancellationToken cancellationToken)
    {
        _vectors[documentId] = vector;
        return Task.CompletedTask;
    }
}
