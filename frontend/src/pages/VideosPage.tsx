import { useState } from "react";
import { api } from "../api/client";
import type { VideoDto, SceneDto } from "../api/types";
import { usePolling } from "../hooks/usePolling";
import {
  formatDate,
  Pager,
  Spinner,
  useToast,
  VideoStatusBadge,
} from "../components/common";

const PAGE_SIZE = 12;

const HAS_FILE_STATUSES = ["Composed", "PendingApproval", "Publishing", "Published", "Rejected"];

// Statuses where scenes can still be edited (nothing composed yet, or a re-generate is possible).
const SCENES_EDITABLE = ["ScriptGenerated", "AssetsReady", "Failed"];

function EditableScene({
  videoId,
  scene,
  editable,
  onSaved,
}: {
  videoId: number;
  scene: SceneDto;
  editable: boolean;
  onSaved: (v: VideoDto) => void;
}) {
  const toast = useToast();
  const [narration, setNarration] = useState(scene.narrationText);
  const [prompt, setPrompt] = useState(scene.videoPrompt);
  const [kwA, setKwA] = useState(scene.visualKeywordA);
  const [kwB, setKwB] = useState(scene.visualKeywordB);
  const [saving, setSaving] = useState(false);

  const dirty =
    narration !== scene.narrationText ||
    prompt !== scene.videoPrompt ||
    kwA !== scene.visualKeywordA ||
    kwB !== scene.visualKeywordB;

  async function save() {
    setSaving(true);
    try {
      const v = await api.updateScene(videoId, scene.id, {
        narrationText: narration,
        videoPrompt: prompt,
        visualKeywordA: kwA,
        visualKeywordB: kwB,
      });
      toast.push(`Sahne #${scene.orderIndex + 1} kaydedildi.`, "ok");
      onSaved(v);
    } catch (e) {
      toast.push(e instanceof Error ? e.message : "Kaydedilemedi", "err");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="scene-item">
      <div className="sc-head">
        <span>#{scene.orderIndex + 1} · {scene.sceneType || "sahne"}</span>
        <span>{scene.durationSeconds.toFixed(1)}sn</span>
      </div>

      {editable ? (
        <>
          <label className="faint" style={{ fontSize: 11, display: "block", marginBottom: 2 }}>Anlatım metni</label>
          <textarea value={narration} onChange={(e) => setNarration(e.target.value)} rows={2} style={{ width: "100%", marginBottom: 8 }} />

          <label className="faint" style={{ fontSize: 11, display: "block", marginBottom: 2 }}>🎬 AI Video Prompt (İngilizce — üretim bununla yapılır)</label>
          <textarea value={prompt} onChange={(e) => setPrompt(e.target.value)} rows={4} style={{ width: "100%", marginBottom: 8 }} />

          <label className="faint" style={{ fontSize: 11, display: "block", marginBottom: 2 }}>Görsel anahtar kelimeler (Pexels)</label>
          <div className="row" style={{ marginBottom: 8 }}>
            <input value={kwA} onChange={(e) => setKwA(e.target.value)} placeholder="A" style={{ flex: 1 }} />
            <input value={kwB} onChange={(e) => setKwB(e.target.value)} placeholder="B" style={{ flex: 1 }} />
          </div>

          <button className="btn btn-sm btn-primary" disabled={!dirty || saving} onClick={save}>
            {saving ? <Spinner /> : "💾"} Kaydet
          </button>
        </>
      ) : (
        <>
          {scene.narrationText && <div style={{ marginBottom: 4 }}>{scene.narrationText}</div>}
          {scene.videoPrompt && <div className="faint" style={{ fontSize: 12, marginBottom: 4 }}>🎬 {scene.videoPrompt}</div>}
          <div className="faint" style={{ fontSize: 12 }}>
            🎨 {scene.visualKeywordA}{scene.visualKeywordB ? ` / ${scene.visualKeywordB}` : ""}
          </div>
        </>
      )}
    </div>
  );
}

function VideoDetailModal({
  video,
  onClose,
  onVideoUpdated,
}: {
  video: VideoDto;
  onClose: () => void;
  onVideoUpdated: (v: VideoDto) => void;
}) {
  const scenesEditable = SCENES_EDITABLE.includes(video.status);
  const hasFile = Boolean(video.outputFilePath) && HAS_FILE_STATUSES.includes(video.status);
  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <strong>#{video.id} · {video.title || "(başlıksız)"}</strong>
          <button className="btn btn-sm" onClick={onClose}>✕</button>
        </div>
        <div className="modal-body">
          <div className="row" style={{ marginBottom: 14 }}>
            <VideoStatusBadge status={video.status} />
            <span className="badge badge-gray">{video.visualMode}</span>
            <span className="badge badge-gray">{video.templateType}</span>
            <span className="faint">{video.durationSeconds}sn</span>
          </div>

          {hasFile && (
            <video className="video-preview" controls src={api.videoFileUrl(video.id)} style={{ marginBottom: 16 }} />
          )}

          {video.script && (
            <div style={{ marginBottom: 16 }}>
              <div className="faint" style={{ fontSize: 12, marginBottom: 4 }}>KONU</div>
              <div>{video.script.topic}</div>
            </div>
          )}

          <div className="row-between" style={{ marginBottom: 8 }}>
            <span className="faint" style={{ fontSize: 12 }}>SAHNELER ({video.scenes.length})</span>
            {scenesEditable && (
              <span className="faint" style={{ fontSize: 11 }}>✏️ Prompt'u düzenleyip kaydet, sonra "Üret"</span>
            )}
          </div>
          <div className="scene-list">
            {video.scenes.map((s) => (
              <EditableScene
                key={s.id}
                videoId={video.id}
                scene={s}
                editable={scenesEditable}
                onSaved={onVideoUpdated}
              />
            ))}
          </div>

          {video.publications.length > 0 && (
            <>
              <div className="faint" style={{ fontSize: 12, margin: "16px 0 8px" }}>YAYINLAR</div>
              {video.publications.map((p) => (
                <div key={p.id} className="row" style={{ marginBottom: 6 }}>
                  <span className="badge badge-gray">{p.platform}</span>
                  {p.isPublished ? (
                    <a href={p.platformUrl ?? "#"} target="_blank" rel="noreferrer" className="badge badge-green">
                      Yayınlandı ↗
                    </a>
                  ) : (
                    <span className="badge badge-red" title={p.errorMessage ?? ""}>Başarısız</span>
                  )}
                </div>
              ))}
            </>
          )}
        </div>
      </div>
    </div>
  );
}

