import type {
  TrendDto,
  VideoDto,
  TemplateDto,
  PipelineStatusDto,
  SettingsStatus,
} from "./types";

// All requests go to same-origin /api/* — the Vite dev server proxies that to the ASP.NET API
// (see vite.config.ts), so there's no CORS and no port baked into the client.
const BASE = "/api";
const TOKEN_KEY = "va_token";

export const tokenStore = {
  get: () => localStorage.getItem(TOKEN_KEY),
  set: (t: string) => localStorage.setItem(TOKEN_KEY, t),
  clear: () => localStorage.removeItem(TOKEN_KEY),
};

class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
    this.name = "ApiError";
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const token = tokenStore.get();
  let res: Response;
  try {
    res = await fetch(`${BASE}${path}`, {
      ...init,
      headers: {
        "Content-Type": "application/json",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(init?.headers ?? {}),
      },
    });
  } catch (e) {
    // fetch only rejects on network/transport failure — surface it as something the UI can show
    // instead of an unhandled promise rejection. Most commonly: the API isn't running.
    throw new ApiError(0, "API'ye ulaşılamadı — sunucu çalışıyor mu? (dotnet run)");
  }

  if (!res.ok) {
    // Token expired/invalid on a protected call → drop it and let the app fall back to the login screen.
    if (res.status === 401 && !path.startsWith("/auth/")) {
      tokenStore.clear();
      window.dispatchEvent(new Event("auth:logout"));
    }
    let msg = `${res.status} ${res.statusText}`;
    try {
      const body = await res.json();
      if (body?.error) msg = body.error;
      else if (body?.message) msg = body.message;
    } catch {
      /* body wasn't JSON — keep the status line */
    }
    throw new ApiError(res.status, msg);
  }

  if (res.status === 204) return undefined as T;
  const text = await res.text();
  return (text ? JSON.parse(text) : undefined) as T;
}

export interface AuthUser {
  id: number;
  email: string;
  username: string;
}

export type KeyField =
  | "gemini"
  | "youtubeApiKey"
  | "youtubeClientId"
  | "youtubeClientSecret"
  | "apify"
  | "pexels"
  | "aiVideo"
  | "telegramBotToken"
  | "telegramChatId";

export type KeyStatusMap = Record<KeyField, { set: boolean; hint: string | null; count?: number }>;

