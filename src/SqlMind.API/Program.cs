using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SqlMind.Agent;
using SqlMind.Infrastructure;
using SqlMind.Infrastructure.Cache;
using SqlMind.Infrastructure.Jobs;
using SqlMind.Infrastructure.LLM;
using SqlMind.Infrastructure.RAG;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);
var cfg     = builder.Configuration;
var disableAuth = builder.Environment.IsDevelopment() && cfg.GetValue<bool>("DisableAuth");

// Startup diagnostic — will be removed after smoke test passes
Console.WriteLine($"[DIAG] GEMINI_API_KEY present: {!string.IsNullOrEmpty(cfg["GEMINI_API_KEY"])}");
Console.WriteLine($"[DIAG] Environment: {builder.Environment.EnvironmentName}");

// ── Controllers + JSON ────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy =
        System.Text.Json.JsonNamingPolicy.SnakeCaseLower);

// ── Swagger/OpenAPI ───────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// ── Authentication ────────────────────────────────────────────────────────────
if (disableAuth)
{
    // Development bypass: any request is authenticated as "dev-user"
    builder.Services.AddAuthentication("DevBypass")
        .AddScheme<AuthenticationSchemeOptions, DevBypassHandler>("DevBypass", null);
    builder.Services.AddAuthorization(o =>
        o.FallbackPolicy = o.DefaultPolicy =
            new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder("DevBypass")
                .RequireAuthenticatedUser()
                .Build());
}
else
{
    var jwtSecret = cfg["Jwt:Secret"]
        ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidateAudience         = true,
                ValidateLifetime         = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer              = cfg["Jwt:Issuer"]  ?? "sqlmind",
                ValidAudience            = cfg["Jwt:Audience"] ?? "sqlmind-api",
                IssuerSigningKey         = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtSecret)),
            };
        });

    builder.Services.AddAuthorization();
}

// ── Domain infrastructure ─────────────────────────────────────────────────────
builder.Services.AddRagPipeline();         // DbContext, IEmbeddingService, IRagService
builder.Services.AddGeminiLLMClient();    // ILLMClient
builder.Services.AddSqlAnalysis();        // ISqlAnalyzer, IRiskEvaluator
builder.Services.AddAgentServices();      // IPolicyEngine, ITool×3, IToolExecutor
builder.Services.AddRepositories();       // IAnalysisJobRepository, IAnalysisResultRepository, IAuditLogRepository
builder.Services.AddAnalysisOrchestrator(); // AgentOrchestrator, AnalysisOrchestrator

// ── Redis cache ───────────────────────────────────────────────────────────────
builder.Services.AddRedisCache();

// ── Hangfire ──────────────────────────────────────────────────────────────────
var hangfireConnStr =
    cfg["DATABASE_URL"]
    ?? cfg.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=sqlmind;Username=postgres;Password=postgres";

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(hangfireConnStr)));

builder.Services.AddHangfireServer(o =>
{
    o.WorkerCount  = 4;
    o.Queues       = ["default"];
});

builder.Services.AddHangfireJobService(); // IBackgroundJobService

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHangfireDashboard("/hangfire"); // visible only in dev
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

/// <summary>
/// Development-only authentication handler that accepts every request as "dev-user".
/// Active only when DisableAuth=true in appsettings.Development.json / appsettings.json.
/// </summary>
public sealed class DevBypassHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public DevBypassHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims  = new[] { new Claim(ClaimTypes.Name, "dev-user"), new Claim(ClaimTypes.Role, "admin") };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket   = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
