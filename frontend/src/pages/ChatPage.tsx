import { useEffect, useRef, useState } from "react";
import { api } from "../api/client";
import type { VideoDto } from "../api/types";
import { Spinner, useToast, VideoStatusBadge } from "../components/common";

interface Msg {
  role: "user" | "assistant";
  content: string;
}

const GREETING: Msg = {
  role: "assistant",
  content:
    "Merhaba! 🎬 Nasıl bir kısa video yapmak istersin? Konuyu, tonu ya da aklındaki fikri anlat — " +
    "birlikte geliştirelim. Hazır olduğunda sağdaki panelden fikri videoya dönüştürebilirsin.",
};

const HAS_FILE = ["Composed", "PendingApproval", "Publishing", "Published", "Rejected"];

export default function ChatPage() {
  const toast = useToast();
  const [messages, setMessages] = useState<Msg[]>([GREETING]);
  const [input, setInput] = useState("");
  const [sending, setSending] = useState(false);

  const [visualMode, setVisualMode] = useState("StockVideo");
  const [creating, setCreating] = useState(false);
  const [createdVideo, setCreatedVideo] = useState<VideoDto | null>(null);
  const [producing, setProducing] = useState(false);

  const scrollRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: "smooth" });
  }, [messages, sending]);

  const hasUserTurn = messages.some((m) => m.role === "user");

  async function send() {
    const text = input.trim();
    if (!text || sending) return;
    const next: Msg[] = [...messages, { role: "user", content: text }];
    setMessages(next);
    setInput("");
    setSending(true);
    try {
      const { reply } = await api.chat(next.map((m) => ({ role: m.role, content: m.content })));
      setMessages([...next, { role: "assistant", content: reply }]);
    } catch (e) {
      toast.push(e instanceof Error ? e.message : "Sohbet hatası", "err");
      setMessages(next); // keep the user's message; let them retry
    } finally {
      setSending(false);
    }
  }

  function onKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      send();
    }
  }

  async function convertToVideo() {
    // Feed Gemini the whole conversation as the idea — it distills scenes from the full context.
    const idea = messages
      .map((m) => `${m.role === "user" ? "Kullanıcı" : "Asistan"}: ${m.content}`)
      .join("\n");
    // Deliberately do NOT clear createdVideo here: if this (re)generation fails — most often a Gemini
    // rate-limit after a burst of chat turns — the previously generated prompt/scenes stay on screen
    // instead of vanishing, so the user doesn't lose their earlier result on a failed retry.
    setCreating(true);
    try {
      const video = await api.generateVideoFromChat(idea, visualMode);
      setCreatedVideo(video);
      toast.push(`Senaryo üretildi → video #${video.id} (${video.scenes.length} sahne).`, "ok");
    } catch (e) {
      toast.push(e instanceof Error ? e.message : "Video oluşturulamadı", "err");
    } finally {
      setCreating(false);
    }
  }

  async function produce() {
    if (!createdVideo) return;
    setProducing(true);
    try {
      const video = await api.generateVideo(createdVideo.id, visualMode);
      setCreatedVideo(video);
      toast.push("Video üretildi! ▶ Aşağıdan izleyebilirsin.", "ok");
    } catch (e) {
      toast.push(e instanceof Error ? e.message : "Üretim başarısız", "err");
    } finally {
      setProducing(false);
    }
  }

  const videoHasFile = createdVideo && HAS_FILE.includes(createdVideo.status);

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Sohbet → Video</h1>
          <p>Gemini ile konuşarak fikri geliştir, tek tıkla videoya dönüştür.</p>
        </div>
      </div>

      <div className="chat-layout">
        {/* ── Chat column ── */}
        <div className="card chat-col">
          <div className="chat-scroll" ref={scrollRef}>
            {messages.map((m, i) => (
              <div key={i} className={`bubble-row ${m.role}`}>
                <div className={`bubble ${m.role}`}>{m.content}</div>
              </div>
            ))}
            {sending && (
              <div className="bubble-row assistant">
                <div className="bubble assistant"><Spinner /> yazıyor…</div>
              </div>
            )}
          </div>
          <div className="chat-input">
            <textarea
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={onKeyDown}
              placeholder="Fikrini yaz… (Enter gönderir, Shift+Enter yeni satır)"
              rows={2}
              disabled={sending}
            />
            <button className="btn btn-primary" onClick={send} disabled={sending || !input.trim()}>
              {sending ? <Spinner /> : "Gönder"}
            </button>
          </div>
        </div>

        {/* ── Produce column ── */}
        <div className="card produce-col">
          <h2>🎬 Videoya Dönüştür</h2>
          <p className="faint" style={{ fontSize: 12, marginTop: 0 }}>
            Sohbetteki fikri sahnelere bölünmüş bir videoya çevirir.
          </p>

          <label className="faint" style={{ fontSize: 12, display: "block", margin: "12px 0 4px" }}>
            Görsel mod
          </label>
          <select
            value={visualMode}
            onChange={(e) => setVisualMode(e.target.value)}
            style={{ width: "100%" }}
            disabled={creating || producing}
          >
            <option value="StockVideo">Stok Video (Pexels) — çalışıyor</option>
            <option value="AIGenerated">AI Üretimi — sağlayıcı gerekli</option>
            <option value="Mixed">Karışık</option>
          </select>

          <button
            className="btn btn-primary"
            style={{ width: "100%", marginTop: 12, justifyContent: "center" }}
            onClick={convertToVideo}
            disabled={!hasUserTurn || creating || producing}
          >
            {creating ? <Spinner /> : createdVideo ? "🔄" : "✨"}{" "}
            {createdVideo ? "Yeni Senaryo Üret" : "Fikirden Senaryo Üret"}
          </button>
          {!hasUserTurn ? (
            <p className="faint" style={{ fontSize: 11, marginBottom: 0 }}>
              Önce en az bir mesaj gönder.
            </p>
          ) : createdVideo ? (
            <p className="faint" style={{ fontSize: 11, marginBottom: 0 }}>
              Beğenmediysen sohbete devam edip yeniden üretebilirsin — mevcut senaryo korunur.
            </p>
          ) : null}

          {createdVideo && (
            <div className="created-box">
              <div className="row-between" style={{ marginBottom: 8 }}>
                <strong>Video #{createdVideo.id}</strong>
                <VideoStatusBadge status={createdVideo.status} />
              </div>
              <div className="faint" style={{ fontSize: 12, marginBottom: 8 }}>
                {createdVideo.scenes.length} sahne · {createdVideo.durationSeconds}sn · {createdVideo.visualMode}
              </div>

              <div className="scene-list" style={{ maxHeight: 180, overflowY: "auto", marginBottom: 10 }}>
                {createdVideo.scenes.map((s) => (
                  <div key={s.id} className="scene-item" style={{ padding: 8 }}>
                    <div className="sc-head">
                      <span>#{s.orderIndex + 1} · {s.sceneType}</span>
                      <span>{s.durationSeconds.toFixed(0)}sn</span>
                    </div>
                    <div style={{ fontSize: 12 }}>{s.narrationText}</div>
                  </div>
                ))}
              </div>

              {!videoHasFile && (
                <button
                  className="btn btn-primary"
                  style={{ width: "100%", justifyContent: "center" }}
                  onClick={produce}
                  disabled={producing}
                >
                  {producing ? <><Spinner /> Üretiliyor… (biraz sürebilir)</> : "⚙ Videoyu Üret"}
                </button>
              )}

              {videoHasFile && (
                <video className="video-preview" controls src={api.videoFileUrl(createdVideo.id)} />
              )}
            </div>
          )}
        </div>
      </div>
    </>
  );
}
