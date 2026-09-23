import { useState } from "react";
import { api } from "../api/client";
import type { ScheduledRunDto } from "../api/client";
import { usePolling } from "../hooks/usePolling";
import { formatDate, Spinner, useToast } from "../components/common";

const STATUS_TR: Record<ScheduledRunDto["status"], { label: string; cls: string }> = {
  Pending: { label: "Bekliyor", cls: "badge-blue" },
  Running: { label: "Çalışıyor", cls: "badge-yellow" },
  Completed: { label: "Tamamlandı", cls: "badge-green" },
  Failed: { label: "Başarısız", cls: "badge-red" },
  Canceled: { label: "İptal edildi", cls: "badge-gray" },
};

export default function SchedulePage() {
  const toast = useToast();
  const [when, setWhen] = useState("");
  const [region, setRegion] = useState("TR");
  const [creating, setCreating] = useState(false);

  // Poll a bit faster while something is running so status flips (Pending→Running→Completed) show up live.
  const { data, error, loading, refresh } = usePolling<ScheduledRunDto[]>(() => api.getSchedules(), 10000);
  const runs = data ?? [];

  async function create() {
    if (!when) {
      toast.push("Bir tarih/saat seç.", "err");
      return;
    }
    setCreating(true);
    try {
      // datetime-local gives a naive local time; convert to a UTC ISO string for the API.
      const iso = new Date(when).toISOString();
      await api.createSchedule(iso, region);
      toast.push("Çalıştırma planlandı.", "ok");
      setWhen("");
      refresh();
    } catch (e) {
      toast.push(e instanceof Error ? e.message : "Planlanamadı", "err");
    } finally {
      setCreating(false);
    }
  }

  async function cancel(id: number) {
    if (!confirm("Bu planlı çalıştırma iptal edilsin mi?")) return;
    try {
      await api.cancelSchedule(id);
      toast.push("İptal edildi.", "ok");
      refresh();
    } catch (e) {
      toast.push(e instanceof Error ? e.message : "İptal edilemedi", "err");
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>İçerik Takvimi</h1>
          <p>Tüm hattı (tara → analiz → üret → yayınla) ileri bir tarih/saate planla. Zamanı gelince otomatik çalışır.</p>
        </div>
      </div>

      {error && <div className="err-banner">{error}</div>}

      <div className="card">
        <h2>Yeni planlama</h2>
        <div className="row" style={{ alignItems: "flex-end", flexWrap: "wrap", gap: 12 }}>
          <div>
            <label className="faint" style={{ fontSize: 12, display: "block", marginBottom: 4 }}>Tarih & saat</label>
            <input type="datetime-local" value={when} onChange={(e) => setWhen(e.target.value)} disabled={creating} />
          </div>
          <div>
            <label className="faint" style={{ fontSize: 12, display: "block", marginBottom: 4 }}>Bölge</label>
            <input value={region} onChange={(e) => setRegion(e.target.value)} style={{ width: 80 }} disabled={creating} />
          </div>
          <button className="btn btn-primary" onClick={create} disabled={creating}>
            {creating ? <Spinner /> : "📅"} Planla
          </button>
        </div>
      </div>

      <div className="card" style={{ padding: 0, overflow: "hidden" }}>
        {loading && runs.length === 0 ? (
          <div className="empty"><Spinner /> Yükleniyor…</div>
        ) : runs.length === 0 ? (
          <div className="empty">Henüz planlanmış çalıştırma yok — yukarıdan bir tarih/saat seçip planla.</div>
        ) : (
          <div style={{ overflowX: "auto" }}>
            <table>
              <thead>
                <tr>
                  <th>Zaman</th>
                  <th>Bölge</th>
                  <th>Durum</th>
                  <th>Sonuç</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {runs.map((r) => {
                  const st = STATUS_TR[r.status];
                  return (
                    <tr key={r.id}>
                      <td>{formatDate(r.scheduledAt)}</td>
                      <td>{r.region}</td>
                      <td><span className={`badge ${st.cls}`}>{st.label}</span></td>
                      <td className="faint" style={{ fontSize: 12 }}>
                        {r.resultVideoId ? `🎬 video #${r.resultVideoId}` : r.errorMessage ? `⚠️ ${r.errorMessage}` : "—"}
                      </td>
                      <td>
                        {r.status === "Pending" && (
                          <button className="btn btn-sm btn-danger" onClick={() => cancel(r.id)}>İptal</button>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </>
  );
}
