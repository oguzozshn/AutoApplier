# AutoApplier — nasıl çalışıyor

LinkedIn'den ilan çeken ve başvuru formlarını pozisyona uygun bilgilerle dolduran bir konsol
uygulaması. Bu belge **iç işleyişi** anlatır; kurulum ve günlük kullanım için
[AutoApplier/README.md](AutoApplier/README.md) dosyasına bak.

Tek cümlelik özet: araç iki bağımsız işten oluşuyor — **ilan toplama** (tarayıcısız, HTTP) ve
**form doldurma** (Selenium). İkisini `data/jobs.json` birbirine bağlıyor.

## Genel akış

```
                    config/searches.json
                            |
  [1] JobSearchService ---- HTTP ----> LinkedIn guest arama uç noktası
          |  regex ile HTML kart ayrıştırma
          v
      JobStore  <---->  data/jobs.json        JobExporter --> jobs.csv / jobs.md
          |  (henüz başvurulmamış ilanlar)
          v
  [2] ApplyAssistant ----- Selenium ----> Chrome (ilan sayfası, dış başvuru formu)
          |                                          ^
          |  ProfileMatcher: ilana uyan profil       | doldurulan alanlar
          |         + config/profiles.json           |
          v                                          |
      ResolvedProfile (kanonik cevap sözlüğü) --> FormFiller --> FieldRules
```

İki iş neden ayrı: ilan toplamak için oturuma ve tarayıcıya gerek yok, guest uç noktası herkese
açık. Selenium yalnızca gerçekten bir formu doldurmak gerektiğinde devreye giriyor — bu hem çok
daha hızlı hem de LinkedIn tarafında çok daha düşük profilli.

## Dosya sorumlulukları

| Dosya | Sorumluluk |
|---|---|
| [Program.cs](AutoApplier/Program.cs) | Menü döngüsü, akışların birbirine bağlanması |
| [JobSearchService.cs](AutoApplier/Services/JobSearchService.cs) | Guest uç noktasından ilan çekme, HTML ayrıştırma |
| [JobStore.cs](AutoApplier/Services/JobStore.cs) | `jobs.json` — kalıcılık, "yeni mi?" ve "başvuruldu mu?" |
| [JobExporter.cs](AutoApplier/Services/JobExporter.cs) | CSV / Markdown çıktısı |
| [ApplyAssistant.cs](AutoApplier/Services/ApplyAssistant.cs) | Chrome'u sürer, ilanı açar, konsol komutlarını işler |
| [ProfileMatcher.cs](AutoApplier/Services/ProfileMatcher.cs) | İlana uyan profili seçer, cevap sözlüğünü kurar |
| [FormFiller.cs](AutoApplier/Services/FormFiller.cs) | Sayfadaki alanları bulur, etiketini okur, doldurur |
| [FieldRules.cs](AutoApplier/Services/FieldRules.cs) | Etiket metni → hangi bilgi (kural tablosu) |
| [ConfigService.cs](AutoApplier/Services/ConfigService.cs) | JSON ayar okuma; dosya yoksa örnek üretme |
| [AppPaths.cs](AutoApplier/Services/AppPaths.cs) | `config/` ve `data/` yollarının tek kaynağı |

Ayar dosyaları çalışma dizinine göre çözülüyor (`AppPaths.Root = Directory.GetCurrentDirectory()`),
yani `dotnet run` ile proje klasöründeyken düzenlediğin dosyalar yeniden derlemeden geçerli oluyor.

## 1) İlan çekme

`https://www.linkedin.com/jobs-guest/jobs/api/seeMoreJobPostings/search` uç noktası tam bir sayfa
değil, `<li>` kartlarından oluşan bir HTML parçası döndürüyor ve giriş istemiyor. `searches.json`
içindeki her alan bir sorgu parametresine çevriliyor:

| Ayar | Parametre |
|---|---|
| `PostedWithin` | `f_TPR=r86400` / `r604800` / `r2592000` |
| `WorkplaceType` | `f_WT=1` (ofis) / `2` (uzaktan) / `3` (hibrit) |
| `JobType` | `f_JT=F` / `P` / `C` / `I` |
| `EasyApplyOnly` | `f_AL=true` |
| `SortByDate` | `sortBy=DD` |

Sayfa boyutu 10; `start` 10'ar artıyor. Döngü şu durumlarda kesiliyor: `MaxResults` doldu,
**arka arkaya iki boş sayfa** geldi, `start >= 1000` (uç nokta zaten ötesini vermiyor), HTTP 429
(hız sınırı) ya da başka bir hata. İstekler arasında `DelayBetweenRequestsMs` kadar bekleniyor.

