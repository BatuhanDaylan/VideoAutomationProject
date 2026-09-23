// Mirrors VideoAutomation.Application.DTOs (AllDtos.cs). Kept hand-written and minimal on purpose —
// the API surface is small and stable, and a codegen step would be more machinery than it earns here.

export interface TrendDto {
  id: number;
  title: string;
  description: string;
  sourceUrl: string;
  sourcePlatform: string;
  category: string;
  viewCount: number;
  likeCount: number;
  commentCount: number;
  finalViralityScore: number;
  viralityLabel: string;
  tags: string[];
  isSelected: boolean;
  createdAt: string;
  publishedAt?: string | null;
  languageCode?: string | null;
  aiFeasibilityScore?: number | null;
  aiFeasibilityReason?: string | null;
  contentSummary?: string | null;
}

export interface SceneDto {
  id: number;
  orderIndex: number;
  narrationText: string;
  sceneType: string;
  visualKeywordA: string;
  visualKeywordB: string;
  videoPrompt: string;
  durationSeconds: number;
  isAvatarScene: boolean;
  transitionEffect: string;
}

export interface ScriptDto {
  id: number;
  topic: string;
  fullText: string;
  language: string;
  scenes: SceneDto[];
}

export interface PublicationDto {
  id: number;
  platform: string;
  isPublished: boolean;
  publishedAt?: string | null;
  platformUrl?: string | null;
  errorMessage?: string | null;
}

export interface VideoDto {
  id: number;
  title: string;
  description: string;
  status: string;
  visualMode: string;
  templateType: string;
  durationSeconds: number;
  outputFilePath?: string | null;
  thumbnailPath?: string | null;
  trendId?: number | null;
  script?: ScriptDto | null;
  scenes: SceneDto[];
  publications: PublicationDto[];
  createdAt: string;
}

export interface TemplateDto {
  id: number;
  name: string;
  description: string;
  type: string;
  ttsVoice: string;
  ttsRate: number;
  transitionStyle: string;
  useABSplit: boolean;
  useAvatarInjection: boolean;
  isDefault: boolean;
}

export interface PipelineStatusDto {
  id: number;
  status: string;
  currentStep: string;
  totalSteps: number;
  completedSteps: number;
  progressPercent: number;
  startedAt?: string | null;
  completedAt?: string | null;
  errorMessage?: string | null;
  logOutput: string;
  videoId?: number | null;
}

export interface SettingsStatus {
  geminiConfigured: boolean;
  geminiKeyCount: number;
  geminiKeysAvailable: number;
  pexelsConfigured: boolean;
  apifyConfigured: boolean;
  aiVideoConfigured: boolean;
  telegramConfigured: boolean;
  youtubeConfigured: boolean;
  region: string;
  language: string;
}
