# Graph Report - VideoAutomationProject  (2026-08-10)

## Corpus Check
- 123 files · ~71,788 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 1037 nodes · 2136 edges · 46 communities
- Extraction: 96% EXTRACTED · 4% INFERRED · 0% AMBIGUOUS · INFERRED: 80 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- YouTubePublisher
- App.tsx
- FFmpegVideoComposer
- ScoutAgent
- .GetTrendsAsync
- GeminiClient
- VideoAutomation.Infrastructure.csproj
- LanguageHeuristics
- TrendsController
- .Validate
- IUserSecrets
- VeoClient
- VideoAutomation.Domain.Entities
- AuthController
- package.json
- http
- GeminiKeyPool
- ErrorHandlingMiddleware
- ViralityScoreTests
- AppExceptions.cs
- AppDbContext
- INotificationService
- TelegramUpdatePollingService
- LoggerConfig.cs
- compilerOptions
- compilerOptions
- TelegramConversationService
- GeneratorAgent
- AiVideoClient
- PexelsStockVideoProvider
- Video Automation
- .GetTrendsAsync
- ChatController
- Video Automation — Dashboard (Faz 7)
- IContentGenerator
- TelegramNotifier
- .GetDualVideosAsync
- Trend
- IVideoComposer
- Template
- .GenerateVideoAsync
- YouTubeTranscriptService
- GeminiScriptModels.cs

## God Nodes (most connected - your core abstractions)
1. `Trend` - 48 edges
2. `VideoAutomation.Domain.Entities` - 44 edges
3. `VideoAutomation.Application.Interfaces` - 41 edges
4. `VideoAutomation.Domain.Enums` - 37 edges
5. `Video` - 32 edges
6. `TelegramConversationService` - 31 edges
7. `AppDbContext` - 30 edges
8. `Template` - 21 edges
9. `GeminiClient` - 21 edges
10. `AuthController` - 20 edges

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
Cohesion: 0.07
Nodes (34): ClientId, ClientSecret, GoogleAuthorizationCodeFlow, CancellationToken, HttpGet, HttpPost, IActionResult, Task (+26 more)

### Community 1 - "App.tsx"
Cohesion: 0.07
Nodes (54): api, ApiError, AuthUser, KeyField, KeyStatusMap, tokenStore, PipelineStatusDto, PublicationDto (+46 more)

### Community 2 - "FFmpegVideoComposer"
Cohesion: 0.08
Nodes (27): Args, End, Process, ProcessStartInfo, SemaphoreSlim, CancellationToken, IConfiguration, ILogger (+19 more)

### Community 3 - "ScoutAgent"
Cohesion: 0.14
Nodes (12): CancellationToken, HashSet, IConfiguration, IEnumerable, ILogger, IReadOnlyCollection, List, Regex (+4 more)

### Community 4 - ".GetTrendsAsync"
Cohesion: 0.14
Nodes (14): TrendCategory, CancellationToken, List, Task, DateTime, Dictionary, IConfiguration, IReadOnlyDictionary (+6 more)

### Community 5 - "GeminiClient"
Cohesion: 0.09
Nodes (22): Role, CancellationToken, IConfiguration, IHttpClientFactory, ILogger, IReadOnlyList, Task, TimeSpan (+14 more)

### Community 6 - "VideoAutomation.Infrastructure.csproj"
Cohesion: 0.06
Nodes (34): coverlet.collector (6.0.4), Google.Apis.YouTube.v3 (1.75.0.4207), Hangfire.AspNetCore (1.8.24), Hangfire.Core (1.8.24), Hangfire.Storage.SQLite (0.4.3), Microsoft.AspNetCore.Authentication.JwtBearer (10.0.10), Microsoft.AspNetCore.OpenApi (10.0.10), Microsoft.EntityFrameworkCore.Sqlite (10.0.10) (+26 more)

### Community 7 - "LanguageHeuristics"
Cohesion: 0.16
Nodes (7): char, string, LanguageHeuristics, Fact, InlineData, Theory, LanguageHeuristicsTests

### Community 8 - "TrendsController"
Cohesion: 0.07
Nodes (42): HttpDelete, IWebHostEnvironment, ManualAnalyzeRequest, SceneUpdateRequest, ActionResult, CancellationToken, HttpGet, HttpPost (+34 more)

### Community 9 - ".Validate"
Cohesion: 0.43
Nodes (4): IConfiguration, ILogger, List, StartupValidator

### Community 10 - "IUserSecrets"
Cohesion: 0.12
Nodes (15): CancellationToken, List, Task, IScraper, IReadOnlyList, IUserSecrets, IConfiguration, IHttpClientFactory (+7 more)

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
Cohesion: 0.10
Nodes (14): CancellationToken, List, Task, IViralityScorer, CancellationToken, IConfiguration, List, Task (+6 more)

### Community 19 - "AppExceptions.cs"
Cohesion: 0.48
Nodes (6): Exception, AnalysisException, ApiException, PublishingException, ScrapingException, VideoGenerationException

### Community 20 - "AppDbContext"
Cohesion: 0.07
Nodes (29): DbContext, DbSet, ModelBuilder, ActionResult, CancellationToken, HttpGet, HttpPost, IActionResult (+21 more)

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
Cohesion: 0.19
Nodes (12): ICurrentUserId, CancellationToken, Exception, HashSet, IBackgroundJobClient, IConfiguration, IHttpClientFactory, ILogger (+4 more)

