# Graph Report - VideoAutomationProject  (2026-08-07)

## Corpus Check
- 107 files · ~64,560 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 921 nodes · 1867 edges · 33 communities
- Extraction: 97% EXTRACTED · 3% INFERRED · 0% AMBIGUOUS · INFERRED: 65 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- Video
- VideosPage.tsx
- FFmpegVideoComposer
- Trend
- .GetTrendsAsync
- GeminiClient
- VideoAutomation.Infrastructure.csproj
- ViralityScoreTests
- TrendsController
- GeminiKeyPool
- AiVideoClient
- VeoClient
- VideoAutomation.Domain.Entities
- package.json
- http
- Template
- ErrorHandlingMiddleware
- AppExceptions.cs
- AesCryptoProvider
- INotificationService
- TelegramUpdatePollingService
- LoggerConfig.cs
- compilerOptions
- compilerOptions
- AppDbContext
- PipelineController
- GeneratorAgent
- ChatController
- Video Automation
- Video Automation — Dashboard (Faz 7)

## God Nodes (most connected - your core abstractions)
1. `Trend` - 46 edges
2. `VideoAutomation.Domain.Entities` - 40 edges
3. `VideoAutomation.Domain.Enums` - 37 edges
4. `Video` - 32 edges
5. `VideoAutomation.Application.Interfaces` - 26 edges
6. `AppDbContext` - 26 edges
7. `TelegramConversationService` - 23 edges
8. `Template` - 21 edges
9. `ScoutAgent` - 20 edges
10. `GeminiClient` - 19 edges

## Surprising Connections (you probably didn't know these)
- `RunCard()` --calls--> `formatDate()`  [EXTRACTED]
  frontend/src/pages/DashboardPage.tsx → frontend/src/components/common.tsx
- `EditableScene()` --calls--> `useToast()`  [EXTRACTED]
  frontend/src/pages/VideosPage.tsx → frontend/src/components/common.tsx
- `ChatController` --references--> `IContentGenerator`  [EXTRACTED]
  src/VideoAutomation.API/Controllers/ChatController.cs → src/VideoAutomation.Application/Interfaces/IContentGenerator.cs
- `PipelineController` --references--> `AppDbContext`  [EXTRACTED]
  src/VideoAutomation.API/Controllers/PipelineController.cs → src/VideoAutomation.Infrastructure/Persistence/AppDbContext.cs
- `TemplatesController` --references--> `AppDbContext`  [EXTRACTED]
  src/VideoAutomation.API/Controllers/TemplatesController.cs → src/VideoAutomation.Infrastructure/Persistence/AppDbContext.cs

## Import Cycles
- None detected.

## Communities (33 total, 0 thin omitted)

### Community 0 - "Video"
Cohesion: 0.06
Nodes (37): ChatId, CancellationToken, List, Task, IPublisherAgent, CancellationToken, Task, IPublisher (+29 more)

### Community 1 - "VideosPage.tsx"
Cohesion: 0.08
Nodes (42): api, ApiError, PipelineStatusDto, PublicationDto, SceneDto, ScriptDto, SettingsStatus, TemplateDto (+34 more)

### Community 2 - "FFmpegVideoComposer"
Cohesion: 0.08
Nodes (27): Args, bool, End, int, Process, ProcessStartInfo, SemaphoreSlim, CancellationToken (+19 more)

### Community 3 - "Trend"
Cohesion: 0.09
Nodes (25): HashSet, CancellationToken, List, Task, IViralityScorer, CancellationToken, IConfiguration, List (+17 more)

### Community 4 - ".GetTrendsAsync"
Cohesion: 0.06
Nodes (36): CancellationToken, List, Task, IScraper, TrendCategory, CancellationToken, IConfiguration, IHttpClientFactory (+28 more)

### Community 5 - "GeminiClient"
Cohesion: 0.09
Nodes (23): Role, Script, CancellationToken, IConfiguration, IHttpClientFactory, ILogger, IReadOnlyList, Task (+15 more)

### Community 6 - "VideoAutomation.Infrastructure.csproj"
Cohesion: 0.07
Nodes (33): coverlet.collector (6.0.4), Google.Apis.YouTube.v3 (1.75.0.4207), Hangfire.AspNetCore (1.8.24), Hangfire.Core (1.8.24), Hangfire.Storage.SQLite (0.4.3), Microsoft.AspNetCore.OpenApi (10.0.10), Microsoft.EntityFrameworkCore.Sqlite (10.0.10), Microsoft.Extensions.Configuration.Abstractions (10.0.10) (+25 more)

### Community 7 - "ViralityScoreTests"
Cohesion: 0.09
Nodes (12): char, VideoAutomation.Tests, string, LanguageHeuristics, Fact, InlineData, Theory, LanguageHeuristicsTests (+4 more)

### Community 8 - "TrendsController"
Cohesion: 0.06
Nodes (53): ControllerBase, HttpDelete, HttpPut, IWebHostEnvironment, ManualAnalyzeRequest, SceneUpdateRequest, HttpPost, IActionResult (+45 more)

### Community 9 - "GeminiKeyPool"
Cohesion: 0.08
Nodes (20): ConcurrentDictionary, VideoAutomation.API, VideoAutomation.Infrastructure.AI, HttpGet, IActionResult, IConfiguration, ILogger, SettingsController (+12 more)

### Community 10 - "AiVideoClient"
Cohesion: 0.21
Nodes (12): CancellationToken, HttpClient, IConfiguration, IHttpClientFactory, ILogger, Task, AiVideoClient, CancellationToken (+4 more)

### Community 11 - "VeoClient"
Cohesion: 0.21
Nodes (12): CancellationToken, HttpClient, IConfiguration, IHttpClientFactory, ILogger, Task, VeoClient, CancellationToken (+4 more)

