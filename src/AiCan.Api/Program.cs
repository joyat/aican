using AiCan.Api;
using AiCan.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AiCanOptions>(builder.Configuration.GetSection("AiCan"));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<SessionContextAccessor>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IUserDirectory, InMemoryUserDirectory>();
builder.Services.AddSingleton<IAssistantProfileStore, InMemoryAssistantProfileStore>();
builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
builder.Services.AddSingleton<IDocumentCatalog, InMemoryDocumentCatalog>();
builder.Services.AddSingleton<IAuditLog, InMemoryAuditLog>();
builder.Services.AddSingleton<IRetrievalService, RetrievalService>();
builder.Services.AddSingleton<IClassificationService, FilingClassificationService>();
builder.Services.AddSingleton<IDocumentIntakeService, DocumentIntakeService>();
builder.Services.AddSingleton<IAssistantOrchestrator, AssistantOrchestrator>();
builder.Services.AddSingleton<IEmbeddingProvider, DeterministicEmbeddingProvider>();
builder.Services.AddSingleton<IOcrProvider, NullOcrProvider>();
builder.Services.AddSingleton<IParserProvider, NullParserProvider>();
builder.Services.AddSingleton<IVectorStore, InMemoryVectorStore>();
builder.Services.AddSingleton<ILLMProvider, LmStudioProvider>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapPost("/session/exchange", (
    SessionExchangeRequest request,
    IUserDirectory users,
    IAssistantProfileStore profiles,
    IAuditLog audit,
    IClock clock) =>
{
    var session = users.Exchange(request);
    profiles.UpsertFromSession(session, request);
    audit.Write(session.UserId, "session.exchange", request.Email, clock.UtcNow);
    return Results.Ok(session);
});

app.MapGet("/assistant/profile", (
    HttpRequest request,
    IUserDirectory users,
    IAssistantProfileStore profiles) =>
{
    var session = users.RequireSession(request);
    var profile = profiles.Get(session.UserId);
    return profile is null ? Results.NotFound() : Results.Ok(profile);
});

app.MapPatch("/assistant/profile", (
    HttpRequest request,
    UpdateAssistantProfileRequest update,
    IUserDirectory users,
    IAssistantProfileStore profiles,
    IAuditLog audit,
    IClock clock) =>
{
    var session = users.RequireSession(request);
    var profile = profiles.Update(session.UserId, update);
    audit.Write(session.UserId, "assistant.profile.updated", profile.BotName, clock.UtcNow);
    return Results.Ok(profile);
});

app.MapGet("/assistant/history", (
    HttpRequest request,
    IUserDirectory users,
    IConversationStore conversations) =>
{
    var session = users.RequireSession(request);
    return Results.Ok(conversations.GetHistory(session.UserId));
});

app.MapPost("/assistant/chat", async (
    HttpRequest request,
    ChatRequest chat,
    IUserDirectory users,
    IAssistantOrchestrator assistant,
    IAuditLog audit,
    IClock clock,
    CancellationToken cancellationToken) =>
{
    var session = users.RequireSession(request);
    var response = await assistant.RespondAsync(session, chat, cancellationToken);
    audit.Write(session.UserId, "assistant.chat", chat.Message, clock.UtcNow);
    return Results.Ok(response);
});

app.MapPost("/documents/intake/register", async (
    HttpRequest request,
    IntakeRegisterRequest register,
    IUserDirectory users,
    IDocumentIntakeService intake,
    IAuditLog audit,
    IClock clock,
    CancellationToken cancellationToken) =>
{
    var session = users.RequireSession(request);
    var response = await intake.RegisterAsync(session, register, cancellationToken);
    audit.Write(session.UserId, "document.intake.registered", response.RepositoryPath, clock.UtcNow);
    return Results.Ok(response);
});

app.MapPost("/actions/access-request", (
    HttpRequest request,
    AccessRequestDto action,
    IUserDirectory users,
    IAuditLog audit,
    IClock clock) =>
{
    var session = users.RequireSession(request);
    var response = new ActionResponse(Guid.NewGuid(), "requested", clock.UtcNow);
    audit.Write(session.UserId, "document.access.requested", action.DocumentId.ToString(), clock.UtcNow);
    return Results.Ok(response);
});

app.MapPost("/actions/reclassification-suggest", (
    HttpRequest request,
    ReclassificationSuggestionRequest action,
    IUserDirectory users,
    IDocumentCatalog catalog,
    IAuditLog audit,
    IClock clock) =>
{
    var session = users.RequireSession(request);
    catalog.MarkSuggestion(action.DocumentId, action.ProposedCategory, action.Reason);
    var response = new ActionResponse(Guid.NewGuid(), "submitted", clock.UtcNow);
    audit.Write(session.UserId, "document.reclassification.suggested", action.DocumentId.ToString(), clock.UtcNow);
    return Results.Ok(response);
});

app.Run();
