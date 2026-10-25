using System.Text.Json.Serialization;
using Serilog;
using SnipCacheAuth;
using SnipCacheEditor;

var builder = WebApplication.CreateBuilder(args);

// sd_notify(READY=1) under systemd (Type=notify); no-op otherwise.
builder.Host.UseSystemd();

builder.AddSnipCacheLogging();

// Spec at /openapi/v1.json in development; also emitted to openapi/openapi.json on build.
builder.Services.AddOpenApi();

// Numbers are numbers: without this the web defaults accept numeric strings and the
// spec types every integer as "integer | string".
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

// BFF auth (cookie + OIDC PKCE against Keycloak), CORS, Data Protection. Degrades to
// cookie-only without /auth/* when Keycloak:Authority is unset.
builder.Services.AddSnipCacheAuth(builder.Configuration, builder.Environment);
builder.Services.AddEditorAuthorization(builder.Configuration);
// X-Api-Key scheme and policy for /bot (Bot:ApiKey).
builder.Services.AddBotAuthentication(builder.Configuration);

// GitHub (Octokit) or the in-memory fake, per GitHub:UseFake.
builder.Services.AddContentRepository(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ContentExceptionHandler>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.WarnIfGitHubTokenExpiresSoon();
app.WarnIfBotApiKeyMissing();

// One line per request, early so auth and CSRF are timed too.
app.UseSerilogRequestLogging();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();

// CORS → authentication → authorization → CSRF guard (mutating /api calls need X-CSRF).
app.UseSnipCacheAuth();
app.MapSnipCacheAuthEndpoints();

var authConfigured = app.Services.GetRequiredService<SnipCacheAuthOptions>().AuthConfigured;
if (app.Environment.IsDevelopment() && !authConfigured)
    DevAuthEndpoints.Map(app);

app.MapHealthChecks("/health");

var api = app.MapGroup("/api")
    .RequireAuthorization(EditorAuthorization.PolicyName)
    .WithTags("Editor")
    .ProducesProblem(StatusCodes.Status401Unauthorized)
    .ProducesProblem(StatusCodes.Status403Forbidden);
ConfigEndpoints.Map(api);
ContentEndpoints.Map(api);

// API-key auth, no cookie and no CSRF header.
BotEndpoints.Map(app);

app.Run();

// Entry point for WebApplicationFactory<Program> in the tests.
public partial class Program;
