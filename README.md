# Video Automation

YouTube/TikTok/Instagram'daki viral kısa videoları (Shorts/Reels) keşfedip, içeriğini yapay zekayla
anlayarak Türkçe dikey kısa videolara dönüştüren, isteğe bağlı olarak Telegram onayıyla YouTube'a yükleyen
otomasyon sistemi.

## Nasıl çalışır (özet)

```
Tarama → Seçim (AI uygunluk) → Analiz (senaryo) → Üretim (FFmpeg video) → Onay (Telegram) → Yükleme (YouTube)
```

- **YouTube:** Gemini videoyu doğrudan URL'den İZLER (multimodal), gerçek içeriği + AI-uygunluk puanını çıkarır — otomatik pipeline yalnızca YouTube ile çalışır.
- **TikTok/Instagram:** Video izleme pahalı olduğu için manuel — kullanıcı videoyu izleyip anlatır, Gemini senaryoyu ondan üretir.
- Üretilen video Telegram'a onaya gönderilir; onaylanırsa YouTube'a (private) yüklenir.

## Gereksinimler

| Araç | Neden |
|---|---|
| **.NET 10 SDK** | Backend (5 katmanlı C# çözümü) |
| **Node.js 18+** | Frontend (React + Vite paneli) |
| **Python 3** | `edge-tts` (Türkçe seslendirme) ve `youtube-transcript-api` (YouTube transkript) için |
| **FFmpeg** | Video birleştirme — **otomatik indirilir** (Xabe.FFmpeg, ilk kullanımda `assets/ffmpeg/` altına), manuel kurulum gerekmez |

Python paketleri:
```bash
pip install edge-tts youtube-transcript-api
```

## Kurulum

1. **`.env` dosyasını doldur** (kök dizinde; `.env.example`'dan kopyalayabilirsin):

   ```
   GEMINI_API_KEY=...            # zorunlu (analiz/senaryo/uygunluk/sohbet)
   # GEMINI_API_KEY_2=...        # opsiyonel: birden fazla anahtar → biri kotayı doldurunca diğerine geçer
   YOUTUBE_API_KEY=...           # YouTube trend taraması
   APIFY_API_TOKEN=...           # TikTok/Instagram taraması
   PEXELS_API_KEY=...            # StockVideo (Pexels) modu
   TELEGRAM_BOT_TOKEN=...        # onay bildirimleri
   TELEGRAM_CHAT_ID=...          # senin Telegram kullanıcı ID'in
   YOUTUBE_CLIENT_ID=...         # YouTube'a yükleme (OAuth)
   YOUTUBE_CLIENT_SECRET=...
   ```

   Eksik anahtarlarda uygulama yine açılır — sadece ilgili özellik kapalı olur ve başlangıçta net bir uyarı loglanır. Yapısal olarak geçersiz config (bozuk cron, geçersiz enum, boş bağlantı dizesi) ise başlangıçta hata verip durdurur (fail-fast).

2. **Backend'i başlat:**
   ```bash
   cd src/VideoAutomation.API
   dotnet run --urls http://localhost:5079
   ```
   İlk açılışta SQLite veritabanı (`videoautomation.db`) otomatik oluşur.

3. **Paneli başlat:**
   ```bash
   cd frontend
   npm install
   npm run dev
   ```
   → **http://localhost:5173** (API'yi arka planda proxy'ler; tek açman gereken adres bu.)

## Panel (frontend)

| Sekme | Ne yapar |
|---|---|
| **Panel** | Servis durumu, Gemini anahtar havuzu, "Şimdi Çalıştır" ve pipeline çalıştırmalarının canlı ilerlemesi |
| **Sohbet → Video** | Gemini ile konuşarak fikir geliştir, tek tıkla videoya dönüştür |
| **Trendler** | Keşfedilen trendler; viralite + AI-uygunluk; YouTube "Analiz Et" (otomatik), TikTok/IG "Manuel" |
| **Videolar** | Üretilen videolar; önizleme, **sahne prompt'larını düzenleme**, üretim, YouTube'a yükleme |

Diğer yerel adresler: `:5079/swagger` (API), `:5079/hangfire` (zamanlanmış işler paneli).

## Mimari

**5 katmanlı C# çözümü** (`src/`):

- **Domain** — entity'ler (Trend, Video, Scene, Script…), enum'lar, value object'ler (ViralityScore)
- **Application** — arayüzler (IScoutAgent, IContentGenerator…), DTO'lar
- **Infrastructure** — asıl iş: Scraper'lar, AI (GeminiClient + anahtar havuzu), Media (FFmpeg, edge-tts), Publishing (Telegram, YouTube), Agent'lar, EF Core/SQLite
- **Shared** — Serilog, exception'lar
- **API** — controller'lar, DI, middleware, Hangfire

**Agent'lar:** Scout (keşif/skorlama) → Analyzer (senaryo) → Generator (FFmpeg video) → Publisher (yayın). `PipelineOrchestrator` bunları Hangfire ile zincirler (günlük cron + manuel tetikleme).

**Veritabanı:** SQLite, EF Core. Migration YOK — şema değişiklikleri manuel `ALTER TABLE` ile. Hangfire ayrı bir `hangfire.db` kullanır.

## Öne çıkan özellikler

- **Multimodal içerik anlama:** Gemini YouTube videosunu URL'den izleyip gerçek içeriği + AI-uygunluk puanını çıkarır (başlık tahmini değil).
- **Gemini anahtar havuzu:** Birden fazla anahtar; biri kota (429) veya red (403) alınca otomatik diğerine geçer.
- **İki ses modu:** AI-üretilen videolar modelin kendi ortam sesini kullanır; Pexels modunda edge-tts Türkçe seslendirme.
- **Düzenlenebilir prompt'lar:** Üretimden önce her sahnenin AI-video prompt'unu panelden düzenleyebilirsin.
- **Onay-kapılı yayın:** Video hazır olunca Telegram'a gider; sen onaylamadan YouTube'a yüklenmez.

## Testler

```bash
dotnet test          # kök dizinden (xUnit, deterministik birim testleri)
```

## Notlar

- Bilgi grafiği `graphify-out/` altında (kod değişince `graphify update .`).
- Bu bir kişisel/yerel projedir; Hangfire paneli ve API kimlik doğrulaması yoktur (yalnızca localhost).
