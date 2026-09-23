import { useState } from "react";
import { api } from "../api/client";
import type { TrendDto } from "../api/types";
import { usePolling } from "../hooks/usePolling";
import {
  formatNumber,
  PlatformBadge,
  Pager,
  ScoreChip,
  Spinner,
  useToast,
} from "../components/common";

const PAGE_SIZE = 20;

export default function TrendsPage() {
  const toast = useToast();
  const [busy, setBusy] = useState<string | null>(null); // global action in flight
  const [analyzing, setAnalyzing] = useState<number | null>(null); // per-row analyze
  const [recentHours, setRecentHours] = useState(24); // only trends seen within this window
  const [manualTrend, setManualTrend] = useState<TrendDto | null>(null); // TikTok/IG manual-analyze modal
  const [manualDesc, setManualDesc] = useState("");
  const [manualBusy, setManualBusy] = useState(false);
  const [expanded, setExpanded] = useState<Set<number>>(new Set()); // rows showing full konu/reason text

  const toggleExpand = (id: number) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });

  const [page, setPage] = useState(1);
  const { data, error, loading, refresh } = usePolling<TrendDto[]>(
    () => api.getTrends(page, PAGE_SIZE, recentHours),
    null,
    [recentHours, page]
  );
  const trends = data ?? [];

  async function runAction(
    label: string,
    fn: () => Promise<unknown>,
    okMsg: string
  ) {
    setBusy(label);
    try {
      await fn();
      toast.push(okMsg, "ok");
      refresh();
    } catch (e) {
      toast.push(e instanceof Error ? e.message : "Hata", "err");
    } finally {
      setBusy(null);
    }
  }

  async function handleAnalyze(t: TrendDto) {
    setAnalyzing(t.id);
    try {
      const video = await api.analyzeTrend(t.id);
      toast.push(`Senaryo üretildi → video #${video.id}. "Videolar" sekmesinden üret.`, "ok");
    } catch (e) {
      toast.push(e instanceof Error ? e.message : "Analiz başarısız", "err");
    } finally {
      // Refresh even on failure: the video understanding (konu + uygunluk) is saved before the script step,
      // so if only the script failed, the trend row still updates with what was captured.
      refresh();
      setAnalyzing(null);
    }
  }

  async function submitManual() {
    if (!manualTrend || !manualDesc.trim()) return;
    setManualBusy(true);
    try {
      const video = await api.analyzeTrendManual(manualTrend.id, manualDesc.trim());
      toast.push(`Senaryo üretildi → video #${video.id}. "Videolar" sekmesinden üret.`, "ok");
      setManualTrend(null);
      setManualDesc("");
      refresh();
    } catch (e) {
      toast.push(e instanceof Error ? e.message : "Manuel analiz başarısız", "err");
    } finally {
      setManualBusy(false);
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Trendler</h1>
          <p>Güncel trendler; viralite ve AI ile üretilebilirlik puanına göre.</p>
        </div>
        <div className="row">
          <select
            value={recentHours}
            onChange={(e) => { setRecentHours(Number(e.target.value)); setPage(1); }}
            disabled={loading}
            title="Yalnızca bu süre içinde görülen trendleri göster"
          >
            <option value={1}>Son 1 saat</option>
            <option value={3}>Son 3 saat</option>
            <option value={6}>Son 6 saat</option>
            <option value={12}>Son 12 saat</option>
            <option value={24}>Son 24 saat</option>
            <option value={48}>Son 2 gün</option>
            <option value={72}>Son 3 gün</option>
            <option value={168}>Son 7 gün</option>
            <option value={336}>Son 14 gün</option>
            <option value={720}>Son 30 gün</option>
            <option value={0}>Tümü</option>
          </select>
          <button
            className="btn"
            disabled={busy !== null}
            onClick={() =>
              runAction(
                "scan",
                () => api.scanTrends("TR"),
                "Tarama tamamlandı."
              )
            }
          >
            {busy === "scan" ? <Spinner /> : "🔍"} Tara
          </button>
          <button
            className="btn"
            disabled={busy !== null}
            onClick={() =>
              runAction(
                "feas",
                () => api.evaluateFeasibility(10, false),
                "Uygunluk değerlendirmesi tamamlandı."
              )
            }
          >
            {busy === "feas" ? <Spinner /> : "🤖"} Uygunluk Değerlendir
          </button>
          <button className="btn" disabled={loading} onClick={refresh}>
            ↻ Yenile
          </button>
        </div>
      </div>

      {error && <div className="err-banner">{error}</div>}
      {busy === "scan" && (
        <div className="err-banner" style={{ background: "rgba(91,140,255,.1)", borderColor: "rgba(91,140,255,.3)", color: "#bcd0ff" }}>
          Tarama sürüyor — YouTube/TikTok/Instagram taranıyor, bir dakika kadar sürebilir…
        </div>
      )}

      <div className="card" style={{ padding: 0, overflow: "hidden" }}>
        {loading && trends.length === 0 ? (
          <div className="empty"><Spinner /> Yükleniyor…</div>
        ) : trends.length === 0 ? (
          <div className="empty">
            {recentHours > 0 ? (
              <>Bu zaman aralığında güncel trend yok. Aralığı genişlet (<strong>Tümü</strong>) ya da <strong>Tara</strong>'ya bas.</>
            ) : (
              <>Trend yok. Yukarıdan <strong>Tara</strong>'ya bas.</>
            )}
          </div>
        ) : (
          <div style={{ overflowX: "auto" }}>
            <table>
              <thead>
                <tr>
                  <th>Platform</th>
                  <th>Başlık</th>
                  <th>İzlenme</th>
                  <th>Viralite</th>
                  <th>AI Uygunluk</th>
                  <th>Dil</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {trends.map((t) => (
                  <tr key={t.id}>
                    <td><PlatformBadge platform={t.sourcePlatform} /></td>
                    <td style={{ maxWidth: 340 }}>
                      <a href={t.sourceUrl} target="_blank" rel="noreferrer" className="clamp2">
                        {t.title || "(başlıksız)"}
                      </a>
                      {t.contentSummary && (
                        <div
                          className={`faint ${expanded.has(t.id) ? "" : "clamp2"}`}
                          style={{ fontSize: 11, marginTop: 4, cursor: "pointer" }}
                          onClick={() => toggleExpand(t.id)}
                          title="Tıkla: tamamını göster/gizle"
                        >
                          🎬 {t.contentSummary}
                        </div>
                      )}
                    </td>
                    <td>{formatNumber(t.viewCount)}</td>
                    <td>
                      <ScoreChip score={t.finalViralityScore} />
                    </td>
                    <td>
                      <ScoreChip score={t.aiFeasibilityScore} />
                      {t.aiFeasibilityReason && (
                        <div
                          className={`faint ${expanded.has(t.id) ? "" : "clamp2"}`}
                          style={{ fontSize: 11, maxWidth: 240, marginTop: 3, cursor: "pointer" }}
                          onClick={() => toggleExpand(t.id)}
                          title="Tıkla: tamamını göster/gizle"
                        >
                          {t.aiFeasibilityReason}
                        </div>
                      )}
                    </td>
                    <td className="faint">{t.languageCode ?? "—"}</td>
                    <td>
                      {t.sourcePlatform === "YouTube" ? (
                        <button
                          className="btn btn-sm btn-primary"
                          disabled={analyzing !== null}
                          onClick={() => handleAnalyze(t)}
                          title="Gemini videoyu izleyip otomatik analiz eder"
                        >
                          {analyzing === t.id ? <Spinner /> : "Analiz Et"}
                        </button>
                      ) : (
                        <button
                          className="btn btn-sm"
                          onClick={() => { setManualTrend(t); setManualDesc(""); }}
                          title="Videoyu sen izleyip anlamını yaz, Gemini senaryo üretsin"
                        >
                          ✍️ Manuel
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      <Pager page={page} pageSize={PAGE_SIZE} count={trends.length} onPage={setPage} loading={loading} />
      <p className="faint" style={{ fontSize: 12 }}>
        Not: <strong>YouTube</strong> → "Analiz Et" (Gemini videoyu otomatik izler). <strong>TikTok/Instagram</strong>
        → "✍️ Manuel" (videoyu sen izleyip anlat, Gemini senaryo üretir). Otomatik pipeline yalnızca YouTube ile çalışır.
      </p>

      {manualTrend && (
        <div className="modal-overlay" onClick={() => !manualBusy && setManualTrend(null)}>
          <div className="modal" onClick={(e) => e.stopPropagation()}>
            <div className="modal-head">
              <strong className="row" style={{ gap: 8 }}>
                ✍️ Manuel Analiz <PlatformBadge platform={manualTrend.sourcePlatform} />
              </strong>
              <button className="btn btn-sm" disabled={manualBusy} onClick={() => setManualTrend(null)}>✕</button>
            </div>
            <div className="modal-body">
              <p className="muted" style={{ marginTop: 0 }}>
                Videoyu izle, ne hakkında olduğunu yaz — Gemini bu açıklamadan hem <strong>AI-uygunluk puanını</strong> hem
                de <strong>senaryoyu</strong> üretecek.
              </p>
              <div className="row" style={{ marginBottom: 12 }}>
                <a href={manualTrend.sourceUrl} target="_blank" rel="noreferrer" className="btn btn-sm">
                  ▶ Videoyu izle
                </a>
                <span className="faint clamp2" style={{ fontSize: 12, flex: 1 }}>{manualTrend.title}</span>
              </div>
              <textarea
                value={manualDesc}
                onChange={(e) => setManualDesc(e.target.value)}
                placeholder="Videoda ne oluyor? Kim var, ne yapıyor, esprisi/mesajı ne? Ne kadar detaylı yazarsan senaryo o kadar isabetli olur."
                rows={6}
                style={{ width: "100%" }}
                disabled={manualBusy}
                autoFocus
              />
              <div className="row-between" style={{ marginTop: 12 }}>
                <span className="faint" style={{ fontSize: 12 }}>{manualDesc.trim().length} karakter</span>
                <button
                  className="btn btn-primary"
                  disabled={manualBusy || manualDesc.trim().length < 10}
                  onClick={submitManual}
                >
                  {manualBusy ? <><Spinner /> Üretiliyor…</> : "✨ Uygunluk + Senaryo Üret"}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
