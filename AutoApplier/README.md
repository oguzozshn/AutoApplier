# AutoApplier — LinkedIn İş Başvuru Yardımcısı

İş başvurusunda zamanın çoğu ilan aramaya ve aynı 15 alanı tekrar tekrar doldurmaya gidiyor.
Bu araç ikisini de üstleniyor:

1. **İlanları çeker** — LinkedIn'in giriş gerektirmeyen arama uç noktasından başlık, şirket,
   konum, tarih ve doğrudan ilan linkini toplar; CSV ve Markdown olarak kaydeder.
2. **Formu doldurur** — İlanı açar, başvurduğun pozisyona uyan profili seçer ve dış başvuru
   sitelerindeki (Workday, Greenhouse, Lever...) formu senin bilgilerinle doldurur.

**Gönder tuşuna hiçbir zaman kendisi basmaz.** Formu doldurup ekrana ne yaptığının raporunu
basar; son kontrolü ve göndermeyi sen yaparsın.

## Kurulum

```bash
dotnet build
```

Chrome'un kurulu olması yeterli. ChromeDriver bilerek sabit bir sürüme bağlanmadı —
Selenium Manager, kurulu Chrome sürümüne uyan sürücüyü ilk çalıştırmada kendisi indirip
önbelleğe alıyor. Böylece Chrome güncellendiğinde "sürüm uyuşmuyor" hatası almıyorsun
(ilk çalıştırmada internet gerekiyor).

## Kullanım

```bash
dotnet run
```

Menü:

| Seçenek | Ne yapar |
|---|---|
| 1 | İlanları çeker, yeni olanları listeler, CSV/Markdown'a yazar. Giriş gerektirmez. |
| 2 | Başvuru asistanı: ilanları tek tek açar, profili eşleştirir, formu doldurur. |
| 3 | Kayıtlı ilanları tekrar dışa aktarır. |
| 4 | Hangi ilana hangi profilin seçildiğini gösterir (anahtar kelimeleri ayarlamak için). |

### İlk çalıştırma

1 ve 2'yi ilk kez çalıştırdığında `config/` altında örnek ayar dosyaları oluşur.
2'yi kullanmadan önce `config/profiles.json` dosyasını **kendi bilgilerinle doldurman gerekiyor**.

## Ayar dosyaları

### `config/searches.json` — ne aranacak

Birden fazla arama tanımlayabilirsin; hepsi tek seferde çalışır ve sonuçlar birleştirilir.

```json
{
  "Searches": [
    {
      "Name": "Backend .NET",
      "Keywords": "backend developer .NET",
      "Location": "Türkiye",
      "MaxResults": 100,
      "PostedWithin": "week",
      "WorkplaceType": "any",
      "JobType": "any",
      "EasyApplyOnly": false,
      "SortByDate": true,
      "ExcludeTitleKeywords": ["senior", "staj"]
    }
  ],
  "DelayBetweenRequestsMs": 2500
}
```

- `PostedWithin`: `day` | `week` | `month` | `any`
- `WorkplaceType`: `onsite` | `remote` | `hybrid` | `any`
- `JobType`: `fulltime` | `parttime` | `contract` | `internship` | `any`
- `ExcludeTitleKeywords`: başlığında bunlardan biri geçen ilanlar elenir
- `DelayBetweenRequestsMs`: düşürme — LinkedIn hız sınırına (HTTP 429) takılırsın

### `config/profiles.json` — bilgiler pozisyona göre nasıl değişsin

Her başvuruda aynı kalan bilgiler `Personal` altında bir kez yazılır.
Pozisyona göre değişen her şey (**CV dosyası, unvan, deneyim, maaş beklentisi, ön yazı**)
ayrı profillerde tutulur:

