using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiCan.Contracts;
using Microsoft.Extensions.Options;

namespace AiCan.Api;

public sealed class AiCanOptions
{
    public string RepositoryRoot { get; set; } = "/srv/aican/repository";
    public string LmStudioBaseUrl { get; set; } = "http://127.0.0.1:1234/v1";
    public string LmStudioModel { get; set; } = "local-model";
    public List<SeedUserOptions> SeedUsers { get; set; } = new();
}

public sealed class SeedUserOptions
{
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Department { get; set; } = "General";
    public string Role { get; set; } = nameof(UserRole.User);
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
    public string? Suggestion { get; set; }
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

    public InMemoryUserDirectory(IOptions<AiCanOptions> options)
    {
        _seedUsers = options.Value.SeedUsers.ToDictionary(x => x.Email, x => x, StringComparer.OrdinalIgnoreCase);
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
}

public sealed class InMemoryAssistantProfileStore : IAssistantProfileStore
{
    private readonly ConcurrentDictionary<Guid, AssistantProfileDto> _profiles = new();

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
    }

    public AssistantProfileDto? Get(Guid userId) => _profiles.TryGetValue(userId, out var profile) ? profile : null;

    public AssistantProfileDto Update(Guid userId, UpdateAssistantProfileRequest update)
    {
        return _profiles.AddOrUpdate(
            userId,
            _ => throw new KeyNotFoundException("Profile does not exist."),
            (_, existing) => existing with
            {
                BotName = update.BotName,
                Tone = update.Tone,
                WorkStyle = update.WorkStyle,
                PreferredLanguage = update.PreferredLanguage
            });
    }
}

public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<Guid, List<HistoryMessageDto>> _messages = new();

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
    }
}

public sealed class InMemoryDocumentCatalog : IDocumentCatalog
{
    private readonly ConcurrentDictionary<Guid, CatalogDocument> _documents = new();

    public CatalogDocument Add(CatalogDocument document)
    {
        _documents[document.Id] = document;
        return document;
    }

    public CatalogDocument? Get(Guid documentId) => _documents.TryGetValue(documentId, out var document) ? document : null;

    public void MarkSuggestion(Guid documentId, string category, string reason)
    {
        if (_documents.TryGetValue(documentId, out var existing))
        {
            existing.Suggestion = $"{category}: {reason}";
        }
    }

    public IReadOnlyList<CatalogDocument> Search(Guid userId, string department, string query)
    {
        return _documents.Values
            .Where(document =>
                document.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                document.Summary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                document.Classification.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Where(document => document.Visibility switch
            {
                VisibilityScope.Private => document.OwnerUserId == userId,
                VisibilityScope.DepartmentShared => string.Equals(document.Department, department, StringComparison.OrdinalIgnoreCase),
                VisibilityScope.CommonShared => true,
                _ => false
            })
            .OrderBy(document => document.Title)
            .Take(5)
            .ToList();
    }
}

public sealed class InMemoryAuditLog : IAuditLog
{
    private readonly ConcurrentQueue<string> _entries = new();

    public void Write(Guid actorId, string action, string target, DateTimeOffset occurredAtUtc)
    {
        _entries.Enqueue($"{occurredAtUtc:O}|{actorId}|{action}|{target}");
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
            category = request.FileName.Contains("invoice", StringComparison.OrdinalIgnoreCase)
                ? "invoice"
                : request.FileName.Contains("hr", StringComparison.OrdinalIgnoreCase)
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
    private readonly IClock _clock;

    public DocumentIntakeService(
        IClassificationService classification,
        IEmbeddingProvider embeddings,
        IVectorStore vectorStore,
        IDocumentCatalog catalog,
        IOptions<AiCanOptions> options,
        IClock clock)
    {
        _classification = classification;
        _embeddings = embeddings;
        _vectorStore = vectorStore;
        _catalog = catalog;
        _options = options;
        _clock = clock;
    }

    public async Task<IntakeRegisterResponse> RegisterAsync(SessionExchangeResponse session, IntakeRegisterRequest request, CancellationToken cancellationToken)
    {
        var classification = _classification.Classify(request);
        var documentId = Guid.NewGuid();
        var repositoryPath = Path.Combine(_options.Value.RepositoryRoot, classification.RepositoryPathSegment, request.FileName).Replace('\\', '/');
        var summary = $"Registered {request.FileName} for {classification.CustomerName} in {classification.Category}.";

        _catalog.Add(new CatalogDocument
        {
            Id = documentId,
            OwnerUserId = session.UserId,
            Title = request.FileName,
            Department = request.Department,
            Visibility = request.Visibility,
            Classification = classification.Category,
            RepositoryPath = repositoryPath,
            Summary = summary
        });

        var vector = await _embeddings.EmbedAsync($"{request.FileName} {classification.Category} {classification.CustomerName}", cancellationToken);
        await _vectorStore.UpsertAsync(documentId, vector, cancellationToken);

        return new IntakeRegisterResponse(documentId, repositoryPath, classification.Category, request.Visibility, _clock.UtcNow);
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

        var citations = await _retrieval.RetrieveAsync(session, request.Message, cancellationToken);
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
