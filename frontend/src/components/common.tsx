import { createContext, useCallback, useContext, useState } from "react";
import type { ReactNode } from "react";

// ── Formatting helpers ──
export function formatNumber(n: number): string {
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1).replace(/\.0$/, "") + "M";
  if (n >= 1_000) return (n / 1_000).toFixed(1).replace(/\.0$/, "") + "K";
  return String(n);
}

export function formatDate(iso?: string | null): string {
  if (!iso) return "—";
  const d = new Date(iso);
  return d.toLocaleString("tr-TR", {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

export function timeAgo(iso?: string | null): string {
  if (!iso) return "—";
  const secs = Math.floor((Date.now() - new Date(iso).getTime()) / 1000);
  if (secs < 60) return `${secs}sn önce`;
  if (secs < 3600) return `${Math.floor(secs / 60)}dk önce`;
  if (secs < 86400) return `${Math.floor(secs / 3600)}sa önce`;
  return `${Math.floor(secs / 86400)}g önce`;
}

// ── Platform badge ──
export function PlatformBadge({ platform }: { platform: string }) {
  const p = platform.toLowerCase();
  const cls = p.includes("youtube")
    ? "badge-red"
    : p.includes("tiktok")
    ? "badge-purple"
    : p.includes("instagram")
    ? "badge-yellow"
    : "badge-gray";
  return <span className={`badge ${cls}`}>{platform}</span>;
}

// ── Video status badge ──
const STATUS_TR: Record<string, { label: string; cls: string }> = {
  Discovered: { label: "Keşfedildi", cls: "badge-gray" },
  ScriptGenerated: { label: "Senaryo Hazır", cls: "badge-blue" },
  AssetsDownloading: { label: "Varlıklar İniyor", cls: "badge-yellow" },
  AssetsReady: { label: "Varlıklar Hazır", cls: "badge-blue" },
  Composing: { label: "Birleştiriliyor", cls: "badge-yellow" },
  Composed: { label: "Üretildi", cls: "badge-green" },
  PendingApproval: { label: "Onay Bekliyor", cls: "badge-purple" },
  Publishing: { label: "Yayınlanıyor", cls: "badge-yellow" },
  Published: { label: "Yayınlandı", cls: "badge-green" },
  Failed: { label: "Başarısız", cls: "badge-red" },
  Rejected: { label: "Reddedildi", cls: "badge-red" },
};

export function VideoStatusBadge({ status }: { status: string }) {
  const s = STATUS_TR[status] ?? { label: status, cls: "badge-gray" };
  return <span className={`badge ${s.cls}`}>{s.label}</span>;
}

// ── Feasibility / virality score chip ──
export function ScoreChip({ score, suffix = "" }: { score: number | null | undefined; suffix?: string }) {
  if (score == null) return <span className="faint">—</span>;
  const cls = score >= 70 ? "badge-green" : score >= 45 ? "badge-yellow" : "badge-red";
  return <span className={`badge ${cls}`}>{Math.round(score)}{suffix}</span>;
}

// ── Toast system ──
interface Toast {
  id: number;
  message: string;
  kind: "ok" | "err" | "info";
}
interface ToastCtx {
  push: (message: string, kind?: Toast["kind"]) => void;
}
const ToastContext = createContext<ToastCtx>({ push: () => {} });
export const useToast = () => useContext(ToastContext);

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const push = useCallback((message: string, kind: Toast["kind"] = "info") => {
    const id = Date.now() + Math.random();
    setToasts((t) => [...t, { id, message, kind }]);
    setTimeout(() => setToasts((t) => t.filter((x) => x.id !== id)), 4500);
  }, []);
  return (
    <ToastContext.Provider value={{ push }}>
      {children}
      <div className="toast-stack">
        {toasts.map((t) => (
          <div key={t.id} className={`toast ${t.kind}`}>
            {t.message}
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

// ── Small inline spinner ──
export const Spinner = () => <span className="spin">⟳</span>;

/**
 * Prev/Next pager. The API returns a page of rows without a total count, so "has next" is inferred: a full
 * page (count === pageSize) means there's probably more. Renders nothing when there's only a single page.
 */
export function Pager({
  page,
  pageSize,
  count,
  onPage,
  loading,
}: {
  page: number;
  pageSize: number;
  count: number;
  onPage: (p: number) => void;
  loading?: boolean;
}) {
  const hasNext = count === pageSize;
  const hasPrev = page > 1;
  if (page === 1 && !hasNext) return null; // single page — no controls needed
  return (
    <div className="pager">
      <button className="btn btn-sm" disabled={!hasPrev || loading} onClick={() => onPage(page - 1)}>
        ← Önceki
      </button>
      <span className="pager-info">Sayfa {page}</span>
      <button className="btn btn-sm" disabled={!hasNext || loading} onClick={() => onPage(page + 1)}>
        Sonraki →
      </button>
    </div>
  );
}
