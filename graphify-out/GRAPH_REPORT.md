# Graph Report - VideoAutomationProject  (2026-08-11)

## Corpus Check
- 135 files · ~76,277 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1122 nodes · 2322 edges · 46 communities
- Extraction: 96% EXTRACTED · 4% INFERRED · 0% AMBIGUOUS · INFERRED: 89 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- YouTubePublisher
- App.tsx
- FFmpegVideoComposer
- Trend
- .GetTrendsAsync
- GeminiClient
- VideoAutomation.Infrastructure.csproj
- LanguageHeuristics
- TrendsController
- .Validate
- PerformanceController
- VeoClient
- VideoAutomation.Domain.Entities
- AuthController
- package.json
- http
- GeminiKeyPool
- ErrorHandlingMiddleware
- ViralityScoreTests
- AppExceptions.cs
- .GetTrendsAsync
- INotificationService
- TelegramUpdatePollingService
- LoggerConfig.cs
- compilerOptions
- compilerOptions
- TelegramConversationService
- GeneratorAgent
- .SaveChangesAsync
- IUserSecrets
- Video Automation
- ControllerBase
- Template
- Video Automation — Dashboard (Faz 7)
- PipelineController
- ScheduleController
- AppDbContext
- Video
- UsageController
- .GetStatsAsync
- ScheduleRunnerService
- SettingsController
- GeminiScriptModels.cs

## God Nodes (most connected - your core abstractions)
1. `VideoAutomation.Domain.Entities` - 49 edges
2. `Trend` - 48 edges
3. `VideoAutomation.Application.Interfaces` - 45 edges
4. `VideoAutomation.Domain.Enums` - 39 edges
5. `AppDbContext` - 39 edges
6. `Video` - 32 edges
7. `TelegramConversationService` - 31 edges
8. `GeminiClient` - 22 edges
9. `Template` - 21 edges
10. `VideoAutomation.Infrastructure.Persistence` - 21 edges

## Surprising Connections (you probably didn't know these)
- `Shell()` --calls--> `useAuth()`  [EXTRACTED]
  frontend/src/App.tsx → frontend/src/auth/AuthContext.tsx
- `RunCard()` --calls--> `formatDate()`  [EXTRACTED]
  frontend/src/pages/DashboardPage.tsx → frontend/src/components/common.tsx
- `EditableScene()` --calls--> `useToast()`  [EXTRACTED]
  frontend/src/pages/VideosPage.tsx → frontend/src/components/common.tsx
- `AuthController` --references--> `IActiveUserProvider`  [EXTRACTED]
  src/VideoAutomation.API/Controllers/AuthController.cs → src/VideoAutomation.Application/Interfaces/IActiveUserProvider.cs
- `AuthController` --references--> `AppDbContext`  [EXTRACTED]
  src/VideoAutomation.API/Controllers/AuthController.cs → src/VideoAutomation.Infrastructure/Persistence/AppDbContext.cs

## Import Cycles
- None detected.

## Communities (46 total, 0 thin omitted)

### Community 0 - "YouTubePublisher"
Cohesion: 0.13
Nodes (18): ClientId, ClientSecret, GoogleAuthorizationCodeFlow, CancellationToken, HttpGet, HttpPost, IActionResult, Task (+10 more)

### Community 1 - "App.tsx"
Cohesion: 0.06
Nodes (62): api, ApiError, AuthUser, KeyField, KeyStatusMap, PerformanceResponse, PerformanceVideo, ScheduledRunDto (+54 more)

### Community 2 - "FFmpegVideoComposer"
Cohesion: 0.08
Nodes (28): Args, End, Process, ProcessStartInfo, SemaphoreSlim, Scene, CancellationToken, IConfiguration (+20 more)

### Community 3 - "Trend"
Cohesion: 0.09
Nodes (25): CancellationToken, List, Task, IViralityScorer, CancellationToken, IConfiguration, List, Task (+17 more)

### Community 4 - ".GetTrendsAsync"
Cohesion: 0.14
Nodes (14): TrendCategory, CancellationToken, List, Task, DateTime, Dictionary, IConfiguration, IReadOnlyDictionary (+6 more)

### Community 5 - "GeminiClient"
Cohesion: 0.07
Nodes (27): Role, Script, CancellationToken, IConfiguration, IHttpClientFactory, ILogger, IReadOnlyList, Task (+19 more)

### Community 6 - "VideoAutomation.Infrastructure.csproj"
Cohesion: 0.06
Nodes (34): coverlet.collector (6.0.4), Google.Apis.YouTube.v3 (1.75.0.4207), Hangfire.AspNetCore (1.8.24), Hangfire.Core (1.8.24), Hangfire.Storage.SQLite (0.4.3), Microsoft.AspNetCore.Authentication.JwtBearer (10.0.10), Microsoft.AspNetCore.OpenApi (10.0.10), Microsoft.EntityFrameworkCore.Sqlite (10.0.10) (+26 more)