export const api = {
  // ── Auth ──
  register: (email: string, username: string, password: string) =>
    request<{ token: string; user: AuthUser }>("/auth/register", {
      method: "POST",
      body: JSON.stringify({ email, username, password }),
    }),
  login: (emailOrUsername: string, password: string) =>
    request<{ token: string; user: AuthUser }>("/auth/login", {
      method: "POST",
      body: JSON.stringify({ emailOrUsername, password }),
    }),
  me: () => request<{ user: AuthUser }>("/auth/me"),
  getKeys: () => request<KeyStatusMap>("/auth/keys"),
  saveKeys: (keys: Partial<Record<KeyField, string>>) =>
    request<KeyStatusMap>("/auth/keys", { method: "PUT", body: JSON.stringify(keys) }),

  // ── Settings ──
  getSettingsStatus: () => request<SettingsStatus>("/settings/status"),

  // ── Trends ──
  // recentHours: only trends last seen within this many hours (0 = all). Keeps stale trends from past
  // scans out of the list.
  getTrends: (page = 1, pageSize = 50, recentHours = 24) =>
    request<TrendDto[]>(`/trends?page=${page}&pageSize=${pageSize}&recentHours=${recentHours}`),
  scanTrends: (region = "TR") =>
    request<{ totalTrends: number; byPlatform: Record<string, number> }>(
      `/trends/scan?region=${region}`,
      { method: "POST" }
    ),
  evaluateFeasibility: (topN = 10, force = false) =>
    request<TrendDto[]>(
      `/trends/evaluate-feasibility?topN=${topN}&force=${force}`,
      { method: "POST" }
    ),
  analyzeTrend: (id: number, templateId?: number) =>
    request<VideoDto>(
      `/trends/${id}/analyze${templateId ? `?templateId=${templateId}` : ""}`,
      { method: "POST" }
    ),
  // Manual analysis for TikTok/Instagram: the user describes what the video is about; Gemini derives the
  // feasibility score + scene script from that description.
  analyzeTrendManual: (id: number, description: string, templateId?: number) =>
    request<VideoDto>(`/trends/${id}/analyze-manual`, {
      method: "POST",
      body: JSON.stringify({ description, templateId }),
    }),

  // ── Videos ──
  getVideos: (page = 1, pageSize = 50) =>
    request<VideoDto[]>(`/videos?page=${page}&pageSize=${pageSize}`),
  getVideo: (id: number) => request<VideoDto>(`/videos/${id}`),
  updateScene: (
    videoId: number,
    sceneId: number,
    data: { narrationText?: string; videoPrompt?: string; visualKeywordA?: string; visualKeywordB?: string }
  ) =>
    request<VideoDto>(`/videos/${videoId}/scenes/${sceneId}`, {
      method: "PUT",
      body: JSON.stringify(data),
    }),
  generateVideo: (id: number, visualMode?: string) =>
    request<VideoDto>(
      `/videos/${id}/generate${visualMode ? `?visualMode=${visualMode}` : ""}`,
      { method: "POST" }
    ),
  publishVideo: (id: number, platform: string) =>
    request<unknown>(`/videos/${id}/publish/${platform}`, { method: "POST" }),
  deleteVideo: (id: number) =>
    request<void>(`/videos/${id}`, { method: "DELETE" }),
  videoFileUrl: (id: number) => `${BASE}/videos/${id}/file`,

  // ── Chat ──
  chat: (messages: { role: string; content: string }[]) =>
    request<{ reply: string }>("/chat", {
      method: "POST",
      body: JSON.stringify({ messages }),
    }),
  generateVideoFromChat: (idea: string, visualMode?: string, templateId?: number) =>
    request<VideoDto>("/chat/generate-video", {
      method: "POST",
      body: JSON.stringify({ idea, visualMode, templateId }),
    }),

  // ── Templates ──
  getTemplates: () => request<TemplateDto[]>("/templates"),

  // ── Pipeline ──
  runPipeline: (region = "TR") =>
    request<{ message: string; hangfireJobId: string }>(
      `/pipeline/run-now?region=${region}`,
      { method: "POST" }
    ),
  getRuns: (page = 1, pageSize = 20) =>
    request<PipelineStatusDto[]>(
      `/pipeline/runs?page=${page}&pageSize=${pageSize}`
    ),
  getRun: (id: number) => request<PipelineStatusDto>(`/pipeline/runs/${id}`),

  // ── Telegram (autonomous flow) ──
  // Manually fire the same candidate batch the peak-hour jobs send — for testing without waiting for 12/19/21.
  triggerTelegramBatch: () =>
    request<{ message: string; hangfireJobId: string }>("/telegram/batch-now", {
      method: "POST",
    }),

  // ── YouTube OAuth (one-time consent for uploads) ──
  getYouTubeAuthStatus: () =>
    request<{ authorized: boolean }>("/youtube/auth-status"),
  authorizeYouTube: () =>
    request<{ authorized: boolean; channel?: string; message: string }>(
      "/youtube/authorize",
      { method: "POST" }
    ),

  // ── Performance (published-video stats) ──
  getPerformance: () => request<PerformanceResponse>("/performance"),
  refreshPerformance: () =>
    request<{ written: number; message: string }>("/performance/refresh", { method: "POST" }),

  // ── Usage / cost ──
  getUsage: () => request<UsageResponse>("/usage"),

  // ── Content calendar (scheduled full-pipeline runs) ──
  getSchedules: () => request<ScheduledRunDto[]>("/schedule"),
  createSchedule: (scheduledAtUtcIso: string, region = "TR") =>
    request<ScheduledRunDto>("/schedule", {
      method: "POST",
      body: JSON.stringify({ scheduledAt: scheduledAtUtcIso, region }),
    }),
  cancelSchedule: (id: number) =>
    request<ScheduledRunDto>(`/schedule/${id}`, { method: "DELETE" }),
};

export interface ScheduledRunDto {
  id: number;
  scheduledAt: string;
  region: string;
  status: "Pending" | "Running" | "Completed" | "Failed" | "Canceled";
  resultVideoId: number | null;
  errorMessage: string | null;
  createdAt: string;
}

export interface UsageResponse {
  gemini: { today: number; month: number; note: string };
  apify: { configured: boolean; monthlyUsageUsd: number | null; note: string };
  pexels: { configured: boolean; note: string };
}

export interface PerformanceVideo {
  videoId: number;
  title: string;
  url: string | null;
  publishedAt: string | null;
  viewCount: number;
  likeCount: number;
  commentCount: number;
  capturedAt: string | null;
  viralityScore: number | null;   // source trend's predicted virality (null for idea/chat videos)
  feasibilityScore: number | null; // source trend's predicted AI-feasibility
}
export interface PerformanceResponse {
  videos: PerformanceVideo[];
  summary: { count: number; totalViews: number; avgViews: number };
}

export { ApiError };
