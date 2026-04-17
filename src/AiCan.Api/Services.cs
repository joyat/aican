using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
    /// <summary>
    /// Path to the OpenClaw workspace directory that contains SOUL.md, IDENTITY.md, AGENTS.md.
    /// Used as the bot's personality layer when calling LM Studio directly.
    /// Leave empty to fall back to a generic system prompt.
    /// </summary>
    public string WorkspaceRoot { get; set; } = "";
    public RagOptions Rag { get; set; } = new();
    public QdrantOptions Qdrant { get; set; } = new();
    public AiWorkerOptions AiWorker { get; set; } = new();
    public OpenClawOptions OpenClaw { get; set; } = new();
    public List<SeedUserOptions> SeedUsers { get; set; } = new();
    public List<SeedDocumentOptions> SeedDocuments { get; set; } = new();
}

public sealed class RagOptions
{
    public int ChunkSizeCharacters { get; set; } = 2400;
    public int ChunkOverlapCharacters { get; set; } = 320;
    public int RetrievalLimit { get; set; } = 10;
    public bool BootstrapOnStart { get; set; } = true;
}

public sealed class QdrantOptions
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "http://127.0.0.1:6333";
    public string CollectionName { get; set; } = "aican_document_chunks";
    public int VectorSize { get; set; } = 768;
    public int SearchLimit { get; set; } = 12;
}

public sealed class AiWorkerOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8001";
    public string EmbeddingModel { get; set; } = "intfloat/multilingual-e5-base";
    public int TimeoutSeconds { get; set; } = 120;
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
    IReadOnlyList<CatalogDocument> GetAll();
    IReadOnlyList<CatalogDocument> Search(Guid userId, string department, string query);
    CatalogDocument? Get(Guid documentId);
    CatalogDocument UpdateExtractedText(Guid documentId, string extractedText, string summary);
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
    Task<float[]> EmbedAsync(string content, EmbeddingInputType inputType, CancellationToken cancellationToken);
    Task<IReadOnlyList<float[]>> EmbedManyAsync(IReadOnlyList<string> contents, EmbeddingInputType inputType, CancellationToken cancellationToken);
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
    Task UpsertAsync(IReadOnlyList<ChunkVectorRecord> chunks, CancellationToken cancellationToken);
    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(float[] queryVector, SessionExchangeResponse session, int limit, CancellationToken cancellationToken);
}

public interface IOpenClawRunner
{
    Task<string> RunAsync(SessionExchangeResponse session, AssistantProfileDto profile, string prompt, CancellationToken cancellationToken);
}

public enum EmbeddingInputType
{
    Passage,
    Query
}

public sealed record ClassificationResult(string Category, string CustomerName, string RepositoryPathSegment);

public sealed record ChunkVectorRecord(
    Guid ChunkId,
    Guid DocumentId,
    int ChunkIndex,
    string Title,
    string Department,
    VisibilityScope Visibility,
    Guid OwnerUserId,
    string Classification,
    string RepositoryPath,
    string Summary,
    string Text,
    float[] Vector);

public sealed record VectorSearchHit(
    Guid ChunkId,
    Guid DocumentId,
    int ChunkIndex,
    string Title,
    string RepositoryPath,
    string Text,
    string Summary,
    double Score);

public interface IDocumentIndexer
{
    Task IndexDocumentAsync(CatalogDocument document, CancellationToken cancellationToken);
}

public interface IRagBootstrapper
{
    Task EnsureIndexedAsync(CancellationToken cancellationToken);
}