### Community 27 - "GeneratorAgent"
Cohesion: 0.13
Nodes (13): CancellationToken, Task, INotifier, IStockVideoProvider, Scene, VisualMode, VisualModeExtensions, CancellationToken (+5 more)

### Community 28 - "AiVideoClient"
Cohesion: 0.21
Nodes (12): CancellationToken, HttpClient, IConfiguration, IHttpClientFactory, ILogger, Task, AiVideoClient, CancellationToken (+4 more)

### Community 29 - "PexelsStockVideoProvider"
Cohesion: 0.27
Nodes (9): CancellationToken, IConfiguration, IHttpClientFactory, ILogger, List, Task, videoPathA, videoPathB (+1 more)

### Community 30 - "Video Automation"
Cohesion: 0.20
Nodes (9): Gereksinimler, Kurulum, Mimari, Nasıl çalışır (özet), Notlar, Panel (frontend), Testler, Video Automation (+1 more)

### Community 31 - ".GetTrendsAsync"
Cohesion: 0.21
Nodes (9): CancellationToken, Dictionary, IConfiguration, IHttpClientFactory, ILogger, List, Task, TimeSpan (+1 more)

### Community 32 - "ChatController"
Cohesion: 0.06
Nodes (32): ChatMessageDto, ChatRequest, ChatResponse, ControllerBase, GenerateFromChatRequest, ActionResult, CancellationToken, HttpPost (+24 more)

### Community 33 - "Video Automation — Dashboard (Faz 7)"
Cohesion: 0.40
Nodes (4): Sayfalar, Video Automation — Dashboard (Faz 7), Çalıştırma, Üretim derlemesi

### Community 34 - "IContentGenerator"
Cohesion: 0.26
Nodes (9): CancellationToken, IReadOnlyDictionary, IReadOnlyList, Task, AiFeasibilityResult, ChatTurn, IContentGenerator, ManualAnalysisResult (+1 more)

### Community 35 - "TelegramNotifier"
Cohesion: 0.26
Nodes (8): ChatId, CancellationToken, IConfiguration, IHttpClientFactory, ILogger, Task, TelegramNotifier, Token

### Community 36 - ".GetDualVideosAsync"
Cohesion: 0.40
Nodes (4): CancellationToken, Task, videoPathA, videoPathB

### Community 40 - "Trend"
Cohesion: 0.16
Nodes (12): CancellationToken, Task, IAnalyzerAgent, DateTime, BaseEntity, DateTime, ICollection, Trend (+4 more)

### Community 41 - "IVideoComposer"
Cohesion: 0.60
Nodes (3): CancellationToken, Task, IVideoComposer

### Community 42 - "Template"
Cohesion: 0.28
Nodes (8): Script, Template, CancellationToken, IConfiguration, ILogger, JsonSerializerOptions, Task, AnalyzerAgent

### Community 43 - ".GenerateVideoAsync"
Cohesion: 0.50
Nodes (3): CancellationToken, Task, IGeneratorAgent

### Community 44 - "YouTubeTranscriptService"
Cohesion: 0.29
Nodes (5): CancellationToken, IConfiguration, ILogger, Task, YouTubeTranscriptService

### Community 45 - "GeminiScriptModels.cs"
Cohesion: 0.67
Nodes (3): List, GeminiSceneDto, GeminiScriptResponse

## Knowledge Gaps
- **124 isolated node(s):** `name`, `private`, `version`, `type`, `dev` (+119 more)
  These have ≤1 connection - possible missing edges or undocumented components.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trend` connect `Trend` to `YouTubePublisher`, `IContentGenerator`, `ScoutAgent`, `.GetTrendsAsync`, `GeminiClient`, `TrendsController`, `IUserSecrets`, `Template`, `VideoAutomation.Domain.Entities`, `ViralityScoreTests`, `AppDbContext`, `TelegramConversationService`, `.GetTrendsAsync`?**
  _High betweenness centrality (0.126) - this node is a cross-community bridge._
- **Why does `AppDbContext` connect `AppDbContext` to `ChatController`, `YouTubePublisher`, `ScoutAgent`, `TrendsController`, `Trend`, `Template`, `VideoAutomation.Domain.Entities`, `AuthController`, `TelegramConversationService`, `GeneratorAgent`?**
  _High betweenness centrality (0.107) - this node is a cross-community bridge._
- **Why does `IUserSecrets` connect `IUserSecrets` to `ChatController`, `YouTubePublisher`, `TelegramNotifier`, `GeminiClient`, `VeoClient`, `AuthController`, `TelegramUpdatePollingService`, `TelegramConversationService`, `AiVideoClient`, `PexelsStockVideoProvider`, `.GetTrendsAsync`?**
  _High betweenness centrality (0.090) - this node is a cross-community bridge._
- **What connects `name`, `private`, `version` to the rest of the system?**
  _124 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `YouTubePublisher` be split into smaller, more focused modules?**
  _Cohesion score 0.06568832983927324 - nodes in this community are weakly interconnected._
- **Should `App.tsx` be split into smaller, more focused modules?**
  _Cohesion score 0.06666666666666667 - nodes in this community are weakly interconnected._
- **Should `FFmpegVideoComposer` be split into smaller, more focused modules?**
  _Cohesion score 0.08139534883720931 - nodes in this community are weakly interconnected._