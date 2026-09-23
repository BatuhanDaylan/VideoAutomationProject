import { useState } from "react";
import { api } from "../api/client";
import type { PerformanceResponse } from "../api/client";
import { usePolling } from "../hooks/usePolling";
import { formatDate, formatNumber, ScoreChip, Spinner, useToast } from "../components/common";

export default function PerformancePage() {
  const toast = useToast();
  const [refreshing, setRefreshing] = useState(false);
  const { data, error, loading, refresh } = usePolling<PerformanceResponse>(
    () => api.getPerformance(),
    null
  );

  const videos = data?.videos ?? [];
  const summary = data?.summary;
  // Videos exist but none has a captured stat yet → almost always because they were uploaded 'private'
  // (an API key can't read private videos' public stats). Surface that so 0'lar bir hata sanılmasın.
  const allUncaptured = videos.length > 0 && videos.every((v) => v.capturedAt == null);

  async function handleRefresh() {
    setRefreshing(true);
    try {
      const r = await api.refreshPerformance();
      toast.push(r.message, r.written > 0 ? "ok" : "info");
      refresh();
    } catch (e) {
      toast.push(e instanceof Error ? e.message : "Yenilenemedi", "err");
    } finally {
      setRefreshing(false);
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Performans</h1>
          <p>Yayınlanan videoların güncel izlenme/beğeni/yorum sayıları. Günlük otomatik güncellenir.</p>
        </div>
        <button className="btn btn-primary" onClick={handleRefresh} disabled={refreshing}>
          {refreshing ? <Spinner /> : "↻"} Şimdi güncelle
        </button>
      </div>

      {error && <div className="err-banner">{error}</div>}

      {allUncaptured && (
        <div className="err-banner" style={{ background: "rgba(91,140,255,.1)", borderColor: "rgba(91,140,255,.3)", color: "#bcd0ff" }}>
          Sayılar 0 çünkü videolar büyük olasılıkla <strong>private</strong> yüklendi — API anahtarı private
          videoların istatistiğini göremez. YouTube'da videoları <strong>unlisted/public</strong> yapıp
          "Şimdi güncelle"ye bas (veya <code>Media:YouTube:PrivacyStatus</code> ayarını değiştir).
        </div>
      )}

      {summary && summary.count > 0 && (
        <div className="card">
          <div className="row" style={{ gap: 24 }}>
            <div><div className="faint" style={{ fontSize: 12 }}>YAYINLANAN VİDEO</div><strong style={{ fontSize: 20 }}>{summary.count}</strong></div>
            <div><div className="faint" style={{ fontSize: 12 }}>TOPLAM İZLENME</div><strong style={{ fontSize: 20 }}>{formatNumber(summary.totalViews)}</strong></div>
            <div><div className="faint" style={{ fontSize: 12 }}>ORTALAMA İZLENME</div><strong style={{ fontSize: 20 }}>{formatNumber(summary.avgViews)}</strong></div>
          </div>
        </div>
      )}

      <div className="card" style={{ padding: 0, overflow: "hidden" }}>
        {loading && videos.length === 0 ? (
          <div className="empty"><Spinner /> Yükleniyor…</div>
        ) : videos.length === 0 ? (
          <div className="empty">
            Henüz yayınlanmış video yok — bir video üretip YouTube'a yükleyince performansı burada görünür.
          </div>
        ) : (
          <div style={{ overflowX: "auto" }}>
            <table>
              <thead>
                <tr>
                  <th>Başlık</th>
                  <th>İzlenme</th>
                  <th>Beğeni</th>
                  <th>Yorum</th>
                  <th title="Kaynak trendin tahmini viralite skoru">Viralite</th>
                  <th title="Kaynak trendin tahmini AI-uygunluk skoru">AI Uygunluk</th>
                  <th>Ölçüm</th>
                </tr>
              </thead>
              <tbody>
                {videos.map((v) => (
                  <tr key={v.videoId}>
                    <td style={{ maxWidth: 360 }}>
                      {v.url ? (
                        <a href={v.url} target="_blank" rel="noreferrer" className="clamp2">{v.title || "(başlıksız)"}</a>
                      ) : (
                        <span className="clamp2">{v.title || "(başlıksız)"}</span>
                      )}
                    </td>
                    <td><strong>{formatNumber(v.viewCount)}</strong></td>
                    <td>{formatNumber(v.likeCount)}</td>
                    <td>{formatNumber(v.commentCount)}</td>
                    <td><ScoreChip score={v.viralityScore} /></td>
                    <td><ScoreChip score={v.feasibilityScore} /></td>
                    <td className="faint" style={{ fontSize: 12 }}>{v.capturedAt ? formatDate(v.capturedAt) : "— henüz ölçülmedi"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </>
  );
}