public interface IRagIndexStateStore
{
    bool IsCurrent(CatalogDocument document);
    void MarkIndexed(CatalogDocument document);
}

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

    public IReadOnlyList<CatalogDocument> GetAll()
    {
        return _documents.Values
            .OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public CatalogDocument? Get(Guid documentId) => _documents.TryGetValue(documentId, out var document) ? document : null;

    public CatalogDocument UpdateExtractedText(Guid documentId, string extractedText, string summary)
    {
        if (!_documents.TryGetValue(documentId, out var existing))
        {
            throw new KeyNotFoundException($"Document {documentId} was not found.");
        }

        var updated = new CatalogDocument
        {
            Id = existing.Id,
            OwnerUserId = existing.OwnerUserId,
            Title = existing.Title,
            Department = existing.Department,
            Visibility = existing.Visibility,
            Classification = existing.Classification,
            RepositoryPath = existing.RepositoryPath,
            Summary = summary,
            ExtractedText = extractedText,
            Suggestion = existing.Suggestion
        };

        _documents[documentId] = updated;
        Persist();
        return updated;
    }

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

public sealed class TextChunker
{
    private readonly RagOptions _options;

    public TextChunker(IOptions<AiCanOptions> options)
    {
        _options = options.Value.Rag;
    }

    public IReadOnlyList<(Guid ChunkId, int ChunkIndex, string Text)> Chunk(CatalogDocument document)
    {
        var normalized = Normalize(document.ExtractedText, document.Summary, document.Title);
        var chunks = new List<(Guid ChunkId, int ChunkIndex, string Text)>();
        var chunkSize = Math.Max(600, _options.ChunkSizeCharacters);
        var overlap = Math.Clamp(_options.ChunkOverlapCharacters, 0, chunkSize / 3);

        var start = 0;
        var chunkIndex = 0;
        while (start < normalized.Length)
        {
            var maxEnd = Math.Min(normalized.Length, start + chunkSize);
            var minPreferred = Math.Min(normalized.Length, start + (chunkSize / 2));
            var end = FindBoundary(normalized, start, maxEnd, minPreferred);
            var text = normalized[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                chunks.Add((CreateChunkId(document.Id, chunkIndex), chunkIndex, text));
                chunkIndex++;
            }

            if (end >= normalized.Length)
            {
                break;
            }

            start = Math.Max(end - overlap, start + 1);
        }

        if (chunks.Count == 0)
        {
            chunks.Add((CreateChunkId(document.Id, 0), 0, normalized));
        }

        return chunks;
    }

    private static string Normalize(string extractedText, string summary, string title)
    {
        var value = string.IsNullOrWhiteSpace(extractedText)
            ? string.IsNullOrWhiteSpace(summary) ? title : summary
            : extractedText;

        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static int FindBoundary(string content, int start, int maxEnd, int minPreferred)
    {
        if (maxEnd >= content.Length)
        {
            return content.Length;
        }

        for (var i = maxEnd; i >= minPreferred; i--)
        {
            if (i + 1 < content.Length && content[i] == '\n' && content[i + 1] == '\n')
            {
                return i + 1;
            }
        }

        for (var i = maxEnd; i >= minPreferred; i--)
        {
            if (".!?;:\n".Contains(content[i]))
            {
                return i + 1;
            }
        }

        for (var i = maxEnd; i >= minPreferred; i--)
        {
            if (char.IsWhiteSpace(content[i]))
            {
                return i + 1;
            }
        }

        return maxEnd;
    }

    private static Guid CreateChunkId(Guid documentId, int chunkIndex)
    {
        Span<byte> source = stackalloc byte[20];
        documentId.TryWriteBytes(source[..16]);
        BitConverter.TryWriteBytes(source[16..20], chunkIndex);
        var hash = MD5.HashData(source);
        return new Guid(hash);
    }
}

public sealed class DocumentIndexer : IDocumentIndexer
{
    private readonly TextChunker _chunker;
    private readonly IEmbeddingProvider _embeddings;
    private readonly IVectorStore _vectorStore;
    private readonly IRagIndexStateStore _indexState;

    public DocumentIndexer(
        TextChunker chunker,
        IEmbeddingProvider embeddings,
        IVectorStore vectorStore,
        IRagIndexStateStore indexState)
    {
        _chunker = chunker;
        _embeddings = embeddings;
        _vectorStore = vectorStore;
        _indexState = indexState;
    }

    public async Task IndexDocumentAsync(CatalogDocument document, CancellationToken cancellationToken)
    {
        var chunks = _chunker.Chunk(document);
        var vectors = await _embeddings.EmbedManyAsync(
            chunks.Select(chunk => chunk.Text).ToList(),
            EmbeddingInputType.Passage,
            cancellationToken);

        if (vectors.Count != chunks.Count)
        {
            throw new InvalidOperationException($"Expected {chunks.Count} embeddings but received {vectors.Count}.");
        }

        var records = chunks
            .Select((chunk, index) => new ChunkVectorRecord(
                chunk.ChunkId,
                document.Id,
                chunk.ChunkIndex,
                document.Title,
                document.Department,
                document.Visibility,
                document.OwnerUserId,
                document.Classification,
                document.RepositoryPath,
                document.Summary,
                chunk.Text,
                vectors[index]))
            .ToList();

        await _vectorStore.UpsertAsync(records, cancellationToken);
        _indexState.MarkIndexed(document);
    }
}

public sealed class RagIndexStateStore : IRagIndexStateStore
{
    private readonly string _statePath;
    private readonly ConcurrentDictionary<Guid, string> _fingerprints = new();
    private readonly object _sync = new();

    public RagIndexStateStore(RuntimePathProvider paths)
    {
        _statePath = paths.GetStateFile("rag-index.json");
        foreach (var pair in JsonStateFile.LoadOrDefault(_statePath, new Dictionary<Guid, string>()))
        {
            _fingerprints[pair.Key] = pair.Value;
        }
    }

    public bool IsCurrent(CatalogDocument document)
    {
        return _fingerprints.TryGetValue(document.Id, out var value)
            && string.Equals(value, CreateFingerprint(document), StringComparison.Ordinal);
    }

    public void MarkIndexed(CatalogDocument document)
    {
        _fingerprints[document.Id] = CreateFingerprint(document);
        Persist();
    }

    private void Persist()
    {
        lock (_sync)
        {
            JsonStateFile.Save(_statePath, _fingerprints.ToDictionary(x => x.Key, x => x.Value));
        }
    }

    private static string CreateFingerprint(CatalogDocument document)
    {
        var source = $"{document.Id:D}|{document.Title}|{document.Classification}|{document.Visibility}|{document.Department}|{document.OwnerUserId:D}|{document.RepositoryPath}|{document.ExtractedText}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(hash);
    }
}

public sealed class RagBootstrapper : IRagBootstrapper
{
    private readonly IDocumentCatalog _catalog;
    private readonly IDocumentIndexer _indexer;
    private readonly IRagIndexStateStore _indexState;
    private readonly RagOptions _options;
    private readonly IParserProvider _parser;
    private readonly IOcrProvider _ocr;

    public RagBootstrapper(
        IDocumentCatalog catalog,
        IDocumentIndexer indexer,
        IRagIndexStateStore indexState,
        IParserProvider parser,
        IOcrProvider ocr,
        IOptions<AiCanOptions> options)
    {
        _catalog = catalog;
        _indexer = indexer;
        _indexState = indexState;
        _parser = parser;
        _ocr = ocr;
        _options = options.Value.Rag;
    }

    public async Task EnsureIndexedAsync(CancellationToken cancellationToken)
    {
        if (!_options.BootstrapOnStart)
        {
            return;
        }

        foreach (var candidate in _catalog.GetAll())
        {
            var document = await RefreshExtractedTextAsync(candidate, cancellationToken);
            if (_indexState.IsCurrent(document))
            {
                continue;
            }

            try
            {
                await _indexer.IndexDocumentAsync(document, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"[RAG bootstrap] Failed to index {document.Title}: {ex.Message}");
            }
        }
    }

    private async Task<CatalogDocument> RefreshExtractedTextAsync(CatalogDocument document, CancellationToken cancellationToken)
    {
        if (!DocumentTextHelpers.LooksLikePlaceholder(document.ExtractedText, document.Title))
        {
            return document;
        }

        if (string.IsNullOrWhiteSpace(document.RepositoryPath) || !File.Exists(document.RepositoryPath))
        {
            return document;
        }

        string extractedText;
        try
        {
            extractedText = await _parser.ParseAsync(document.RepositoryPath, cancellationToken);
        }
        catch
        {
            extractedText = string.Empty;
        }

        if (DocumentTextHelpers.LooksLikePlaceholder(extractedText, document.Title))
        {
            try
            {
                extractedText = await _ocr.ExtractTextAsync(document.RepositoryPath, cancellationToken);
            }
            catch
            {
                extractedText = string.Empty;
            }
        }

        if (DocumentTextHelpers.LooksLikePlaceholder(extractedText, document.Title))
        {
            return document;
        }

        var classification = new ClassificationResult(
            document.Classification,
            DeriveCustomerName(document.RepositoryPath),
            string.Empty);
        var summary = DocumentTextHelpers.BuildSummary(document.Title, classification, extractedText);
        return _catalog.UpdateExtractedText(document.Id, extractedText, summary);
    }

    private static string DeriveCustomerName(string repositoryPath)
    {
        var directory = Path.GetDirectoryName(repositoryPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return "unassigned";
        }

        return new DirectoryInfo(directory).Name;
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
    private readonly IEmbeddingProvider _embeddings;
    private readonly IVectorStore _vectorStore;
    private readonly RagOptions _options;

    public RetrievalService(
        IDocumentCatalog catalog,
        IEmbeddingProvider embeddings,
        IVectorStore vectorStore,
        IOptions<AiCanOptions> options)
    {
        _catalog = catalog;
        _embeddings = embeddings;
        _vectorStore = vectorStore;
        _options = options.Value.Rag;
    }

    public async Task<IReadOnlyList<CitationDto>> RetrieveAsync(SessionExchangeResponse session, string query, CancellationToken cancellationToken)
    {
        try
        {
            var queryVector = await _embeddings.EmbedAsync(query, EmbeddingInputType.Query, cancellationToken);
            var hits = await _vectorStore.SearchAsync(
                queryVector,
                session,
                Math.Max(4, _options.RetrievalLimit),
                cancellationToken);

            var citations = hits
                .GroupBy(hit => hit.DocumentId)
                .Select(group =>
                {
                    var top = group
                        .OrderByDescending(hit => hit.Score)
                        .ThenBy(hit => hit.ChunkIndex)
                        .First();
                    return new CitationDto(
                        top.DocumentId,
                        top.Title,
                        top.RepositoryPath,
                        top.Text.Length > 240 ? top.Text[..240] + "..." : top.Text);
                })
                .Take(5)
                .ToList();

            if (citations.Count > 0)
            {
                return citations;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"[Retrieval] Falling back to catalog search: {ex.Message}");
        }

        return _catalog.Search(session.UserId, session.Department, query)
            .Select(document => new CitationDto(document.Id, document.Title, document.RepositoryPath, document.Summary))
            .ToList();
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
    private readonly IDocumentCatalog _catalog;
    private readonly RuntimePathProvider _paths;
    private readonly IClock _clock;
    private readonly IDocumentIndexer _indexer;
    private readonly IParserProvider _parser;
    private readonly IOcrProvider _ocr;

    public DocumentIntakeService(
        IClassificationService classification,
        IDocumentCatalog catalog,
        RuntimePathProvider paths,
        IClock clock,
        IDocumentIndexer indexer,
        IParserProvider parser,
        IOcrProvider ocr)
    {
        _classification = classification;
        _catalog = catalog;
        _paths = paths;
        _clock = clock;
        _indexer = indexer;
        _parser = parser;
        _ocr = ocr;
    }

    public async Task<IntakeRegisterResponse> RegisterAsync(SessionExchangeResponse session, IntakeRegisterRequest request, CancellationToken cancellationToken)
    {
        var documentId = Guid.NewGuid();
        var fileName = Path.GetFileName(string.IsNullOrWhiteSpace(request.FileName) ? "document.bin" : request.FileName);
        var initialText = await BuildInitialExtractedTextAsync(request, fileName, cancellationToken);
        var classificationSeed = request with { ExtractedText = initialText };
        var classification = _classification.Classify(classificationSeed);
        var repositoryPath = Path.Combine(_paths.RepositoryRoot, classification.RepositoryPathSegment, fileName);

        Directory.CreateDirectory(Path.GetDirectoryName(repositoryPath)!);
        PersistBinary(repositoryPath, request);
        var extractedText = await EnsureExtractedTextAsync(request, fileName, repositoryPath, initialText, cancellationToken);
        var summary = DocumentTextHelpers.BuildSummary(fileName, classification, extractedText);

        var document = _catalog.Add(new CatalogDocument
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

        try
        {
            await _indexer.IndexDocumentAsync(document, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"[Document intake] Indexed metadata for {fileName}, but chunk indexing failed: {ex.Message}");
        }

        return new IntakeRegisterResponse(documentId, repositoryPath.Replace('\\', '/'), classification.Category, request.Visibility, _clock.UtcNow);
    }

    private async Task<string> BuildInitialExtractedTextAsync(IntakeRegisterRequest request, string fileName, CancellationToken cancellationToken)
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

        if (!string.IsNullOrWhiteSpace(request.OriginalFilePath) && File.Exists(request.OriginalFilePath))
        {
            try
            {
                var parsed = await _parser.ParseAsync(request.OriginalFilePath, cancellationToken);
                if (!LooksLikePlaceholder(parsed, fileName))
                {
                    return parsed;
                }
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    private async Task<string> EnsureExtractedTextAsync(
        IntakeRegisterRequest request,
        string fileName,
        string repositoryPath,
        string initialText,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(initialText))
        {
            return initialText;
        }

        try
        {
            var parsed = await _parser.ParseAsync(repositoryPath, cancellationToken);
            if (!LooksLikePlaceholder(parsed, fileName))
            {
                return parsed;
            }
        }
        catch
        {
        }

        try
        {
            var ocr = await _ocr.ExtractTextAsync(repositoryPath, cancellationToken);
            if (!LooksLikePlaceholder(ocr, fileName))
            {
                return ocr;
            }
        }
        catch
        {
        }

        return $"Registered file {fileName} from {request.OriginalFilePath}.";
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

    private static bool LooksLikePlaceholder(string? text, string fileName)
    {
        return DocumentTextHelpers.LooksLikePlaceholder(text, fileName);
    }
}

public static class DocumentTextHelpers
{
    public static string BuildSummary(string fileName, ClassificationResult classification, string extractedText)
    {
        var snippet = extractedText.Length > 160 ? extractedText[..160] + "..." : extractedText;
        return $"{fileName} filed under {classification.Category} for {classification.CustomerName}. {snippet}".Trim();
    }

    public static bool LooksLikePlaceholder(string? text, string fileName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var trimmed = text.Trim();
        return trimmed.StartsWith("placeholder extracted text from", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals($"Registered file {fileName}.", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals($"OCR placeholder extracted text from {fileName}", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals($"Parser placeholder extracted text from {fileName}", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals($"Registered file {fileName} from .", StringComparison.OrdinalIgnoreCase);
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

        // Fast path: instant replies for simple social phrases (hi, hello, who are you).
        // Everything else — including email drafts, document questions, tasks — must reach the LLM.
        if (TryBuildInstantResponse(profile, request.Message, out var instantResponse))
        {
            _conversations.Append(session.UserId, MessageRole.Assistant, instantResponse);
            return new ChatResponse(
                instantResponse,
                Array.Empty<CitationDto>(),
                new[] { new SuggestedActionDto(SuggestedActionType.RequestAccess, "Request access to a library", null) });
        }

        // Retrieve authorized documents to provide as grounded context to the LLM.
        var citations = await _retrieval.RetrieveAsync(session, request.Message, cancellationToken);

        var context = citations.Count == 0
            ? "No authorized document citations were found for this query."
            : string.Join(Environment.NewLine, citations.Select(c => $"- {c.Title}: {c.Snippet}"));

        var prompt = $"""
            You are {profile.BotName}, a warm professional office assistant for {profile.DisplayName}.
            Work style: {profile.WorkStyle}.
            Preferred language: {profile.PreferredLanguage}.
            Respond helpfully and completely. When asked to compose an email, letter, or document, produce the full text.
            Cite document titles naturally when you use them. Never invent access to files not listed in the context below.

            User message:
            {request.Message}

            Authorized context:
            {context}
            """;

        var responseText = await _llm.GenerateResponseAsync(session, profile, prompt, cancellationToken);
        _conversations.Append(session.UserId, MessageRole.Assistant, responseText);

        return new ChatResponse(responseText, citations, BuildSuggestedActions(citations));
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

    public AssistantRuntimeRouter(
        IOptions<AiCanOptions> options,
        IAssistantOrchestrator builtIn,
        OpenClawAssistantRuntime openClaw)
    {
        _options = options;
        _builtIn = builtIn;
        _openClaw = openClaw;
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
            You have access to AiCan's authorized retrieval context below and should answer from it whenever relevant.
            When the employee uploads or ingests a file into AiCan, treat it as available only if it appears in the authorized context below.
            Never claim access beyond the authorized AiCan context below.
            Cite the file titles naturally in your answer when you rely on them.
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
    private string? _cachedSoulContent;
    private readonly object _soulLock = new();

    public LmStudioProvider(IHttpClientFactory httpClientFactory, IOptions<AiCanOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    /// <summary>
    /// Loads SOUL.md, IDENTITY.md, and AGENTS.md from the configured workspace directory.
    /// The result is cached after the first successful load.
    /// Falls back to a sensible generic soul if the files are missing or the path is not set.
    /// </summary>
    private string LoadSoulContent()
    {
        lock (_soulLock)
        {
            if (_cachedSoulContent is not null)
            {
                return _cachedSoulContent;
            }

            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(_options.WorkspaceRoot))
            {
                var root = Path.GetFullPath(_options.WorkspaceRoot);
                foreach (var fileName in new[] { "SOUL.md", "IDENTITY.md", "AGENTS.md" })
                {
                    var path = Path.Combine(root, fileName);
                    if (File.Exists(path))
                    {
                        var content = File.ReadAllText(path).Trim();
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            parts.Add(content);
                        }
                    }
                }
            }

            if (parts.Count == 0)
            {
                parts.Add("You are a warm, professional, and trustworthy workplace assistant.\nBe helpful, grounded, and never fabricate document access or citations.");
            }

            _cachedSoulContent = string.Join("\n\n---\n\n", parts);
            return _cachedSoulContent;
        }
    }

    /// <summary>
    /// Builds the LLM system message by combining the OpenClaw soul/identity files
    /// with this employee's personal profile (name, department, tone).
    /// This is what gives the bot its warm, familiar feel per employee.
    /// </summary>
    private string BuildSystemMessage(AssistantProfileDto profile, SessionExchangeResponse session)
    {
        var soul = LoadSoulContent();
        return $"""
            {soul}

            ---

            You are currently acting as {profile.BotName}, the personal assistant for {profile.DisplayName} ({session.Email}).
            Department: {session.Department}. Role: {session.Role}.
            Preferred language: {profile.PreferredLanguage}. Work style: {profile.WorkStyle}. Tone: {profile.Tone}.
            Always stay within the authorized context the user has been given.
            Never claim access to documents that were not provided in the authorized context below.
            When the employee uploads files, treat them as available only when they appear in the authorized context.
            """;
    }

    public async Task<string> GenerateResponseAsync(SessionExchangeResponse session, AssistantProfileDto profile, string prompt, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(nameof(LmStudioProvider));
        client.BaseAddress = new Uri(_options.LmStudioBaseUrl.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var systemMessage = BuildSystemMessage(profile, session);

        var payload = new
        {
            model = _options.LmStudioModel,
            temperature = 0.3,
            messages = new[]
            {
                new { role = "system", content = systemMessage },
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
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.Error.WriteLine($"[LmStudio] {response.StatusCode}: {errorBody}");
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"[LmStudio] Error calling {_options.LmStudioBaseUrl}: {ex.Message}");
            return BuildFallback(profile, prompt);
        }
    }

    private static string BuildFallback(AssistantProfileDto profile, string prompt)
    {
        return $"{profile.BotName}: I could not reach the LM Studio brain right now. Please check that LM Studio is running on the Mac and the model is loaded. Prompt excerpt: {prompt[..Math.Min(prompt.Length, 180)]}";
    }
}

public sealed class WorkerEmbeddingProvider : IEmbeddingProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiWorkerOptions _options;

    public WorkerEmbeddingProvider(IHttpClientFactory httpClientFactory, IOptions<AiCanOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value.AiWorker;
    }

    public async Task<float[]> EmbedAsync(string content, EmbeddingInputType inputType, CancellationToken cancellationToken)
    {
        var values = await EmbedManyAsync([content], inputType, cancellationToken);
        return values[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedManyAsync(IReadOnlyList<string> contents, EmbeddingInputType inputType, CancellationToken cancellationToken)
    {
        if (contents.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        var client = _httpClientFactory.CreateClient(nameof(WorkerEmbeddingProvider));
        client.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");

        using var response = await client.PostAsJsonAsync(
            "embed-batch",
            new
            {
                texts = contents,
                inputType = inputType is EmbeddingInputType.Query ? "query" : "passage",
                model = _options.EmbeddingModel
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Embedding worker returned {(int)response.StatusCode}: {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var embeddings = new List<float[]>();
        foreach (var item in document.RootElement.GetProperty("embeddings").EnumerateArray())
        {
            var values = item.EnumerateArray().Select(value => value.GetSingle()).ToArray();
            embeddings.Add(values);
        }

        return embeddings;
    }
}

public sealed class WorkerExtractionProvider : IOcrProvider, IParserProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiWorkerOptions _options;

    public WorkerExtractionProvider(IHttpClientFactory httpClientFactory, IOptions<AiCanOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value.AiWorker;
    }

    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken)
        => ExtractAsync(filePath, cancellationToken);

    public Task<string> ParseAsync(string filePath, CancellationToken cancellationToken)
        => ExtractAsync(filePath, cancellationToken);

    private async Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(nameof(WorkerExtractionProvider));
        client.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");

        using var response = await client.PostAsJsonAsync(
            "extract",
            new
            {
                file_path = filePath
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Extraction worker returned {(int)response.StatusCode}: {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.GetProperty("extracted_text").GetString() ?? string.Empty;
    }
}

public sealed class QdrantVectorStore : IVectorStore
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly QdrantOptions _options;
    private readonly SemaphoreSlim _ensureLock = new(1, 1);
    private volatile bool _collectionEnsured;

    public QdrantVectorStore(IHttpClientFactory httpClientFactory, IOptions<AiCanOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value.Qdrant;
    }

    public async Task UpsertAsync(IReadOnlyList<ChunkVectorRecord> chunks, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || chunks.Count == 0)
        {
            return;
        }

        await EnsureCollectionAsync(cancellationToken);

        var client = CreateClient();
        using var response = await client.PutAsJsonAsync(
            $"collections/{_options.CollectionName}/points",
            new
            {
                points = chunks.Select(chunk => new
                {
                    id = chunk.ChunkId,
                    vector = chunk.Vector,
                    payload = new
                    {
                        document_id = chunk.DocumentId,
                        chunk_index = chunk.ChunkIndex,
                        title = chunk.Title,
                        department = chunk.Department,
                        visibility = chunk.Visibility.ToString(),
                        owner_user_id = chunk.OwnerUserId,
                        classification = chunk.Classification,
                        repository_path = chunk.RepositoryPath,
                        summary = chunk.Summary,
                        text = chunk.Text,
                        access_tags = BuildAccessTags(chunk).ToArray()
                    }
                }).ToArray()
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Qdrant upsert failed with {(int)response.StatusCode}: {errorBody}");
        }
    }

    public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(float[] queryVector, SessionExchangeResponse session, int limit, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return Array.Empty<VectorSearchHit>();
        }

        await EnsureCollectionAsync(cancellationToken);

        var client = CreateClient();
        var effectiveLimit = Math.Max(Math.Max(limit, 1), _options.SearchLimit);
        using var response = await client.PostAsJsonAsync(
            $"collections/{_options.CollectionName}/points/search",
            new
            {
                vector = queryVector,
                limit = effectiveLimit,
                with_payload = true,
                filter = new
                {
                    should = new object[]
                    {
                        new { key = "access_tags", match = new { value = "common" } },
                        new { key = "access_tags", match = new { value = $"dept:{NormalizeTag(session.Department)}" } },
                        new { key = "access_tags", match = new { value = $"user:{session.UserId:D}" } }
                    }
                }
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Qdrant search failed with {(int)response.StatusCode}: {errorBody}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("result", out var resultsElement) || resultsElement.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<VectorSearchHit>();
        }

        var hits = new List<VectorSearchHit>();
        foreach (var item in resultsElement.EnumerateArray())
        {
            if (!item.TryGetProperty("payload", out var payload))
            {
                continue;
            }

            hits.Add(new VectorSearchHit(
                ReadGuid(item, "id"),
                ReadGuid(payload, "document_id"),
                payload.GetProperty("chunk_index").GetInt32(),
                payload.GetProperty("title").GetString() ?? "Untitled",
                payload.GetProperty("repository_path").GetString() ?? string.Empty,
                payload.GetProperty("text").GetString() ?? string.Empty,
                payload.GetProperty("summary").GetString() ?? string.Empty,
                item.GetProperty("score").GetDouble()));
        }

        return hits;
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient(nameof(QdrantVectorStore));
        client.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        return client;
    }

    private async Task EnsureCollectionAsync(CancellationToken cancellationToken)
    {
        if (_collectionEnsured || !_options.Enabled)
        {
            return;
        }

        await _ensureLock.WaitAsync(cancellationToken);
        try
        {
            if (_collectionEnsured)
            {
                return;
            }

            var client = CreateClient();
            using var probe = await client.GetAsync($"collections/{_options.CollectionName}", cancellationToken);
            if (probe.IsSuccessStatusCode)
            {
                _collectionEnsured = true;
                return;
            }

            if (probe.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                var probeBody = await probe.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Qdrant collection probe failed with {(int)probe.StatusCode}: {probeBody}");
            }

            using var create = await client.PutAsJsonAsync(
                $"collections/{_options.CollectionName}",
                new
                {
                    vectors = new
                    {
                        size = _options.VectorSize,
                        distance = "Cosine"
                    },
                    on_disk_payload = true
                },
                cancellationToken);

            if (!create.IsSuccessStatusCode)
            {
                var body = await create.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"Qdrant collection creation failed with {(int)create.StatusCode}: {body}");
            }

            _collectionEnsured = true;
        }
        finally
        {
            _ensureLock.Release();
        }
    }

    private static IEnumerable<string> BuildAccessTags(ChunkVectorRecord chunk)
    {
        yield return chunk.Visibility switch
        {
            VisibilityScope.CommonShared => "common",
            VisibilityScope.DepartmentShared => $"dept:{NormalizeTag(chunk.Department)}",
            VisibilityScope.Private => $"user:{chunk.OwnerUserId:D}",
            _ => $"user:{chunk.OwnerUserId:D}"
        };
    }

    private static string NormalizeTag(string value)
    {
        return value.Trim().Replace(' ', '-').ToLowerInvariant();
    }

    private static Guid ReadGuid(JsonElement element, string propertyName)
    {
        var value = element.GetProperty(propertyName).GetString();
        return Guid.TryParse(value, out var parsed) ? parsed : Guid.Empty;
    }
}