Ayrıştırma **düzenli ifadelerle** yapılıyor, HTML kütüphanesi yok — ek bağımlılık getirmemek için
bilinçli bir tercih. HTML `<li ` ile parçalanıyor, her parçadan `urn:li:jobPosting:(\d+)` ile
kimlik, `base-search-card__title` gibi sınıf adlarıyla başlık/şirket/konum/tarih/maaş çekiliyor.
Kimlik ve başlık yoksa o parça ilan kartı sayılmıyor. Linkteki takip parametreleri (`?...`)
atılıyor. **LinkedIn kart yapısını değiştirirse güncellenmesi gereken tek yer burası.**

Aynı ilan birden fazla aramadan gelebilir; tekilleştirme `JobId` üzerinden yapılıyor.

## 2) Kalıcılık — jobs.json

`JobStore.Merge` iki şey yapıyor: yeni ilanları ekliyor ve **daha önce görülenlerin `Processed`
bayrağını koruyor** (yalnızca boş kalmış `SalaryInfo`/`PostedDate` alanlarını tazeliyor). Bu
sayede her çalıştırmada sadece gerçekten yeni ilanlar listeleniyor ve başvuru geçmişi
kaybolmuyor. `Pending` = `Processed == false` olanlar, tarihe göre yeniden eskiye.

## 3) Profil seçimi

`ProfileMatcher.SelectProfile`, `"{başlık} {şirket}"` metnini her profilin `MatchKeywords`
listesiyle karşılaştırıyor. Puan = **eşleşen anahtar kelimelerin uzunlukları toplamı** — böylece
`"asp.net"` gibi spesifik bir kelime `"api"` gibi genel bir kelimeyi yeniyor. En yüksek puanlı
profil kazanıyor; hiçbir kelime tutmazsa `DefaultProfile`, o da yoksa listedeki ilk profil
kullanılıyor.

Bu ayrım `MatchedByKeyword` bayrağıyla taşınıyor ve ekranda `[eşleşme yok — varsayılan profil]`
diye gösteriliyor: ilana alakasız bir CV gitmesi sessizce olmasın diye. Menü 4 bu eşleşmeleri
başvuru yapmadan önce toplu olarak test etmek için var.

## 4) Cevap sözlüğü

Form doldurucu doğrudan `profiles.json` şemasını bilmiyor; arada **kanonik anahtarlar**
(`AnswerKeys.FirstName`, `AnswerKeys.CoverLetter`, ...) var. `ProfileMatcher.Resolve` şunu üretiyor:

1. `Personal` altındaki ortak bilgiler sözlüğe yazılır (her başvuruda aynı).
2. Seçilen profilin pozisyona özel alanları (**CV, unvan, deneyim, maaş, ön yazı, yetenekler**)
   bunların üzerine yazılır — sadece boş olmayanlar.
3. Ön yazı ve serbest cevaplardaki `{position}`, `{company}`, `{name}` yer tutucuları doldurulur.

Sonuç `ResolvedProfile`: bir `Answers` sözlüğü + profile özel `ExtraAnswers` + o pozisyonun CV
yolu. Bu ara katman sayesinde yeni bir bilgi türü eklemek, form tarafını hiç ellemeden mümkün.

## 5) Form doldurma

`FormFiller.Fill` sayfayı alan türüne göre altı geçişte tarıyor: dosya yükleme → metin alanları →
`<select>` → radio grupları → onay kutuları → özel (div tabanlı) açılır listeler. Her alan için
aynı üç adım işliyor:

**a. Etiketi oku.** Bir JS betiği alanla ilgili tüm metin kaynaklarını topluyor: `aria-label`,
`aria-labelledby`, `label[for]`, saran `<label>`, `<fieldset><legend>`, `role="group"` etiketi,
`placeholder`, ardından `name` / `data-automation-id` / `title`. Standart kaynakların hiçbiri
tutmazsa **`containerText`** yedeğine düşülüyor: alanı saran kutunun metninden girdi elemanları
temizlenip geriye soru metni bırakılıyor. Lever gibi sistemler soruyu `<label>` yerine ayrı bir
div'de tutup alana `cards[uuid][field0]` adını verdiği için bu yedek olmadan alanlar isimsiz
kalıyordu.

Radio grupları ayrı ele alınıyor (`GroupLabelScript`): `closest('label')` ilk seçeneğin metnini
("Yes") döndürüp hem raporu hem eşleştirmeyi bozduğu için, **`radioQuestion`** aynı `name`'i
paylaşan birden fazla seçeneği kapsayan ilk kutuyu bulup seçenek etiketlerini çıkarıyor; geriye
sorunun kendisi kalıyor.

**b. Etiketi bir cevaba bağla.** Metin önce `FieldRules.Normalize`'dan geçiyor: küçük harf, Türkçe
karakterler ASCII karşılığına (`Şehir` → `sehir`), noktalama boşluğa. Böylece kural tablosu düz
ASCII yazılabiliyor.