function VideoCard({
  video,
  onOpen,
  onChanged,
}: {
  video: VideoDto;
  onOpen: () => void;
  onChanged: () => void;
}) {
  const toast = useToast();
  const [busy, setBusy] = useState<string | null>(null);
  const [visualMode, setVisualMode] = useState(video.visualMode || "AIGenerated");
  const hasFile = Boolean(video.outputFilePath) && HAS_FILE_STATUSES.includes(video.status);

  async function act(label: string, fn: () => Promise<unknown>, okMsg: string) {
    setBusy(label);
    try {
      await fn();
      toast.push(okMsg, "ok");
      onChanged();
    } catch (e) {
      toast.push(e instanceof Error ? e.message : "Hata", "err");
    } finally {
      setBusy(null);
    }
  }

  const canGenerate = ["ScriptGenerated", "AssetsReady", "Failed"].includes(video.status);
  const canPublish = video.status === "Composed" || video.status === "PendingApproval";

  return (
    <div className="card" style={{ marginBottom: 0 }}>
      <div
        onClick={onOpen}
        style={{ cursor: "pointer", marginBottom: 12 }}
        title="Detayları gör"
      >
        {hasFile ? (
          <video
            className="video-preview"
            muted
            preload="metadata"
            src={`${api.videoFileUrl(video.id)}#t=0.5`}
          />
        ) : (
          <div
            className="video-preview"
            style={{ height: 160, display: "flex", alignItems: "center", justifyContent: "center", color: "var(--text-faint)" }}
          >
            🎬 önizleme yok
          </div>
        )}
      </div>

      <div className="row-between" style={{ marginBottom: 8 }}>
        <strong className="clamp2" style={{ fontSize: 14 }}>#{video.id} {video.title || "(başlıksız)"}</strong>
        <VideoStatusBadge status={video.status} />
      </div>

      <div className="row" style={{ marginBottom: 12, fontSize: 12 }}>
        <span className="badge badge-gray">{video.visualMode}</span>
        <span className="faint">{video.durationSeconds}sn · {video.scenes.length} sahne</span>
        <span className="faint" style={{ marginLeft: "auto" }}>{formatDate(video.createdAt)}</span>
      </div>

      <div className="row">
        {canGenerate && (
          <>
            <select value={visualMode} onChange={(e) => setVisualMode(e.target.value)} disabled={busy !== null}>
              <option value="AIGenerated">AI Üretimi</option>
              <option value="StockVideo">Stok Video (Pexels)</option>
              <option value="Mixed">Karışık</option>
            </select>
            <button
              className="btn btn-sm btn-primary"
              disabled={busy !== null}
              onClick={() => act("gen", () => api.generateVideo(video.id, visualMode), "Üretim tamamlandı.")}
            >
              {busy === "gen" ? <Spinner /> : "⚙"} Üret
            </button>
          </>
        )}
        {canPublish && (
          <button
            className="btn btn-sm btn-primary"
            disabled={busy !== null}
            onClick={() => act("pub", () => api.publishVideo(video.id, "YouTube"), "YouTube'a yükleme başlatıldı.")}
          >
            {busy === "pub" ? <Spinner /> : "▲"} YouTube'a Yükle
          </button>
        )}
        <button
          className="btn btn-sm btn-danger"
          style={{ marginLeft: "auto" }}
          disabled={busy !== null}
          onClick={() => {
            if (confirm(`Video #${video.id} silinsin mi?`))
              act("del", () => api.deleteVideo(video.id), "Video silindi.");
          }}
        >
          🗑
        </button>
      </div>
    </div>
  );
}

