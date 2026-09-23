import { useEffect, useMemo, useState } from "react";
import { api } from "../api/client";
import type { UsageResponse } from "../api/client";
import type { PipelineStatusDto, SettingsStatus } from "../api/types";
import { usePolling } from "../hooks/usePolling";
import { formatDate, formatNumber, Spinner, useToast } from "../components/common";

const STEPS = ["Tarama", "Seçim", "Analiz", "Üretim"];

const SERVICE_LABELS: { key: keyof SettingsStatus; label: string }[] = [
  { key: "geminiConfigured", label: "Gemini" },
  { key: "youtubeConfigured", label: "YouTube" },
  { key: "apifyConfigured", label: "Apify" },
  { key: "pexelsConfigured", label: "Pexels" },
  { key: "aiVideoConfigured", label: "AI Video" },
  { key: "telegramConfigured", label: "Telegram" },
];

function RunCard({ run }: { run: PipelineStatusDto }) {
  const isRunning = run.status === "running";
  const isFailed = run.status === "failed";
  const barCls = isFailed ? "failed" : run.status === "completed" ? "done" : "";

  return (
    <div className="card">
      <div className="row-between" style={{ marginBottom: 12 }}>
        <div className="row">
          <strong>Çalıştırma #{run.id}</strong>
          {isRunning && <span className="badge badge-yellow"><Spinner /> Çalışıyor</span>}
          {run.status === "completed" && <span className="badge badge-green">Tamamlandı</span>}
          {isFailed && <span className="badge badge-red">Başarısız</span>}
        </div>
        <span className="faint" style={{ fontSize: 12 }}>
          {formatDate(run.startedAt)}
          {run.videoId ? ` · 🎬 video #${run.videoId}` : ""}
        </span>
      </div>

      <div className={`progress ${barCls}`}>
        <div style={{ width: `${run.progressPercent}%` }} />
      </div>

      <div className="steps">
        {STEPS.map((step, i) => {
          const done = i < run.completedSteps;
          const active = isRunning && step === run.currentStep;
          return (
            <div key={step} className={`step ${done ? "done" : ""} ${active ? "active" : ""}`}>
              {done ? "✓" : active ? <Spinner /> : "○"} {step}
            </div>
          );
        })}
      </div>

      {run.errorMessage && (
        <div className="err-banner" style={{ marginTop: 10, marginBottom: 0 }}>
          ⚠️ {run.errorMessage}
        </div>
      )}

      {run.logOutput && <pre className="log" style={{ marginTop: 10 }}>{run.logOutput.trim()}</pre>}
    </div>
  );
}

