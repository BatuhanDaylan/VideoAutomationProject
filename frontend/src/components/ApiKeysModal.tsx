import { useEffect, useState } from "react";
import { api, type KeyField, type KeyStatusMap } from "../api/client";
import { Spinner, useToast } from "./common";

// Single-value fields (Gemini is handled separately as a multi-key list below).
const FIELDS: { key: Exclude<KeyField, "gemini">; label: string; placeholder?: string }[] = [
  { key: "youtubeApiKey", label: "YouTube Data API Anahtarı", placeholder: "AIza..." },
  { key: "youtubeClientId", label: "YouTube OAuth Client ID", placeholder: "...apps.googleusercontent.com" },
  { key: "youtubeClientSecret", label: "YouTube OAuth Client Secret", placeholder: "GOCSPX-..." },
  { key: "apify", label: "Apify Token", placeholder: "apify_api_..." },
  { key: "pexels", label: "Pexels API Anahtarı" },
  { key: "aiVideo", label: "AI Video Token (Veo/Kling)" },
  { key: "telegramBotToken", label: "Telegram Bot Token", placeholder: "1234:ABC..." },
  { key: "telegramChatId", label: "Telegram Chat ID", placeholder: "123456789" },
];

export default function ApiKeysModal({ onClose }: { onClose: () => void }) {
  const toast = useToast();
  const [status, setStatus] = useState<KeyStatusMap | null>(null);
  const [values, setValues] = useState<Partial<Record<Exclude<KeyField, "gemini">, string>>>({});
  const [geminiKeys, setGeminiKeys] = useState<string[]>([""]);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api.getKeys().then(setStatus).catch(() => setStatus(null));
  }, []);

  function setGeminiAt(i: number, v: string) {
    setGeminiKeys((prev) => prev.map((k, idx) => (idx === i ? v : k)));
  }
  function addGemini() {
    setGeminiKeys((prev) => [...prev, ""]);
  }
  function removeGemini(i: number) {
    setGeminiKeys((prev) => (prev.length === 1 ? [""] : prev.filter((_, idx) => idx !== i)));
  }

  async function save() {
    const changed: Partial<Record<KeyField, string>> = {};
    for (const f of FIELDS) {
      const v = values[f.key];
      if (v !== undefined && v.trim() !== "") changed[f.key] = v.trim();
    }
    // Gemini: join all filled rows into one comma-separated blob (only if the user entered at least one).
    const gemini = geminiKeys.map((k) => k.trim()).filter(Boolean);
    if (gemini.length > 0) changed.gemini = gemini.join(",");

    if (Object.keys(changed).length === 0) {
      toast.push("Değişiklik yok — yeni bir değer gir.", "err");
      return;
    }
    setBusy(true);
    try {
      const next = await api.saveKeys(changed);
      setStatus(next);
      setValues({});
      setGeminiKeys([""]);
      toast.push("API anahtarları kaydedildi ✅", "ok");
    } catch (e) {
      toast.push(e instanceof Error ? e.message : "Kaydedilemedi", "err");
    } finally {
      setBusy(false);
    }
  }

  const noneSet = status && Object.values(status).every((s) => !s.set);
  const geminiStatus = status?.gemini;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" style={{ maxWidth: 520 }} onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <strong>🔑 API Anahtarları</strong>
          <button className="btn btn-sm" onClick={onClose}>✕</button>
        </div>
        <div className="modal-body">
          <p className="faint" style={{ marginTop: 0, fontSize: 13 }}>
            Anahtarlar şifreli saklanır ve yalnızca senin işlemlerinde kullanılır. Bir kez gir; sonraki
            girişlerde tekrar girmen gerekmez. Değiştirmek istediğin alana yeni değeri yaz, boş bırakılan
            alanlar değişmez.
          </p>
          {noneSet && (
            <div className="err-banner" style={{ background: "rgba(124,140,255,0.1)", color: "var(--text)" }}>
              👋 İlk kez giriş yaptın — çalışması için en azından Gemini + Pexels anahtarlarını gir.
            </div>
          )}

          <form className="auth-form" onSubmit={(e) => { e.preventDefault(); save(); }}>
            {/* Gemini — multiple keys (biri kotaya takılınca sistem diğerine geçer) */}
            <div className="key-group">
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", fontSize: 12, color: "var(--text-dim)" }}>
                <span>Gemini API Anahtar(lar)ı</span>
                <span className={`badge ${geminiStatus?.set ? "badge-green" : "badge-gray"}`} style={{ fontSize: 10 }}>
                  {geminiStatus?.set ? `✓ ${geminiStatus.count ?? 1} anahtar kayıtlı` : "girilmedi"}
                </span>
              </div>
              {geminiKeys.map((k, i) => (
                <div key={i} style={{ display: "flex", gap: 6, marginTop: 6 }}>
                  <input
                    type="password"
                    autoComplete="off"
                    placeholder={geminiStatus?.set ? "Değiştirmek için tüm anahtarları yeniden gir" : `Gemini anahtarı ${i + 1}`}
                    value={k}
                    onChange={(e) => setGeminiAt(i, e.target.value)}
                    style={{ flex: 1 }}
                  />
                  <button type="button" className="btn btn-sm" title="Bu satırı sil" onClick={() => removeGemini(i)}>✕</button>
                </div>
              ))}
              <button type="button" className="btn btn-sm" onClick={addGemini} style={{ marginTop: 6, alignSelf: "flex-start" }}>
                ＋ Anahtar ekle
              </button>
              {geminiStatus?.set && (
                <div className="faint" style={{ fontSize: 11, marginTop: 4 }}>
                  Not: Gemini'yi güncellemek için TÜM anahtarları yeniden girmen gerekir (kayıtlılar güvenlik için gösterilmez).
                </div>
              )}
            </div>

            {FIELDS.map((f) => {
              const st = status?.[f.key];
              return (
                <label key={f.key}>
                  <span style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                    {f.label}
                    <span className={`badge ${st?.set ? "badge-green" : "badge-gray"}`} style={{ fontSize: 10 }}>
                      {st?.set ? `✓ ${st.hint ?? "kayıtlı"}` : "girilmedi"}
                    </span>
                  </span>
                  <input
                    type="password"
                    autoComplete="off"
                    placeholder={st?.set ? "Değiştirmek için yeni değer gir" : (f.placeholder ?? "Değer gir")}
                    value={values[f.key] ?? ""}
                    onChange={(e) => setValues((v) => ({ ...v, [f.key]: e.target.value }))}
                  />
                </label>
              );
            })}
            <button className="btn btn-primary" type="submit" disabled={busy} style={{ marginTop: 6 }}>
              {busy ? <Spinner /> : "💾"} Kaydet
            </button>
          </form>
        </div>
      </div>
    </div>
  );
}
