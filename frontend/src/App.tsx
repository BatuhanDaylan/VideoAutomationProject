import { useEffect, useState } from "react";
import { ToastProvider } from "./components/common";
import { AuthProvider, useAuth } from "./auth/AuthContext";
import { api } from "./api/client";
import ApiKeysModal from "./components/ApiKeysModal";
import LoginPage from "./pages/LoginPage";
import DashboardPage from "./pages/DashboardPage";
import TrendsPage from "./pages/TrendsPage";
import VideosPage from "./pages/VideosPage";
import ChatPage from "./pages/ChatPage";
import PerformancePage from "./pages/PerformancePage";
import SchedulePage from "./pages/SchedulePage";

type Tab = "dashboard" | "chat" | "trends" | "videos" | "performance" | "schedule";

const TABS: { id: Tab; label: string; icon: string }[] = [
  { id: "dashboard", label: "Panel", icon: "📊" },
  { id: "chat", label: "Sohbet → Video", icon: "💬" },
  { id: "trends", label: "Trendler", icon: "🔥" },
  { id: "videos", label: "Videolar", icon: "🎬" },
  { id: "performance", label: "Performans", icon: "📈" },
  { id: "schedule", label: "Takvim", icon: "📅" },
];

function Shell() {
  const { user, loading, logout } = useAuth();
  const [tab, setTab] = useState<Tab>("dashboard");
  const [keysOpen, setKeysOpen] = useState(false);

  // On first login with no keys configured yet, prompt for them automatically ("sadece ilk girişte").
  useEffect(() => {
    if (!user) return;
    api
      .getKeys()
      .then((s) => {
        if (Object.values(s).every((k) => !k.set)) setKeysOpen(true);
      })
      .catch(() => {});
  }, [user]);

  if (loading) {
    return <div className="auth-wrap"><div className="faint">Yükleniyor…</div></div>;
  }
  if (!user) return <LoginPage />;

  return (
    <div className="app">
      <aside className="sidebar">
        <div className="brand">🎥 Video Automation</div>
        {TABS.map((t) => (
          <button
            key={t.id}
            className={`nav-item ${tab === t.id ? "active" : ""}`}
            onClick={() => setTab(t.id)}
          >
            <span>{t.icon}</span>
            {t.label}
          </button>
        ))}
        <div className="nav-spacer" />
        <a className="nav-item" href="/hangfire" target="_blank" rel="noreferrer">
          <span>⏱️</span> Hangfire
        </a>

        {/* User info at the bottom of the layout (API keys section added in Faz 2) */}
        <div className="user-box">
          <div className="user-ident">
            <div className="user-avatar">{user.username.slice(0, 1).toUpperCase()}</div>
            <div className="user-meta">
              <strong>{user.username}</strong>
              <span className="faint">{user.email}</span>
            </div>
          </div>
          <button className="btn btn-ghost" onClick={() => setKeysOpen(true)} style={{ width: "100%", marginTop: 8 }}>
            🔑 API Anahtarları
          </button>
          <button className="btn btn-ghost" onClick={logout} style={{ width: "100%", marginTop: 6 }}>
            🚪 Çıkış yap
          </button>
        </div>
      </aside>

      <main className="main">
        {tab === "dashboard" && <DashboardPage />}
        {tab === "chat" && <ChatPage />}
        {tab === "trends" && <TrendsPage />}
        {tab === "videos" && <VideosPage />}
        {tab === "performance" && <PerformancePage />}
        {tab === "schedule" && <SchedulePage />}
      </main>

      {keysOpen && <ApiKeysModal onClose={() => setKeysOpen(false)} />}
    </div>
  );
}

export default function App() {
  return (
    <ToastProvider>
      <AuthProvider>
        <Shell />
      </AuthProvider>
    </ToastProvider>
  );
}