### Community 7 - "LanguageHeuristics"
Cohesion: 0.16
Nodes (7): char, string, LanguageHeuristics, Fact, InlineData, Theory, LanguageHeuristicsTests

### Community 8 - "TrendsController"
Cohesion: 0.07
Nodes (45): IWebHostEnvironment, ManualAnalyzeRequest, SceneUpdateRequest, ActionResult, CancellationToken, HttpGet, HttpPost, IActionResult (+37 more)

### Community 9 - ".Validate"
Cohesion: 0.43
Nodes (4): IConfiguration, ILogger, List, StartupValidator

### Community 10 - "PerformanceController"
Cohesion: 0.21
Nodes (10): CancellationToken, HttpGet, HttpPost, IActionResult, Task, PerformanceController, CancellationToken, ILogger (+2 more)

### Community 11 - "VeoClient"
Cohesion: 0.21
Nodes (12): CancellationToken, HttpClient, IConfiguration, IHttpClientFactory, ILogger, Task, VeoClient, CancellationToken (+4 more)

### Community 12 - "VideoAutomation.Domain.Entities"
Cohesion: 0.05
Nodes (29): ClaimsPrincipal, VideoAutomation.Infrastructure.Persistence, VideoAutomation.API, VideoAutomation.API.Middleware, VideoAutomation.Domain.ValueObjects, VideoAutomation.Application.Interfaces, VideoAutomation.Application.Services, VideoAutomation.API.Controllers (+21 more)

### Community 13 - "AuthController"
Cohesion: 0.07
Nodes (30): AllowAnonymous, Authorize, byte, Hash, LoginRequest, RegisterRequest, Salt, SaveKeysRequest (+22 more)

### Community 14 - "package.json"
Cohesion: 0.07
Nodes (26): dependencies, react, react-dom, devDependencies, @types/node, @types/react, @types/react-dom, typescript (+18 more)

### Community 15 - "http"
Cohesion: 0.20
Nodes (9): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, profiles, http (+1 more)

### Community 16 - "GeminiKeyPool"
Cohesion: 0.14
Nodes (9): ConcurrentDictionary, DateTime, IReadOnlyList, List, TimeSpan, GeminiKeyPool, ConcurrentDictionary, IReadOnlyList (+1 more)

### Community 17 - "ErrorHandlingMiddleware"
Cohesion: 0.43
Nodes (5): HttpContext, RequestDelegate, ILogger, Task, ErrorHandlingMiddleware

### Community 18 - "ViralityScoreTests"
Cohesion: 0.26
Nodes (4): Fact, InlineData, Theory, ViralityScoreTests

### Community 19 - "AppExceptions.cs"
Cohesion: 0.48
Nodes (6): Exception, AnalysisException, ApiException, PublishingException, ScrapingException, VideoGenerationException

### Community 20 - ".GetTrendsAsync"
Cohesion: 0.21
Nodes (9): CancellationToken, Dictionary, IConfiguration, IHttpClientFactory, ILogger, List, Task, TimeSpan (+1 more)

### Community 21 - "INotificationService"
Cohesion: 0.47
Nodes (3): CancellationToken, Task, INotificationService

### Community 22 - "TelegramUpdatePollingService"
Cohesion: 0.19
Nodes (12): BackgroundService, IServiceScope, IServiceScopeFactory, long, CancellationToken, Func, IConfiguration, IHttpClientFactory (+4 more)

### Community 23 - "LoggerConfig.cs"
Cohesion: 0.40
Nodes (3): VideoAutomation.Shared.Logging, ILogger, LoggerConfig

### Community 24 - "compilerOptions"
Cohesion: 0.09
Nodes (22): compilerOptions, allowImportingTsExtensions, isolatedModules, jsx, lib, module, moduleDetection, moduleResolution (+14 more)

### Community 25 - "compilerOptions"
Cohesion: 0.12
Nodes (16): compilerOptions, allowSyntheticDefaultImports, composite, isolatedModules, lib, module, moduleDetection, moduleResolution (+8 more)

### Community 26 - "TelegramConversationService"
Cohesion: 0.15
Nodes (16): CancellationToken, List, Task, IPublisherAgent, ICurrentUserId, CancellationToken, Exception, HashSet (+8 more)

### Community 27 - "GeneratorAgent"
Cohesion: 0.05
Nodes (40): CancellationToken, Task, INotifier, CancellationToken, Task, videoPathA, videoPathB, IStockVideoProvider (+32 more)

### Community 28 - ".SaveChangesAsync"
Cohesion: 0.20
Nodes (11): DateTime, PipelineRun, CancellationToken, Exception, Func, IConfiguration, ILogger, Task (+3 more)

### Community 29 - "IUserSecrets"
Cohesion: 0.12
Nodes (15): CancellationToken, List, Task, IScraper, IReadOnlyList, IUserSecrets, IConfiguration, IHttpClientFactory (+7 more)

