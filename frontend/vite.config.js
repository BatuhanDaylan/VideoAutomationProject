import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
// The dashboard talks to the local ASP.NET API. Requests to /api and /hangfire are proxied
// through the Vite dev server so the browser sees a same-origin URL (no CORS preflight in dev,
// and no hardcoded port scattered through the app). Change the target if the API runs elsewhere.
const API_TARGET = process.env.VITE_API_TARGET || "http://localhost:5079";
export default defineConfig({
    plugins: [react()],
    server: {
        port: 5173,
        proxy: {
            "/api": { target: API_TARGET, changeOrigin: true },
            "/hangfire": { target: API_TARGET, changeOrigin: true },
        },
    },
});
