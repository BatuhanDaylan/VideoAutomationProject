# Graph Report - VideoAutomationProject  (2026-08-06)

## Corpus Check
- 75 files · ~48,137 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 589 nodes · 1160 edges · 25 communities (24 shown, 1 thin omitted)
- Extraction: 97% EXTRACTED · 3% INFERRED · 0% AMBIGUOUS · INFERRED: 32 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- Video
- TrendsController
- FFmpegVideoComposer
- Trend
- .GetTrendsAsync
- GeminiClient
- VideoAutomation.Infrastructure.csproj
- Template
- SettingsController
- AppDbContext
- AiVideoClient
- VeoClient
- VideoAutomation.Domain.Entities
- LanguageHeuristics
- PexelsStockVideoProvider
- http
- AnalyzerAgent
- ErrorHandlingMiddleware
- GeminiScriptModels.cs
- AppExceptions.cs
- AesCryptoProvider
- INotificationService
- TelegramUpdatePollingService
- LoggerConfig.cs
- ICryptoService

## God Nodes (most connected - your core abstractions)
1. `Trend` - 35 edges
2. `VideoAutomation.Domain.Entities` - 34 edges
3. `VideoAutomation.Domain.Enums` - 30 edges
4. `Video` - 27 edges
5. `VideoAutomation.Application.Interfaces` - 26 edges
6. `AppDbContext` - 22 edges
7. `FFmpegVideoComposer` - 19 edges
8. `ScoutAgent` - 18 edges
9. `VideoAutomation.Shared.Exceptions` - 18 edges
10. `TrendsController` - 16 edges

## Surprising Connections (you probably didn't know these)
- `TemplatesController` --references--> `AppDbContext`  [EXTRACTED]
  src/VideoAutomation.API/Controllers/TemplatesController.cs → src/VideoAutomation.Infrastructure/Persistence/AppDbContext.cs
- `TrendsController` --references--> `IScoutAgent`  [EXTRACTED]
  src/VideoAutomation.API/Controllers/TrendsController.cs → src/VideoAutomation.Application/Interfaces/Agents/IScoutAgent.cs
- `TrendsController` --references--> `IContentGenerator`  [EXTRACTED]
  src/VideoAutomation.API/Controllers/TrendsController.cs → src/VideoAutomation.Application/Interfaces/IContentGenerator.cs
- `TrendsController` --references--> `AppDbContext`  [EXTRACTED]
  src/VideoAutomation.API/Controllers/TrendsController.cs → src/VideoAutomation.Infrastructure/Persistence/AppDbContext.cs
- `VideosController` --references--> `IPublisherAgent`  [EXTRACTED]
  src/VideoAutomation.API/Controllers/VideosController.cs → src/VideoAutomation.Application/Interfaces/Agents/IPublisherAgent.cs

## Import Cycles
- None detected.

## Communities (25 total, 1 thin omitted)

### Community 0 - "Video"
Cohesion: 0.06
Nodes (38): ChatId, CancellationToken, List, Task, IPublisherAgent, CancellationToken, Task, INotifier (+30 more)

### Community 1 - "TrendsController"
Cohesion: 0.13
Nodes (25): ActionResult, CancellationToken, HttpGet, HttpPost, IActionResult, IConfiguration, ILogger, List (+17 more)

### Community 2 - "FFmpegVideoComposer"
Cohesion: 0.08
Nodes (28): Args, bool, End, int, Process, ProcessStartInfo, SemaphoreSlim, Scene (+20 more)

### Community 3 - "Trend"
Cohesion: 0.09
Nodes (26): HashSet, CancellationToken, List, Task, IScoutAgent, CancellationToken, List, Task (+18 more)

### Community 4 - ".GetTrendsAsync"
Cohesion: 0.06
Nodes (36): CancellationToken, List, Task, IScraper, TrendCategory, CancellationToken, IConfiguration, IHttpClientFactory (+28 more)

### Community 5 - "GeminiClient"
Cohesion: 0.13
Nodes (14): CancellationToken, IConfiguration, IHttpClientFactory, ILogger, Task, GeminiClient, CancellationToken, IConfiguration (+6 more)

### Community 6 - "VideoAutomation.Infrastructure.csproj"
Cohesion: 0.09
Nodes (23): Google.Apis.YouTube.v3 (1.75.0.4207), Microsoft.AspNetCore.OpenApi (10.0.10), Microsoft.EntityFrameworkCore.Sqlite (10.0.10), Microsoft.Extensions.Configuration.Abstractions (10.0.10), Microsoft.Extensions.Configuration.Binder (10.0.10), Microsoft.Extensions.Http (10.0.10), Swashbuckle.AspNetCore (10.2.3), Xabe.FFmpeg (6.0.2) (+15 more)