### Community 30 - "Video Automation"
Cohesion: 0.20
Nodes (9): Gereksinimler, Kurulum, Mimari, Nasıl çalışır (özet), Notlar, Panel (frontend), Testler, Video Automation (+1 more)

### Community 31 - "ControllerBase"
Cohesion: 0.14
Nodes (12): ControllerBase, HttpPost, IActionResult, IBackgroundJobClient, IConfiguration, TelegramController, ActionResult, HttpGet (+4 more)

### Community 32 - "Template"
Cohesion: 0.06
Nodes (39): ChatMessageDto, ChatRequest, ChatResponse, GenerateFromChatRequest, ActionResult, CancellationToken, HttpPost, ILogger (+31 more)

### Community 33 - "Video Automation — Dashboard (Faz 7)"
Cohesion: 0.40
Nodes (4): Sayfalar, Video Automation — Dashboard (Faz 7), Çalıştırma, Üretim derlemesi

### Community 34 - "PipelineController"
Cohesion: 0.20
Nodes (10): ActionResult, CancellationToken, HttpGet, HttpPost, IActionResult, IBackgroundJobClient, ILogger, List (+2 more)

### Community 35 - "ScheduleController"
Cohesion: 0.19
Nodes (12): CreateRequest, CancellationToken, DateTime, HttpDelete, HttpGet, HttpPost, IActionResult, IBackgroundJobClient (+4 more)

### Community 36 - "AppDbContext"
Cohesion: 0.20
Nodes (6): DbContext, DbSet, ModelBuilder, DateTime, ScheduledRun, AppDbContext

### Community 40 - "Video"
Cohesion: 0.07
Nodes (30): ChatId, CancellationToken, Task, IPublisher, DateTime, ApiUsage, DateTime, BaseEntity (+22 more)

### Community 41 - "UsageController"
Cohesion: 0.25
Nodes (8): CancellationToken, HttpGet, IActionResult, IHttpClientFactory, ILogger, JsonElement, Task, UsageController

### Community 42 - ".GetStatsAsync"
Cohesion: 0.20
Nodes (9): CancellationToken, Dictionary, IHttpClientFactory, ILogger, IReadOnlyCollection, Task, VideoStats, YouTubeStatsService (+1 more)

### Community 43 - "ScheduleRunnerService"
Cohesion: 0.25
Nodes (7): CancellationToken, Task, IPipelineOrchestrator, CancellationToken, ILogger, Task, ScheduleRunnerService

### Community 44 - "SettingsController"
Cohesion: 0.38
Nodes (5): HttpGet, IActionResult, IConfiguration, ILogger, SettingsController

### Community 45 - "GeminiScriptModels.cs"
Cohesion: 0.67
Nodes (3): List, GeminiSceneDto, GeminiScriptResponse

## Knowledge Gaps
- **127 isolated node(s):** `name`, `private`, `version`, `type`, `dev` (+122 more)
  These have ≤1 connection - possible missing edges or undocumented components.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `AppDbContext` connect `AppDbContext` to `Template`, `PipelineController`, `ScheduleController`, `Trend`, `GeminiClient`, `FFmpegVideoComposer`, `TrendsController`, `UsageController`, `PerformanceController`, `Video`, `ScheduleRunnerService`, `AuthController`, `VideoAutomation.Domain.Entities`, `TelegramConversationService`, `GeneratorAgent`, `.SaveChangesAsync`, `ControllerBase`?**
  _High betweenness centrality (0.154) - this node is a cross-community bridge._
- **Why does `Trend` connect `Trend` to `Template`, `.GetTrendsAsync`, `GeminiClient`, `AppDbContext`, `TrendsController`, `Video`, `VideoAutomation.Domain.Entities`, `.GetTrendsAsync`, `TelegramConversationService`, `IUserSecrets`?**
  _High betweenness centrality (0.108) - this node is a cross-community bridge._
- **Why does `IUserSecrets` connect `IUserSecrets` to `YouTubePublisher`, `GeminiClient`, `Video`, `UsageController`, `.GetStatsAsync`, `VeoClient`, `SettingsController`, `AuthController`, `.GetTrendsAsync`, `TelegramUpdatePollingService`, `TelegramConversationService`, `GeneratorAgent`?**
  _High betweenness centrality (0.075) - this node is a cross-community bridge._
- **What connects `name`, `private`, `version` to the rest of the system?**
  _127 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `YouTubePublisher` be split into smaller, more focused modules?**
  _Cohesion score 0.12643678160919541 - nodes in this community are weakly interconnected._
- **Should `App.tsx` be split into smaller, more focused modules?**
  _Cohesion score 0.06487341772151899 - nodes in this community are weakly interconnected._
- **Should `FFmpegVideoComposer` be split into smaller, more focused modules?**
  _Cohesion score 0.07922705314009662 - nodes in this community are weakly interconnected._