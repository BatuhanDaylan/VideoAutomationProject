import { useState } from "react";
import { useAuth } from "../auth/AuthContext";
import { Spinner } from "../components/common";

export default function LoginPage() {
  const { login, register } = useAuth();
  const [mode, setMode] = useState<"login" | "register">("login");
  const [emailOrUsername, setEmailOrUsername] = useState("");
  const [email, setEmail] = useState("");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      if (mode === "login") await login(emailOrUsername.trim(), password);
      else await register(email.trim(), username.trim(), password);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Bir hata oluştu");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="auth-wrap">
      <div className="auth-card card">
        <div className="brand" style={{ justifyContent: "center", marginBottom: 6 }}>🎥 Video Automation</div>
        <p className="faint" style={{ textAlign: "center", marginTop: 0, fontSize: 13 }}>
          {mode === "login" ? "Hesabına giriş yap" : "Yeni hesap oluştur"}
        </p>

        {error && <div className="err-banner">{error}</div>}

        <form onSubmit={submit} className="auth-form">
          {mode === "register" && (
            <>
              <label>
                E-posta
                <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoComplete="email" />
              </label>
              <label>
                Kullanıcı adı
                <input value={username} onChange={(e) => setUsername(e.target.value)} required minLength={2} autoComplete="username" />
              </label>
            </>
          )}
          {mode === "login" && (
            <label>
              E-posta veya kullanıcı adı
              <input value={emailOrUsername} onChange={(e) => setEmailOrUsername(e.target.value)} required autoComplete="username" />
            </label>
          )}
          <label>
            Parola
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              minLength={6}
              autoComplete={mode === "login" ? "current-password" : "new-password"}
            />
          </label>

          <button className="btn btn-primary" type="submit" disabled={busy} style={{ marginTop: 6 }}>
            {busy ? <Spinner /> : mode === "login" ? "Giriş yap" : "Kayıt ol"}
          </button>
        </form>

        <div className="auth-switch faint">
          {mode === "login" ? (
            <>Hesabın yok mu? <button className="linkbtn" onClick={() => { setMode("register"); setError(null); }}>Kayıt ol</button></>
          ) : (
            <>Zaten hesabın var mı? <button className="linkbtn" onClick={() => { setMode("login"); setError(null); }}>Giriş yap</button></>
          )}
        </div>
      </div>
    </div>
  );
}