### Community 7 - "Template"
Cohesion: 0.12
Nodes (17): CancellationToken, Task, videoPathA, videoPathB, IStockVideoProvider, CancellationToken, Task, IVideoComposer (+9 more)

### Community 8 - "SettingsController"
Cohesion: 0.14
Nodes (12): ControllerBase, HttpGet, IActionResult, IConfiguration, ILogger, SettingsController, ActionResult, HttpGet (+4 more)

### Community 9 - "AppDbContext"
Cohesion: 0.07
Nodes (25): DbContext, DbSet, HttpDelete, ModelBuilder, ActionResult, CancellationToken, HttpGet, HttpPost (+17 more)

### Community 10 - "AiVideoClient"
Cohesion: 0.21
Nodes (12): CancellationToken, HttpClient, IConfiguration, IHttpClientFactory, ILogger, Task, AiVideoClient, CancellationToken (+4 more)

### Community 11 - "VeoClient"
Cohesion: 0.21
Nodes (12): CancellationToken, HttpClient, IConfiguration, IHttpClientFactory, ILogger, Task, VeoClient, CancellationToken (+4 more)

### Community 12 - "VideoAutomation.Domain.Entities"
Cohesion: 0.10
Nodes (15): VideoAutomation.Infrastructure.Persistence, VideoAutomation.Domain.ValueObjects, VideoAutomation.Application.Interfaces, VideoAutomation.Application.Services, VideoAutomation.API.Controllers, VideoAutomation.Infrastructure.Media, VideoAutomation.Infrastructure.Publishing, VideoAutomation.Infrastructure.AI (+7 more)

### Community 13 - "LanguageHeuristics"
Cohesion: 0.33
Nodes (3): char, string, LanguageHeuristics

### Community 14 - "PexelsStockVideoProvider"
Cohesion: 0.27
Nodes (9): CancellationToken, IConfiguration, IHttpClientFactory, ILogger, List, Task, videoPathA, videoPathB (+1 more)

### Community 15 - "http"
Cohesion: 0.20
Nodes (9): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, profiles, http (+1 more)

### Community 16 - "AnalyzerAgent"
Cohesion: 0.07
Nodes (24): CancellationToken, IReadOnlyDictionary, IReadOnlyList, Task, AiFeasibilityResult, IContentGenerator, CancellationToken, IConfiguration (+16 more)

### Community 17 - "ErrorHandlingMiddleware"
Cohesion: 0.31
Nodes (6): VideoAutomation.API.Middleware, HttpContext, RequestDelegate, ILogger, Task, ErrorHandlingMiddleware

### Community 18 - "GeminiScriptModels.cs"
Cohesion: 0.67
Nodes (3): List, GeminiSceneDto, GeminiScriptResponse

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
Cohesion: 0.24
Nodes (10): BackgroundService, IServiceScopeFactory, long, CancellationToken, IConfiguration, IHttpClientFactory, ILogger, JsonElement (+2 more)

### Community 23 - "LoggerConfig.cs"
Cohesion: 0.40
Nodes (3): VideoAutomation.Shared.Logging, ILogger, LoggerConfig

## Knowledge Gaps
- **33 isolated node(s):** `$schema`, `commandName`, `dotnetRunMessages`, `launchBrowser`, `applicationUrl` (+28 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **1 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Trend` connect `Trend` to `Video`, `TrendsController`, `.GetTrendsAsync`, `GeminiClient`, `AppDbContext`, `VideoAutomation.Domain.Entities`, `AnalyzerAgent`?**
  _High betweenness centrality (0.206) - this node is a cross-community bridge._
- **Why does `Video` connect `Video` to `FFmpegVideoComposer`, `Trend`, `Template`, `AppDbContext`, `VideoAutomation.Domain.Entities`?**
  _High betweenness centrality (0.175) - this node is a cross-community bridge._
- **Why does `AppDbContext` connect `AppDbContext` to `Video`, `TrendsController`, `FFmpegVideoComposer`, `Trend`, `Template`, `SettingsController`, `VideoAutomation.Domain.Entities`, `AnalyzerAgent`?**
  _High betweenness centrality (0.106) - this node is a cross-community bridge._
- **What connects `$schema`, `commandName`, `dotnetRunMessages` to the rest of the system?**
  _33 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Video` be split into smaller, more focused modules?**
  _Cohesion score 0.05952380952380952 - nodes in this community are weakly interconnected._
- **Should `TrendsController` be split into smaller, more focused modules?**
  _Cohesion score 0.12605042016806722 - nodes in this community are weakly interconnected._
- **Should `FFmpegVideoComposer` be split into smaller, more focused modules?**
  _Cohesion score 0.07922705314009662 - nodes in this community are weakly interconnected._