```json
{
  "Personal": {
    "FirstName": "...", "LastName": "...", "Email": "...", "Phone": "...",
    "City": "İstanbul", "Country": "Türkiye",
    "LinkedInUrl": "...", "GitHubUrl": "...",
    "University": "...", "Degree": "Bachelor's Degree", "Major": "...",
    "WorkAuthorization": "Yes", "RequiresSponsorship": "No"
  },
  "DefaultProfile": "Backend .NET",
  "Profiles": [
    {
      "Name": "Backend .NET",
      "MatchKeywords": ["backend", ".net", "c#", "asp.net"],
      "ResumePath": "C:\\...\\CV-Backend.pdf",
      "CurrentTitle": "Backend Developer",
      "YearsOfExperience": "3",
      "ExpectedSalary": "80000",
      "CoverLetter": "Merhaba,\n\n{company} bünyesindeki {position} pozisyonuna...",
      "ExtraAnswers": { "why do you want to work": "..." }
    }
  ]
}
```

**Profil nasıl seçilir:** İlan başlığı ve şirket adı `MatchKeywords` ile karşılaştırılır,
en çok tutan profil kazanır. Hiçbiri tutmazsa `DefaultProfile` kullanılır — bu durum
ekranda `[eşleşme yok — varsayılan profil]` diye işaretlenir, çünkü o ilana alakasız bir
CV gitmesini istemezsin. Eşleşmeleri **menü 4** ile önceden test edip anahtar kelimeleri
ayarlayabilirsin.

**Ön yazı yer tutucuları:** `{position}`, `{company}`, `{name}` başvuru anında doldurulur.

**`ExtraAnswers`:** Serbest sorular için. Anahtar, form alanının etiketinde geçen bir ifade
olmalı (ör. `"how did you hear"`). Buradaki cevaplar diğer tüm kuralları ezer.

## Başvuru asistanı akışı

İlan açıldıktan sonra konsol komutları:

| Komut | Ne yapar |
|---|---|
| `d` | O anda ekranda olan formu doldurur. Çok adımlı formlarda **her adımda tekrar bas**. |
| `t` | İlanı "başvuruldu" olarak işaretler ve sonrakine geçer |
| `n` | Sonraki ilana geçer |
| `q` | Asistandan çıkar |

`d` komutundan sonra ekrana üç liste basılır: doldurulanlar, **zorunlu olup boş kalanlar**
(elle doldurman gerekenler) ve atlananlar.

Notlar:
- Başvuru butonu yeni sekme açarsa asistan otomatik olarak o sekmeye geçer.
- Şartlar/KVKK onay kutuları kasıtlı olarak işaretlenmez — onları okuyup sen onaylamalısın.
- Zaten dolu olan alanların üstüne yazılmaz.
- Chrome oturumu `data/chrome-profile` altında saklanır; LinkedIn'e bir kez giriş yapman yeter.
  (Bu klasörü kullanan başka bir Chrome açıksa tarayıcı başlamaz.)

## Çıktılar

| Dosya | İçerik |
|---|---|
| `data/jobs.json` | Tüm ilanlar + hangisine başvurulduğu (uygulamanın hafızası) |
| `data/jobs.csv` | Excel'de açılabilir liste (UTF-8 BOM, noktalı virgül ayraçlı) |
| `data/jobs.md` | Aramaya göre gruplanmış, tıklanabilir linkli tablo |

`jobs.json` sayesinde her çalıştırmada sadece **yeni** ilanlar listelenir; aynı ilanı iki kez
gözden geçirmezsin.

## Sınırlar ve riskler

- **Kullanım şartları:** LinkedIn otomatik veri toplamayı kullanım şartlarında kısıtlıyor.
  Bu araç oturum sürmediği ve insan hızında çalıştığı için düşük profillidir, ama riski
  tamamen sıfırlamaz. `DelayBetweenRequestsMs` değerini düşürme.
- **HTML'e bağımlılık:** İlan çekme, LinkedIn'in kart yapısına dayanıyor. LinkedIn arayüzünü
  değiştirirse `JobSearchService.cs` içindeki düzenli ifadeler güncellenmeli.
- **Her form doldurulamaz:** Özel (div tabanlı) açılır listeler, çok adımlı Workday formları
  ve serbest metin soruları kısmen çalışır. Rapordaki "zorunlu ama boş" listesi tam olarak
  bunun için var.

## Lisans

MIT
