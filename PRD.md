## UltraVoice MVP - Product Requirements Document

### 1. Executive Summary
- **Product**: UltraVoice MVP — ultra-hafif, 3 odalı, düşük gecikmeli Windows sesli sohbet ürünü.
- **Target launch**: Ekim 2025 (hedef: Windows 11 ve Windows 10 22H2 ESU müşterileri için GA).
- **Objective**: Minimum kaynak tüketimiyle sürekli ses iletişimi sağlayan basit istemci+sunucu çözümü.
- **Primary differentiators**: <150 ms gecikme, tek dosya dağıtım, oda başına sınırlı kullanıcı kapasitesiyle yüksek stabilite.

### 2. Goals & Non-Goals
- **Goals**
  - Ortalama <150 ms uçtan uca gecikme.
  - 2 aktif konuşmacı ile <=%2 CPU ve <=60 MB RAM.
  - Kullanıcı adı, oda, cihaz, ses ayarlarının kalıcı tutulması.
  - 3 sabit oda (room-a/b/c) arasında hızlı geçiş.
  - 10 dakikalık görüşmelerde kesinti/crash olmadan stabil çalışma.
- **Non-Goals**
  - Metin sohbeti veya ekran/video paylaşımı.
  - Uçtan uca şifreleme veya gelişmiş yetkilendirme.
  - Kaydetme/geri oynatma.

### 3. Personas & Use Cases
- **Primary persona**: Düşük donanımlı Windows 11/10 (22H2) kullanıcıları; basit arayüz ve düşük kaynak tüketimi arayan topluluklar.
- **Core use cases**
  - İlk kurulumsuz kullanım: Tek `.exe` indir → kullanıcı adını gir → odalardan birine katıl.
  - Oda içi sohbet: Maksimum 2 aktif konuşmacı decode edilerek dengeli ses deneyimi.
  - Per-user ayarlar: Volume/mute kalıcılığı; cihaz değiştirirken kesintisiz yayın.

### 4. Timeline & Milestones (2025)
1. **Şubat–Mart**: Sunucu iskeleti (JOIN/LEAVE/forward/state), temel CI.
2. **Nisan–Mayıs**: İstemci ses yolu (capture→Opus encode→LiteNetLib send / receive→decode→mix).
3. **Haziran**: UI/kalıcılık (Avalonia), konfigürasyon formatı, basit RTT telemetrisi.
4. **Temmuz**: Beta 1 — iç ekip testi; paket kaybı ve cihaz değiştirme senaryoları ölçümü.
5. **Ağustos**: Aktif konuşmacı seçici (RMS tabanlı top-2), agresif optimizasyonlar.
6. **Eylül**: Beta 2 — 6 istemcili saha testi, telemetri incelemesi.
7. **Ekim (GA)**: Yayın, Windows 10 EOL (14 Ekim 2025) öncesinde ESU planı doğrulama, dağıtım otomasyonu.

### 5. Scope
- **In scope**
  - Windows 11 23H2+ ve Windows 10 22H2 (ESU planı olan kurumsal müşteriler) için tek dosya istemci.
  - Merkezi SFU sunucu (tek VPS; 1 vCPU/512 MB).
  - UDP tabanlı iletişim (LiteNetLib) ve Opus VOIP codec.
  - Per-user volume/mute, giriş/çıkış cihaz seçimi, top-2 aktif konuşmacı stratejisi.
  - basit RTT telemetrisi (CPU, RAM, RTT, jitter, kayıp oranı).
- **Out of scope**
  - NAT traversal için TURN/ICE.
  - Mobil ve macOS istemcileri.
  - Sunucu yatay ölçekleme veya cluster yönetimi.

### 6. Functional Requirements
- **Oda Yönetimi**
  - room-a/b/c sabit odaları listele.
  - Kullanıcılar tek oda ile sınırlandırılır; geçiş <1 sn.
- **Kimlik & Kalıcılık**
  - İlk açılışta kullanıcı adı zorunlu; `%AppData%/UltraVoice/config.json` içinde saklanır.
  - Hesap açma / giriş akışı yok; tüm kimlik bilgileri yerel cihazda tutulur.
  - `inputGainDb`, `perUserVolumeDb`, `lastRoom`, `inputDeviceId`, `outputDeviceId`, `server.host/port/token` alanları belirtilen tiplerde saklanır.
- **Oturum Yönetimi**
  - Makine başına tek aktif istemci süreci; ikinci kopya açılmaya çalışıldığında mevcut oturum odaklanır veya kullanıcı bilgilendirilir.
- **Ses İşleme**
  - Capture: 16 kHz, mono, 20 ms frame.
  - Encode: Opus VOIP, 24 kbps VBR, complexity=2, DTX açık, FEC kapalı.
  - İstemci yalnızca en yüksek 2 RMS konuşmacıyı decode eder.
- **UI**
  - Sol panel: Odalar + kullanıcı sayısı + ping göstergesi.
  - Sağ panel: Kullanıcı listesi (isim, seviye göstergesi, volume slider, mute toggle).
  - Üst çubuk: Mikrofon/çıkış cihazı seçici, input gain, bağlantı durumu göstergesi.
  - Animasyonsuz, event-driven redraw, tek pencere.