export default function DashboardPage() {
  const toast = useToast();
  const [starting, setStarting] = useState(false);
  const [sendingBatch, setSendingBatch] = useState(false);
  const [ytAuth, setYtAuth] = useState<boolean | null>(null);
  const [authing, setAuthing] = useState(false);

  const settings = usePolling<SettingsStatus>(() => api.getSettingsStatus(), null);
  const usage = usePolling<UsageResponse>(() => api.getUsage(), 60000); // slow heartbeat; usage changes slowly

  useEffect(() => {
    api.getYouTubeAuthStatus().then((r) => setYtAuth(r.authorized)).catch(() => setYtAuth(null));
  }, []);

  // Poll fast (2.5s) while a run is in progress, then drop to a 15s heartbeat once everything
  // is finished — no point hammering the API to re-render an unchanging list. `anyRunning` is
  // derived from the hook's own latest data; usePolling re-subscribes when the interval changes.
  const [pollMs, setPollMs] = useState(2500);
  const [runLimit, setRunLimit] = useState(15); // grown by "daha fazla göster"
  const runsResult = usePolling<PipelineStatusDto[]>(() => api.getRuns(1, runLimit), pollMs, [runLimit]);
  const displayRuns = runsResult.data ?? [];
  const anyRunning = useMemo(() => displayRuns.some((r) => r.status === "running"), [displayRuns]);
  useEffect(() => {
    setPollMs(anyRunning ? 2500 : 15000);
  }, [anyRunning]);

  async function handleRun() {
    setStarting(true);
    try {
      await api.runPipeline("TR");
      toast.push("Pipeline kuyruğa alındı — ilerleme aşağıda görünecek.", "ok");
      setTimeout(() => runsResult.refresh(), 800);
    } catch (e) {
      toast.push(e instanceof Error ? e.message : "Başlatılamadı", "err");
    } finally {
      setStarting(false);
    }
  }

  async function handleTelegramBatch() {
    setSendingBatch(true);
    try {
      const r = await api.triggerTelegramBatch();
      toast.push(r.message ?? "Telegram partisi kuyruğa alındı.", "ok");
    } catch (e) {
      toast.push(e instanceof Error ? e.message : "Gönderilemedi", "err");
    } finally {
      setSendingBatch(false);
    }
  }

  async function handleYouTubeAuth() {
    setAuthing(true);
    toast.push("Tarayıcı açılıyor — YouTube hesabınla izin ver (bu makinede).", "ok");
    try {
      const r = await api.authorizeYouTube();
      setYtAuth(r.authorized);
      toast.push(r.message ?? (r.authorized ? "Yetkilendirildi" : "Yetkilendirilemedi"), r.authorized ? "ok" : "err");
    } catch (e) {
      toast.push(e instanceof Error ? e.message : "Yetkilendirme başarısız", "err");
    } finally {
      setAuthing(false);
    }
  }

  const s = settings.data;

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Panel</h1>
          <p>Pipeline'ı elle çalıştır ve tüm çalıştırmaların ilerlemesini canlı izle.</p>
        </div>
        <button className="btn btn-primary" onClick={handleRun} disabled={starting}>
          {starting ? <Spinner /> : "▶"} Şimdi Çalıştır
        </button>
      </div>

      {runsResult.error && <div className="err-banner">{runsResult.error}</div>}

      {/* Service status strip */}
      <div className="card">
        <h2>Servis Durumu</h2>
        <div className="row">
          {SERVICE_LABELS.map(({ key, label }) => {
            const ok = s ? Boolean(s[key]) : false;
            return (
              <span key={key} className={`badge ${ok ? "badge-green" : "badge-gray"}`}>
                {ok ? "●" : "○"} {label}
              </span>
            );
          })}
          {s && (
            <span className="faint" style={{ marginLeft: "auto", fontSize: 12 }}>
              Bölge: {s.region} · Dil: {s.language}
            </span>
          )}
        </div>
        {s && s.geminiKeyCount > 0 && (
          <div className="faint" style={{ fontSize: 12, marginTop: 10 }}>
            🔑 Gemini anahtar havuzu: <strong>{s.geminiKeyCount}</strong> anahtar ·{" "}
            {s.geminiKeysAvailable === s.geminiKeyCount
              ? "hepsi hazır"
              : `${s.geminiKeysAvailable} hazır, ${s.geminiKeyCount - s.geminiKeysAvailable} kota doldu`}
          </div>
        )}
      </div>

      {/* API usage / cost */}
      {usage.data && (
        <div className="card">
          <h2>API Kullanımı</h2>
          <div className="row" style={{ gap: 28, flexWrap: "wrap" }}>
            <div>
              <div className="faint" style={{ fontSize: 12 }}>GEMINI (çağrı)</div>
              <strong style={{ fontSize: 18 }}>{formatNumber(usage.data.gemini.today)}</strong>
              <span className="faint" style={{ fontSize: 12 }}> bugün · {formatNumber(usage.data.gemini.month)} bu ay</span>
            </div>
            <div>
              <div className="faint" style={{ fontSize: 12 }}>APIFY (bu ay)</div>
              {usage.data.apify.configured ? (
                <strong style={{ fontSize: 18 }}>
                  {usage.data.apify.monthlyUsageUsd != null ? `$${usage.data.apify.monthlyUsageUsd.toFixed(2)}` : "—"}
                </strong>
              ) : (
                <span className="badge badge-gray">yapılandırılmamış</span>
              )}
            </div>
            <div>
              <div className="faint" style={{ fontSize: 12 }}>PEXELS</div>
              <span className={`badge ${usage.data.pexels.configured ? "badge-green" : "badge-gray"}`}>
                {usage.data.pexels.configured ? "● limitsiz" : "○ yok"}
              </span>
            </div>
          </div>
          <div className="faint" style={{ fontSize: 11, marginTop: 8 }}>{usage.data.gemini.note}</div>
        </div>
      )}

      {/* Telegram autonomous flow — background schedule + manual test trigger */}
      <div className="card">
        <h2>Telegram Otonom Akışı</h2>
        <p className="faint" style={{ fontSize: 13, marginTop: 0 }}>
          Sistem arka planda açık kaldıkça her gün <strong>12:00, 19:00, 21:00</strong> (TR) Telegram'a güncel
          viral aday videoları gönderir; sen seçince analiz → üretim → YouTube yayını akışı işler. Beklemeden
          test etmek için aşağıdan elle bir parti gönder.
        </p>
        <div className="row">
          <button
            className="btn btn-primary"
            onClick={handleTelegramBatch}
            disabled={sendingBatch || !s?.telegramConfigured}
          >
            {sendingBatch ? <Spinner /> : "📲"} Telegram'a test partisi gönder
          </button>
          {ytAuth === false && (
            <button className="btn" onClick={handleYouTubeAuth} disabled={authing}>
              {authing ? <Spinner /> : "🔑"} YouTube'u yetkilendir
            </button>
          )}
          <span
            className={`badge ${ytAuth === null ? "badge-gray" : ytAuth ? "badge-green" : "badge-yellow"}`}
            style={{ marginLeft: 4 }}
          >
            YouTube yayını: {ytAuth === null ? "…" : ytAuth ? "✅ hazır" : "⚠️ yetki gerekli"}
          </span>
        </div>
        {!s?.telegramConfigured && (
          <div className="faint" style={{ fontSize: 12, marginTop: 8 }}>
            Telegram yapılandırılmamış (.env: TELEGRAM_BOT_TOKEN / TELEGRAM_CHAT_ID).
          </div>
        )}
      </div>

      {/* Runs */}
      <div className="row-between" style={{ margin: "6px 2px 12px" }}>
        <h2 style={{ margin: 0, fontSize: 15 }}>Pipeline Çalıştırmaları</h2>
        {anyRunning && <span className="faint" style={{ fontSize: 12 }}><Spinner /> canlı güncelleniyor</span>}
      </div>

      {displayRuns.length === 0 ? (
        <div className="card empty">
          Henüz çalıştırma yok. Yukarıdan <strong>Şimdi Çalıştır</strong>'a bas.
        </div>
      ) : (
        <>
          {displayRuns.map((run) => <RunCard key={run.id} run={run} />)}
          {/* A full page suggests there may be older runs — grow the limit to fetch more. */}
          {displayRuns.length >= runLimit && (
            <div className="pager">
              <button className="btn btn-sm" disabled={runsResult.loading} onClick={() => setRunLimit((l) => l + 15)}>
                ⌄ Daha fazla göster
              </button>
            </div>
          )}
        </>
      )}
    </>
  );
}