Sonra `FieldRules.MatchKey` **ilk eşleşeni değil, en uzun anahtar kelimeyle eşleşeni** seçiyor.
Bu kritik: form etiketleri çoğu zaman tam cümle oluyor ve kısa kelimeler cümle içinde kazara
geçiyor. `"Are you legally authorized to work in this country?"` sorusunda hem `country` hem
`legally authorized` tutuyor — uzun olan doğru. Sıraya güvenmek bu çakışmalarda sessizce yanlış
alanı doldururdu. Aynı mekanizma `location` (Şehir) kuralının `relocation` sorusunu çalmasını da
engelliyor. Genel bir kelimenin yanlış alana bulaştığı durumlar için kuralın `Excludes` listesi var
(`address` kuralı `email address`'e düşmesin diye).

Profildeki `ExtraAnswers` bu tablodan **önce** bakılıyor ve her şeyi eziyor — şirkete özel
sorular buradan cevaplanıyor, araç onları kendi başına tahmin etmiyor.

**c. Değeri yaz.** `<select>` için önce birebir, sonra kısmi eşleşme deneniyor (`"3"` → `"3-5
years"`); "Seçiniz" tipi yer tutucu seçenekler eşleşme sayılmıyor. Radio'da seçenek etiketleri
cevapla karşılaştırılıyor. Özel açılır listelerde kutu tıklanıp `role="option"` öğeleri aranıyor,
bulunamazsa liste `Escape` ile kapatılıyor.

**Kasıtlı olarak yapmadıkları:**

- **Gönder tuşuna basmaz.** Hiçbir kod yolunda submit yok.
- **Şartlar/KVKK onay kutularını işaretlemez** (`terms`, `privacy`, `consent`, `kvkk`, `açık rıza`...
  içeren etiketler atlanır) — onları okuyup onaylamak sana ait.
- **Dolu alanın üstüne yazmaz** — siteyi kendi doldurmuş olabilir.

Her geçiş sonunda `FillReport` üç liste döndürüyor: dolduruldu, **zorunlu olup boş kaldı**
(`required` / `aria-required` olup cevap bulunamayanlar) ve atlandı. Ortadaki liste aracın
dürüstlük mekanizması: neyi yapamadığını saklamak yerine elle doldurman gerekenleri sayıyor.

Çok adımlı formlar için durum tutulmuyor; her adımda `d` komutuyla doldurucu yeniden çalışıyor.
Başvuru butonu yeni sekme açarsa `d` komutu önce en son açılan sekmeye geçiyor.

## Tarayıcı oturumu

Chrome, `data/chrome-profile` altındaki kalıcı profille açılıyor (`--user-data-dir`), böylece
LinkedIn'e bir kez giriş yapmak yetiyor. ChromeDriver sürümü sabitlenmedi — Selenium Manager kurulu
Chrome'a uyan sürücüyü ilk çalıştırmada indirip önbelleğe alıyor, Chrome güncellendiğinde sürüm
uyuşmazlığı çıkmıyor. Aynı profil klasörünü kullanan başka bir Chrome açıksa tarayıcı başlamıyor.

## Bir şey değiştirmek gerektiğinde

| Belirti | Bakılacak yer |
|---|---|
| İlan çekilmiyor / alanlar boş geliyor | `JobSearchService` içindeki regex'ler — LinkedIn kart yapısı değişmiştir |
| Alan doldurulmuyor, raporda "(isimsiz alan)" | Etiket okuma betikleri (`LabelScript`, `containerText`) — sitenin etiketi farklı bir yerde |
| Yanlış alana doğru bilgi yazılıyor | `FieldRules` — kuralın `Excludes` listesine ekle ya da daha uzun bir anahtar kelime tanımla |
| Yeni bir bilgi türü lazım | `AnswerKeys` + `FieldRules` + `ProfileMatcher.Resolve` (üç yerde de) |
| İlana yanlış CV/ön yazı gidiyor | `profiles.json` içindeki `MatchKeywords` — önce menü 4 ile test et |
| Şirkete özel soru cevaplanmıyor | Profilin `ExtraAnswers` alanı; anahtar, etikette geçen bir ifade olmalı |

## Bilinen sınırlar

- Guest uç noktası ilan **açıklamasını** vermiyor; elde yalnızca kart üstündeki bilgiler var.
- Regex tabanlı ayrıştırma HTML değişikliklerine kırılgan — bilinçli takas.
- Çok adımlı Workday formları ve özel açılır listeler kısmen çalışıyor; rapordaki "zorunlu ama
  boş" listesi tam olarak bunun için var.
- LinkedIn otomatik veri toplamayı kullanım şartlarında kısıtlıyor. Araç oturum sürmediği ve insan
  hızında çalıştığı için düşük profilli, ama riski sıfırlamıyor — `DelayBetweenRequestsMs`
  değerini düşürme.
