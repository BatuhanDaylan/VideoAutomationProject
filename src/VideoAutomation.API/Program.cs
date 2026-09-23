using Hangfire;
using Hangfire.Storage.SQLite;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Serilog;
using VideoAutomation.API.Middleware;
using VideoAutomation.API.Security;
using VideoAutomation.Infrastructure.Security;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Application.Interfaces.Agents;
using VideoAutomation.Application.Services;
using VideoAutomation.Infrastructure.Agents;
using VideoAutomation.Infrastructure.AI;
using VideoAutomation.Infrastructure.Media;
using VideoAutomation.Infrastructure.Persistence;
using VideoAutomation.Infrastructure.Publishing;
using VideoAutomation.Infrastructure.Scrapers;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ──────────────────────────────────────────────────────
builder.Host.UseSerilog((context, config) =>
{
    config
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: "logs/videoautomation-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30);
});

// ── Configuration ────────────────────────────────────────────────
// Load .env file if it exists
var envPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env");
if (File.Exists(envPath))
{
    foreach (var line in File.ReadAllLines(envPath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
        var parts = line.Split('=', 2);
        if (parts.Length == 2)
            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
    }
}

// Override appsettings with environment variables
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["ApiKeys:GeminiApiKey"] = Environment.GetEnvironmentVariable("GEMINI_API_KEY"),
    ["ApiKeys:PexelsApiKey"] = Environment.GetEnvironmentVariable("PEXELS_API_KEY"),
    ["ApiKeys:ApifyApiToken"] = Environment.GetEnvironmentVariable("APIFY_API_TOKEN"),
    ["ApiKeys:AiVideoApiToken"] = Environment.GetEnvironmentVariable("AI_VIDEO_API_TOKEN"),
    ["ApiKeys:TelegramBotToken"] = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN"),
    ["ApiKeys:TelegramChatId"] = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID"),
    ["ApiKeys:YouTubeApiKey"] = Environment.GetEnvironmentVariable("YOUTUBE_API_KEY"),
    ["YouTube:ClientId"] = Environment.GetEnvironmentVariable("YOUTUBE_CLIENT_ID"),
    ["YouTube:ClientSecret"] = Environment.GetEnvironmentVariable("YOUTUBE_CLIENT_SECRET"),
    ["Security:EncryptionMasterKey"] = Environment.GetEnvironmentVariable("ENCRYPTION_MASTER_KEY"),
    ["Auth:JwtSecret"] = Environment.GetEnvironmentVariable("JWT_SECRET"),
});

// ── Gemini key pool (per-user, multi-key quota failover) ─────────
// Keys are per-user now (stored encrypted, entered in the UI), so the pool is built per user on demand:
// GeminiKeyPoolManager caches one pool per user (keeping each user's 429/403 cooldown state). GeminiClient
// resolves the current user's pool from it via IUserSecrets.
builder.Services.AddSingleton<GeminiKeyPoolManager>();

// ── EF Core + SQLite ─────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── CORS (React dev server) ──────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ── SignalR ──────────────────────────────────────────────────────
builder.Services.AddSignalR();

// ── Controllers + JSON ───────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Video Automation API", Version = "v1" });
});

// ── HttpClient ───────────────────────────────────────────────────
builder.Services.AddHttpClient("Gemini", client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(120);
});

