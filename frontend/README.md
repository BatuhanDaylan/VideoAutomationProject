# Video Automation — Dashboard (Faz 7)

React + Vite + TypeScript kontrol paneli. Mevcut ASP.NET API'nin üzerine oturur; kendi backend'i yoktur.

## Çalıştırma

1. **API'yi başlat** (ayrı terminal):
   ```
   cd ../src/VideoAutomation.API
   dotnet run --urls http://localhost:5079
   ```
2. **Paneli başlat**:
   ```
   npm install
   npm run dev
   ```
   → http://localhost:5173

Vite dev sunucusu `/api` ve `/hangfire` isteklerini `http://localhost:5079`'a proxy'ler
(`vite.config.ts`), bu yüzden tarayıcı her şeyi aynı-origin görür — CORS yok, porta gömülü
bağımlılık yok. API başka portta çalışıyorsa: `VITE_API_TARGET=http://localhost:5080 npm run dev`.

## Sayfalar

- **Panel** — servis durumu (hangi API anahtarları ayarlı), "Şimdi Çalıştır" (pipeline'ı Hangfire'a
  kuyruğa alır) ve tüm pipeline çalıştırmalarının canlı ilerlemesi (bir çalıştırma aktifken 2.5sn'de
  bir, boştayken 15sn'de bir yoklama).
- **Trendler** — keşfedilen trendler; viralite + AI-uygunluk puanı. Tara / Uygunluk Değerlendir /
  Analiz Et.
- **Videolar** — üretilen videolar; kart üzerinde önizleme (`GET /api/videos/{id}/file`, aralık/range
  destekli), Üret (görsel mod seçimiyle), YouTube'a Yükle, detay modalı (sahneler + tam önizleme).

## Üretim derlemesi

```
npm run build   # tsc -b && vite build → dist/
```
