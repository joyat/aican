namespace AiCan.Contracts;

public enum UserRole
{
    PlatformAdmin,
    DocAdmin,
    User
}

public enum VisibilityScope
{
    Private,
    DepartmentShared,
    CommonShared
}

public enum MessageRole
{
    System,
    User,
    Assistant
}

public enum SuggestedActionType
{
    OpenDocument,
    RequestAccess,
    ViewSource,
    Reclassify
}

public sealed record SessionExchangeRequest(
    string Email,
    string DisplayName,
    string? BotName,
    string? Department,
    string? M365AccessToken);

public sealed record SessionExchangeResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    string BotName,
    string Department,
    UserRole Role,
    string SessionToken);

public sealed record AssistantProfileDto(
    Guid UserId,
    string Email,
    string DisplayName,
    string BotName,
    string Department,
    string Tone,
    string WorkStyle,
    string PreferredLanguage,
    UserRole Role);

public sealed record UpdateAssistantProfileRequest(
    string BotName,
    string Tone,
    string WorkStyle,
    string PreferredLanguage);

public sealed record HistoryMessageDto(
    Guid Id,
    Guid UserId,
    MessageRole Role,
    string Content,
    DateTimeOffset CreatedAtUtc);

public sealed record CitationDto(
    Guid DocumentId,
    string Title,
    string RepositoryPath,
    string Snippet);

public sealed record SuggestedActionDto(
    SuggestedActionType Type,
    string Label,
    string? TargetId);

public sealed record ChatRequest(
    string Message);

public sealed record ChatResponse(
    string Message,
    IReadOnlyList<CitationDto> Citations,
    IReadOnlyList<SuggestedActionDto> SuggestedActions);

public sealed record IntakeRegisterRequest(
    string OriginalFilePath,
    string FileName,
    string Department,
    VisibilityScope Visibility,
    string? DeclaredCategory,
    string? OwnerEmail,
    string? CustomerName,
    string? FileContentBase64,
    string? ExtractedText);

public sealed record IntakeRegisterResponse(
    Guid DocumentId,
    string RepositoryPath,
    string Classification,
    VisibilityScope Visibility,
    DateTimeOffset RegisteredAtUtc);

public sealed record DocumentSearchRequest(
    string Query);

public sealed record DocumentSearchResultDto(
    Guid DocumentId,
    string Title,
    string Department,
    VisibilityScope Visibility,
    string Classification,
    string RepositoryPath,
    string Summary,
    string? SuggestedCategory);

public sealed record DocumentSearchResponse(
    IReadOnlyList<DocumentSearchResultDto> Results);

public sealed record DocumentDetailDto(
    Guid DocumentId,
    string Title,
    string Department,
    VisibilityScope Visibility,
    string Classification,
    string RepositoryPath,
    string Summary,
    string? SuggestedCategory);

public sealed record AccessRequestDto(
    Guid DocumentId,
    string Reason);

public sealed record ReclassificationSuggestionRequest(
    Guid DocumentId,
    string ProposedCategory,
    string Reason);

public sealed record ActionResponse(
    Guid ActionId,
    string Status,
    DateTimeOffset CreatedAtUtc);
