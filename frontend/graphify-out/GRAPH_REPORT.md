# Graph Report - frontend  (2026-08-10)

## Corpus Check
- 17 files · ~5,858 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 130 nodes · 189 edges · 11 communities
- Extraction: 100% EXTRACTED · 0% INFERRED · 0% AMBIGUOUS
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- compilerOptions
- client.ts
- VideosPage.tsx
- common.tsx
- compilerOptions
- package.json
- devDependencies
- Video Automation — Dashboard (Faz 7)

## God Nodes (most connected - your core abstractions)
1. `compilerOptions` - 16 edges
2. `compilerOptions` - 12 edges
3. `useToast()` - 10 edges
4. `usePolling()` - 7 edges
5. `api` - 5 edges
6. `formatDate()` - 5 edges
7. `Spinner()` - 5 edges
8. `TrendsPage()` - 5 edges
9. `scripts` - 4 edges
10. `VideoDto` - 4 edges

## Surprising Connections (you probably didn't know these)
- `RunCard()` --calls--> `formatDate()`  [EXTRACTED]
  src/pages/DashboardPage.tsx → src/components/common.tsx
- `ChatPage()` --calls--> `useToast()`  [EXTRACTED]
  src/pages/ChatPage.tsx → src/components/common.tsx
- `TrendsPage()` --calls--> `useToast()`  [EXTRACTED]
  src/pages/TrendsPage.tsx → src/components/common.tsx
- `EditableScene()` --calls--> `useToast()`  [EXTRACTED]
  src/pages/VideosPage.tsx → src/components/common.tsx
- `TrendsPage()` --calls--> `usePolling()`  [EXTRACTED]
  src/pages/TrendsPage.tsx → src/hooks/usePolling.ts

## Import Cycles
- None detected.

## Communities (11 total, 0 thin omitted)

### Community 0 - "compilerOptions"
Cohesion: 0.09
Nodes (22): DOM, DOM.Iterable, ES2020, src, compilerOptions, allowImportingTsExtensions, isolatedModules, jsx (+14 more)

### Community 1 - "client.ts"
Cohesion: 0.16
Nodes (14): api, ApiError, PipelineStatusDto, PublicationDto, ScriptDto, SettingsStatus, TemplateDto, TrendDto (+6 more)

### Community 2 - "VideosPage.tsx"
Cohesion: 0.20
Nodes (15): SceneDto, formatDate(), useToast(), PollingResult, usePolling(), DashboardPage(), RunCard(), SERVICE_LABELS (+7 more)

### Community 3 - "common.tsx"
Cohesion: 0.17
Nodes (13): App(), Tab, TABS, formatNumber(), PlatformBadge(), ScoreChip(), Spinner(), STATUS_TR (+5 more)

### Community 4 - "compilerOptions"
Cohesion: 0.12
Nodes (16): ES2023, node, vite.config.ts, compilerOptions, allowSyntheticDefaultImports, composite, isolatedModules, lib (+8 more)

### Community 5 - "package.json"
Cohesion: 0.14
Nodes (13): dependencies, react, react-dom, name, private, scripts, build, dev (+5 more)

### Community 6 - "devDependencies"
Cohesion: 0.15
Nodes (13): devDependencies, @types/node, @types/react, @types/react-dom, typescript, vite, @vitejs/plugin-react, @types/node (+5 more)

### Community 7 - "Video Automation — Dashboard (Faz 7)"
Cohesion: 0.40
Nodes (4): Sayfalar, Video Automation — Dashboard (Faz 7), Çalıştırma, Üretim derlemesi

## Knowledge Gaps
- **62 isolated node(s):** `name`, `private`, `version`, `type`, `dev` (+57 more)
  These have ≤1 connection - possible missing edges or undocumented components.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `devDependencies` connect `devDependencies` to `package.json`?**
  _High betweenness centrality (0.028) - this node is a cross-community bridge._
- **What connects `name`, `private`, `version` to the rest of the system?**
  _62 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `compilerOptions` be split into smaller, more focused modules?**
  _Cohesion score 0.08695652173913043 - nodes in this community are weakly interconnected._
- **Should `compilerOptions` be split into smaller, more focused modules?**
  _Cohesion score 0.11764705882352941 - nodes in this community are weakly interconnected._
- **Should `package.json` be split into smaller, more focused modules?**
  _Cohesion score 0.14285714285714285 - nodes in this community are weakly interconnected._