### Community 12 - "VideoAutomation.Domain.Entities"
Cohesion: 0.08
Nodes (18): VideoAutomation.Infrastructure.Persistence, VideoAutomation.API.Middleware, VideoAutomation.Domain.ValueObjects, VideoAutomation.Application.Interfaces, VideoAutomation.Application.Services, VideoAutomation.API.Controllers, VideoAutomation.Infrastructure.Media, VideoAutomation.Infrastructure.Publishing (+10 more)

### Community 14 - "package.json"
Cohesion: 0.07
Nodes (26): dependencies, react, react-dom, devDependencies, @types/node, @types/react, @types/react-dom, typescript (+18 more)

### Community 15 - "http"
Cohesion: 0.20
Nodes (9): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, profiles, http (+1 more)

### Community 16 - "Template"
Cohesion: 0.10
Nodes (22): CancellationToken, IReadOnlyDictionary, IReadOnlyList, Task, AiFeasibilityResult, ChatTurn, IContentGenerator, ManualAnalysisResult (+14 more)

### Community 17 - "ErrorHandlingMiddleware"
Cohesion: 0.43
Nodes (5): HttpContext, RequestDelegate, ILogger, Task, ErrorHandlingMiddleware

### Community 19 - "AppExceptions.cs"
Cohesion: 0.48
Nodes (6): Exception, AnalysisException, ApiException, PublishingException, ScrapingException, VideoGenerationException

### Community 20 - "AesCryptoProvider"
Cohesion: 0.33
Nodes (3): byte, VideoAutomation.Infrastructure.Security, AesCryptoProvider

### Community 21 - "INotificationService"
Cohesion: 0.47
Nodes (3): CancellationToken, Task, INotificationService

### Community 22 - "TelegramUpdatePollingService"
Cohesion: 0.22
Nodes (11): BackgroundService, IServiceScope, IServiceScopeFactory, long, CancellationToken, IConfiguration, IHttpClientFactory, ILogger (+3 more)

### Community 23 - "LoggerConfig.cs"
Cohesion: 0.40
Nodes (3): VideoAutomation.Shared.Logging, ILogger, LoggerConfig

### Community 24 - "compilerOptions"
Cohesion: 0.09
Nodes (22): compilerOptions, allowImportingTsExtensions, isolatedModules, jsx, lib, module, moduleDetection, moduleResolution (+14 more)

### Community 25 - "compilerOptions"
Cohesion: 0.12
Nodes (16): compilerOptions, allowSyntheticDefaultImports, composite, isolatedModules, lib, module, moduleDetection, moduleResolution (+8 more)

### Community 26 - "AppDbContext"
Cohesion: 0.08
Nodes (29): DbContext, DbSet, Func, ModelBuilder, CancellationToken, Task, IGeneratorAgent, CancellationToken (+21 more)

### Community 27 - "PipelineController"
Cohesion: 0.20
Nodes (10): ActionResult, CancellationToken, HttpGet, HttpPost, IActionResult, IBackgroundJobClient, ILogger, List (+2 more)

### Community 28 - "GeneratorAgent"
Cohesion: 0.07
Nodes (29): CancellationToken, Task, INotifier, CancellationToken, Task, videoPathA, videoPathB, IStockVideoProvider (+21 more)

### Community 29 - "ChatController"
Cohesion: 0.12
Nodes (18): ChatMessageDto, ChatRequest, ChatResponse, GenerateFromChatRequest, ActionResult, CancellationToken, HttpPost, ILogger (+10 more)

### Community 30 - "Video Automation"
Cohesion: 0.20
Nodes (9): Gereksinimler, Kurulum, Mimari, Nasıl çalışır (özet), Notlar, Panel (frontend), Testler, Video Automation (+1 more)

### Community 33 - "Video Automation — Dashboard (Faz 7)"
Cohesion: 0.40
Nodes (4): Sayfalar, Video Automation — Dashboard (Faz 7), Çalıştırma, Üretim derlemesi

## Knowledge Gaps
- **119 isolated node(s):** `name`, `private`, `version`, `type`, `dev` (+114 more)
  These have ≤1 connection - possible missing edges or undocumented components.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trend` connect `Trend` to `Video`, `.GetTrendsAsync`, `GeminiClient`, `TrendsController`, `VideoAutomation.Domain.Entities`, `Template`, `AppDbContext`, `ChatController`?**
  _High betweenness centrality (0.145) - this node is a cross-community bridge._
- **Why does `Video` connect `Video` to `FFmpegVideoComposer`, `Trend`, `GeminiClient`, `TrendsController`, `VideoAutomation.Domain.Entities`, `Template`, `AppDbContext`, `GeneratorAgent`, `ChatController`?**
  _High betweenness centrality (0.102) - this node is a cross-community bridge._
- **Why does `AppDbContext` connect `AppDbContext` to `Video`, `Trend`, `GeminiClient`, `TrendsController`, `VideoAutomation.Domain.Entities`, `Template`, `PipelineController`, `GeneratorAgent`?**
  _High betweenness centrality (0.080) - this node is a cross-community bridge._
- **What connects `name`, `private`, `version` to the rest of the system?**
  _119 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Video` be split into smaller, more focused modules?**
  _Cohesion score 0.06103896103896104 - nodes in this community are weakly interconnected._
- **Should `VideosPage.tsx` be split into smaller, more focused modules?**
  _Cohesion score 0.08148148148148149 - nodes in this community are weakly interconnected._
- **Should `FFmpegVideoComposer` be split into smaller, more focused modules?**
  _Cohesion score 0.08139534883720931 - nodes in this community are weakly interconnected._