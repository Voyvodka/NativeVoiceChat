# UltraVoice MVP

UltraVoice, Windows masaüstü için ultra-hafif, üç odalı, düşük gecikmeli bir sesli sohbet deneyimi sağlamayı hedefleyen istemci–sunucu tabanlı bir projedir. Bu depo, PRD’de tanımlanan MVP için paylaşılan modelleri, LiteNetLib tabanlı SFU sunucusunu ve Avalonia ile yazılmış masaüstü istemciyi içerir.

## Klasör Yapısı

- `client/UltraVoice.Client`: Avalonia tabanlı Windows istemcisi (net9.0, win-x64 publish profili).
- `server/UltraVoice.Server`: Merkezi SFU sunucusu (LiteNetLib + MessagePack).
- `common/UltraVoice.Shared`: Paylaşılan mesaj tipleri, yapılandırma modelleri ve telemetri sınıfları.
- `PRD.md`: Ürün gereksinimlerinin detaylı dokümanı.
- `server/server.json`: Örnek sunucu yapılandırması.

## Kurulum ve Derleme

Önkoşullar:
- .NET SDK 9.0.304 (veya daha güncel bir 9.0 sürümü)
- İsteğe bağlı: Windows üzerinde NAudio için WASAPI/ASIO cihaz sürücüleri

Derlemek için:

```bash
dotnet build UltraVoice.sln
```

Sunucuyu çalıştırmak için:

```bash
dotnet run --project server/UltraVoice.Server -- server/server.json
```

İstemciyi geliştirme modunda çalıştırmak için:

```bash
dotnet run --project client/UltraVoice.Client
```

> Not: Avalonia istemcisi şu anda Windows hedefli bir RID (`win-x64`) ile yayınlanacak şekilde yapılandırılmıştır. Cross-platform geliştirme sırasında UI açılabilse de ses hattı Windows API’lerine bağlıdır.

## PRD Uyum Durumu (Özet)

- ✔ Sunucu iskeleti, oda yönetimi, MessagePack tabanlı kontrol paketi ve temel LiteNetLib loop’u hazır.
- ✔ İstemci tek-instance guard, konfigürasyon kalıcılığı ve publish ayarları PRD ile uyumlu.
- ✖ Opus tabanlı ses yakalama/encode/gönderme/çözme zinciri henüz uygulanmadı.
- ✖ İlk açılışta kullanıcı adı toplama, per-user volume/mute senkronizasyonu ve aktif konuşmacı seçimi eksik.
- ✖ Telemetri toplama (CPU, RAM, RTT, jitter, kayıp) ve rate limit / keepalive politikaları uygulanmadı.
- ✖ CI, paketleme doğrulamaları ve PRD’de tanımlı yük/kayıp/persist testleri henüz eklenmedi.

Detaylı gereksinimler için `PRD.md` dosyasına göz atın.

## Lisans

Bu proje [MIT Lisansı](LICENSE) altında lisanslanmıştır.