builder.Services.AddHttpClient("Pexels", client =>
{
    client.BaseAddress = new Uri("https://api.pexels.com/");
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddHttpClient("Apify", client =>
{
    client.BaseAddress = new Uri("https://api.apify.com/");
    client.Timeout = TimeSpan.FromSeconds(120);
});

builder.Services.AddHttpClient("AiVideo", client =>
{
    // aimlapi.com fronts multiple video-generation models (currently Kling) behind one API —
    // AiVideoClient reads the actual model name from config, this is just the transport.
    client.BaseAddress = new Uri("https://api.aimlapi.com/");
    client.Timeout = TimeSpan.FromSeconds(90);
});

builder.Services.AddHttpClient("Veo", client =>
{
    // Google Veo via the Gemini API — same key as GeminiClient, different auth convention
    // (x-goog-api-key header instead of ?key= query param) and an async LRO poll flow.
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");
    client.Timeout = TimeSpan.FromSeconds(120);
});

builder.Services.AddHttpClient("Telegram", client =>
{
    client.BaseAddress = new Uri("https://api.telegram.org/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHttpClient("YouTube", client =>
{
    client.BaseAddress = new Uri("https://www.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(120);
});

// ── DI services — Faz 2 (Scout Agent) ────────────────────────────
builder.Services.AddScoped<YouTubeTranscriptService>();
builder.Services.AddScoped<IScraper, YouTubeScraper>();
// TikTokTranscriptService removed: TikTok is handled via the manual-description flow now, so the paid
// clockworks/tiktok-transcript-extractor Apify actor is no longer used.
builder.Services.AddScoped<IScraper, TikTokScraper>();
builder.Services.AddScoped<IScraper, InstagramScraper>();
builder.Services.AddScoped<IViralityScorer, ViralityScorer>();
builder.Services.AddScoped<IScoutAgent, ScoutAgent>();

// ── DI services — Faz 3 (Analyzer Agent) ─────────────────────────
builder.Services.AddScoped<UsageTracker>();
builder.Services.AddScoped<GeminiClient>();
builder.Services.AddScoped<IContentGenerator, GeminiContentGenerator>();
builder.Services.AddScoped<IAnalyzerAgent, AnalyzerAgent>();

// ── DI services — Faz 4 (Generator Agent) ────────────────────────
builder.Services.AddScoped<FFmpegRunner>();
builder.Services.AddScoped<EdgeTtsService>();
builder.Services.AddScoped<IStockVideoProvider, PexelsStockVideoProvider>();
builder.Services.AddScoped<AiVideoClient>();
// AiVideoStockProvider (aimlapi/Kling) not registered — AI_VIDEO_API_TOKEN was never provided.
// VeoVideoProvider (Google Veo via the Gemini API, same key as GeminiClient) is used for
// VisualMode.AIGenerated instead. Swap back by registering AiVideoStockProvider here instead —
// GeneratorAgent.SelectProvider just matches on SupportedMode, so only one AIGenerated
// provider should be registered at a time.
builder.Services.AddScoped<VeoClient>();
builder.Services.AddScoped<IStockVideoProvider, VeoVideoProvider>();
builder.Services.AddScoped<IVideoComposer, FFmpegVideoComposer>();
builder.Services.AddScoped<IGeneratorAgent, GeneratorAgent>();

// ── DI services — Faz 5 (Publisher Agent: YouTube + Telegram approval) ──
// TelegramNotifier is registered as itself too (not just as INotifier) — TelegramUpdatePollingService
// needs UpdateApprovalMessageAsync, which isn't part of the INotifier contract (that's YouTube/TikTok/
// Instagram-agnostic; editing "the approval message" is Telegram-specific).
builder.Services.AddScoped<TelegramNotifier>();
builder.Services.AddScoped<INotifier>(sp => sp.GetRequiredService<TelegramNotifier>());
// Registered as the concrete type too so YouTubeAuthController can call the interactive
// AuthorizeInteractivelyAsync/IsAuthorizedAsync (one-time OAuth consent), which aren't on IPublisher.
builder.Services.AddScoped<YouTubePublisher>();
builder.Services.AddScoped<IPublisher>(sp => sp.GetRequiredService<YouTubePublisher>());
builder.Services.AddScoped<IPublisherAgent, PublisherAgent>();
// Interactive autonomous Telegram flow (peak-hour candidates → review → tool → generate → publish).
builder.Services.AddScoped<TelegramConversationService>();
builder.Services.AddHostedService<TelegramUpdatePollingService>();
// Performance feedback: fetch published videos' public stats (views/likes/comments) via API key.
builder.Services.AddScoped<YouTubeStatsService>();
builder.Services.AddScoped<PublicationStatsRefresher>();
// ── TODO: Instagram/TikTok IPublisher implementations (Faz 5 continued) ──

// ── DI services — Faz 6 (Orchestration) ──────────────────────────
builder.Services.AddScoped<IPipelineOrchestrator, PipelineOrchestrator>();
// Content calendar: runs the full pipeline at a scheduled time (Hangfire delayed job).
builder.Services.AddScoped<ScheduleRunnerService>();

// Separate SQLite file from videoautomation.db on purpose: Hangfire owns and migrates its own schema
// (Job/JobParameter/Set/... tables) — sharing our EF-managed file risks EnsureCreatedAsync and Hangfire's
// own storage initialization stepping on each other.
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSQLiteStorage("hangfire.db"));
builder.Services.AddHangfireServer();

// ── Auth: password hashing, API-key encryption, JWT ──────────────
builder.Services.AddSingleton<ICryptoService>(_ =>
    new AesCryptoProvider(builder.Configuration["Security:EncryptionMasterKey"]
        ?? throw new InvalidOperationException("ENCRYPTION_MASTER_KEY yapılandırılmamış.")));
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<JwtTokenService>();

// Per-user API keys: resolve the current user (JWT for HTTP, active user for background jobs) and decrypt.
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IActiveUserProvider, ActiveUserProvider>();
builder.Services.AddScoped<ICurrentUserId, CurrentUserId>();
builder.Services.AddScoped<IUserSecrets, UserSecrets>();

var jwtValidation = new JwtTokenService(builder.Configuration).ValidationParameters;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = jwtValidation);
// Every endpoint requires a logged-in user by default; [AllowAnonymous] opts register/login out.
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var app = builder.Build();

// ── Startup config validation (fail-fast on invalid config, warn on missing optional keys) ──
VideoAutomation.API.StartupValidator.Validate(app.Configuration, app.Logger);

// ── Middleware ────────────────────────────────────────────────────
app.UseMiddleware<ErrorHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseCors("AllowReact");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
// app.MapHub<ChatHub>("/hub/chat"); // Will be added in Faz 7

// No auth filter: this app runs locally only, never exposed publicly — the default Hangfire dashboard
// (unauthenticated by default outside localhost) would need one before that stops being true.
app.UseHangfireDashboard("/hangfire");

// ── Auto-migrate database ────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    Log.Information("✅ Database initialized successfully");

    // Seed the active user to the most recent account so peak-hour/pipeline background jobs have someone to
    // run as after a restart (before anyone logs in). A login overrides this.
    var lastUser = await db.Users.OrderByDescending(u => u.Id).Select(u => (int?)u.Id).FirstOrDefaultAsync();
    if (lastUser is { } uid)
        app.Services.GetRequiredService<IActiveUserProvider>().ActiveUserId = uid;
}

// ── Faz 6: daily pipeline recurring job ──────────────────────────
// Disabled by default: the daily auto-pipeline generates a video and sends an approve/reject Telegram
// message, which overlaps/conflicts with the interactive peak-hour candidate flow (12/19/21). The
// orchestrator stays available on demand via POST /api/pipeline/run-now. Set Pipeline:DailyScheduleEnabled
// = true to bring the daily cron back.
var pipelineRegion = builder.Configuration.GetValue("Pipeline:Region", "TR")!;
if (builder.Configuration.GetValue("Pipeline:DailyScheduleEnabled", false))
{
    var cronSchedule = builder.Configuration.GetValue("Pipeline:CronSchedule", "0 9 * * *")!;
    RecurringJob.AddOrUpdate<IPipelineOrchestrator>(
        "daily-pipeline-run",
        orchestrator => orchestrator.RunAsync(pipelineRegion, CancellationToken.None),
        cronSchedule);
    Log.Information("📅 Günlük pipeline zamanlandı: {Cron}", cronSchedule);
}
else
{
    RecurringJob.RemoveIfExists("daily-pipeline-run");  // drop a previously-registered daily job
    Log.Information("📅 Günlük otomatik pipeline kapalı (interaktif peak-saat akışı kullanılıyor). Manuel: POST /api/pipeline/run-now");
}

// ── Telegram otonom aday partileri (yoğun saatlerde) ─────────────
// "hour:windowHours" — her parti YouTube'u taze tarar ve o pencerede YAYINLANMIŞ videolardan aday seçer.
// Pencereler bir önceki partiden bu yana olan boşluğu kapatır (12:00→15s, 19:00→7s, 21:00→2s) → trend kaçmaz.
var peakHours = (builder.Configuration["Telegram:PeakHours"] ?? "12:15,19:7,21:2")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
TimeZoneInfo trZone;
try { trZone = TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time"); }
catch { trZone = TimeZoneInfo.Utc; }
foreach (var entry in peakHours)
{
    var parts = entry.Split(':', StringSplitOptions.TrimEntries);
    if (parts.Length != 2 || !int.TryParse(parts[0], out var hour) || hour is < 0 or > 23) continue;
    if (!int.TryParse(parts[1], out var window) || window <= 0) window = 3;
    RecurringJob.AddOrUpdate<VideoAutomation.Infrastructure.Publishing.TelegramConversationService>(
        $"telegram-batch-{hour:D2}",
        s => s.RunAutonomousBatchAsync(window, CancellationToken.None),
        $"0 {hour} * * *",
        new RecurringJobOptions { TimeZone = trZone });
}
Log.Information("📨 Telegram otonom partileri zamanlandı: {Hours} (saat:pencere, TR saati)", string.Join(" ", peakHours));

// ── Performans: yayınlanan videoların istatistiklerini günlük yenile ──
// Aktif kullanıcının YouTube'a yüklenmiş videolarının public izlenme/beğeni/yorum sayısını çekip
// PublicationStat zaman-serisine yazar (TR saatiyle her gün 08:00). Manuel: POST /api/performance/refresh.
RecurringJob.AddOrUpdate<VideoAutomation.Infrastructure.Publishing.PublicationStatsRefresher>(
    "refresh-publication-stats",
    r => r.RefreshAsync(CancellationToken.None),
    "0 8 * * *",
    new RecurringJobOptions { TimeZone = trZone });

// ── Ensure directories exist ─────────────────────────────────────
var outputDir = builder.Configuration["Pipeline:OutputDirectory"] ?? "output";
var assetsDir = builder.Configuration["Pipeline:AssetsDirectory"] ?? "assets";
Directory.CreateDirectory(outputDir);
Directory.CreateDirectory(assetsDir);
Directory.CreateDirectory(Path.Combine(assetsDir, "bgm"));
Directory.CreateDirectory(Path.Combine(assetsDir, "avatars"));
Directory.CreateDirectory(Path.Combine(assetsDir, "fonts"));
Directory.CreateDirectory(Path.Combine(assetsDir, "templates"));
Directory.CreateDirectory("logs");

Log.Information("🚀 Video Automation API started on {Urls}", string.Join(", ", app.Urls));

await app.RunAsync();