export default function VideosPage() {
  const [selected, setSelected] = useState<VideoDto | null>(null);
  const [page, setPage] = useState(1);
  const { data, error, loading, refresh } = usePolling<VideoDto[]>(
    () => api.getVideos(page, PAGE_SIZE),
    null,
    [page]
  );
  const videos = data ?? [];

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Videolar</h1>
          <p>Üretilen videolar — önizle, üret, yayınla.</p>
        </div>
        <button className="btn" disabled={loading} onClick={refresh}>↻ Yenile</button>
      </div>

      {error && <div className="err-banner">{error}</div>}

      {loading && videos.length === 0 ? (
        <div className="card empty"><Spinner /> Yükleniyor…</div>
      ) : videos.length === 0 ? (
        <div className="card empty">
          Henüz video yok. "Trendler" sekmesinden bir trendi <strong>Analiz Et</strong>.
        </div>
      ) : (
        <div className="grid grid-2">
          {videos.map((v) => (
            <VideoCard
              key={v.id}
              video={v}
              onOpen={() => setSelected(v)}
              onChanged={refresh}
            />
          ))}
        </div>
      )}

      <Pager page={page} pageSize={PAGE_SIZE} count={videos.length} onPage={setPage} loading={loading} />

      {selected && (
        <VideoDetailModal
          video={selected}
          onClose={() => setSelected(null)}
          onVideoUpdated={(v) => {
            setSelected(v);
            refresh();
          }}
        />
      )}
    </>
  );
}