- **Telemetri**
  - İstemci: `avgCpu`, `ramMb`, `rttMs`, `jitterMs`, `pktLossPercent`, `decodeErrors`.
  - Sunucu: `roomUserCount`, `forwardHz`, `avgRtt`, `errors`.

### 7. System Architecture
- **Model**: Merkezi SFU (Selective Forwarding Unit). İstemciler tek uplink gönderir, sunucu akışları yönlendirir.
- **Sunucu**
  - .NET 9.0 (STS) Console uygulaması; C# 13 dil özellikleri.
  - LiteNetLib 1.1.x ile UDP transport; tek port 40000/UDP.
  - MessagePack for C# 2.5.x ile mesaj serileştirme; LZ4 opsiyonel.
  - Concurrency: Oda bazlı forward döngüsü; miks yok.
- **İstemci**
  - .NET 9.0 (publish self-contained, `win-x64`).
  - Avalonia UI 11.1.x (win32 backend).
  - miniaudio.NET 0.11.x (ya da fallback: NAudio 2.2) için capture/playback.
  - Opus codec: `libopus` 1.5.x native x64 (preferred) veya Concentus 1.4.x (managed fallback).
  - MessagePack + LiteNetLib protokol implementasyonu.
- **Wire Protocol**
  - Üst bilgi: `MsgType` (`u8`) + `sessionId` (`u32`) + `seq` (`u16`) + `timestamp` (`u32`).
  - Mesaj tipleri: `HELLO`, `WELCOME`, `STATE`, `AUDIO_FRAME`, `PING/PONG`, `USER_EVENT`.
  - Oda durumu paketleri MessagePack ile; ses paketleri ham Opus payload + header.

### 8. Performance & Reliability Targets
- Ortalama uçtan uca gecikme ≤150 ms; jitter buffer 50 ms.
- 2 aktif konuşmacıda CPU ≤%2 (Intel i5-8250U referans), RAM ≤60 MB.
- Odaya katılım süresi ≤1000 ms.
- Paket kaybı toleransı: %3.
- 10 dakikalık kesintisiz görüşmede crash/freez olmamalı.

### 9. Security & Compliance
- Session ID: Rastgele `u32`, çakışma engelleme.
- Opsiyonel shared token ile basit bağlantı yetkilendirmesi.
- DoS azaltımı: IP rate limit (audio 50 pkt/s, control 10 msg/s), kısa timeout (keepalive 5 sn, timeout 15 sn).
- Düşük hassasiyetli veri; PII saklanmaz. Config dosyası lokal kullanıcı profiline yazılır.
- Windows 10 desteği için 22H2 + ESU lisansı gerektiği belirtilmelidir (EOL 14 Ekim 2025).

### 10. Deployment & Operations
- **Sunucu**
  - Ortam: Tek Linux VPS, 1 vCPU / 512 MB RAM, UDP 40000 açık.
  - Loglar: `/var/log/ultravoice-sfu.log`, günlük rotasyonu.
  - CI: GitHub Actions (Windows + Linux matrix) — Opus DLL mevcutluğunu doğrula.
- **İstemci paketleme**
  - `dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=true`.
  - `trimMode=full`, `ReadyToRun`, `InvariantGlobalization`.
  - Native bağımlılıklar: `opus.dll` (x64), miniaudio native bindingleri.

### 11. Test Plan
- **Load**: 6 istemci, 2 aktif konuşmacı; CPU/RAM ölç.
- **Loss**: 0/3/5% paket kaybı senaryoları.
- **Device switch**: Mikrofon/çıktı cihazı değişiminde kesintisiz akış.
- **Room switch**: <1 sn.
- **Persistence**: Uygulama yeniden açıldığında kullanıcı/volume ayarlarının geri yüklenmesi.

### 12. Risks & Mitigations
- **NAT/UDP engeli**: UDP-only MVP. Mitigasyon: Belirlenen hedef kullanıcılar için kontrollü dağıtım, gelecekte TCP fallback planı.
- **Opus native paketleme**: CI’da x64 doğrulamaları; runtime’da DLL bulunamazsa Concentus fallback.
- **Eşzamanlı konuşmacı fazlalığı**: Top-2 decode politikası + UI uyarısı.
- **Windows 10 EOL**: Ekim 2025 GA öncesi desteklenen platformları duyur; kurum içi ESU gereksinimini bilgilendir.
- **.NET 9 STS**: Mayıs 2026 STS bitişi; 2026 Q1’de .NET 10/11 değerlendirme planı.

### 13. Open Questions
- Windows 10 için ESU lisans desteğine devam edilecek mi, yoksa GA sonrası yalnızca Windows 11 mi hedeflenecek?
- Kullanıcı başına max eşzamanlı bağlantı sayısı? (Şu an 1 varsayılıyor.)
- Sunucu monitoring entegrasyonu (Prometheus/Influx) gerekli mi, yoksa sade loglar yeterli mi?
