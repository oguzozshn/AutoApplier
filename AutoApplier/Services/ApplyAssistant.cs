using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using AutoApplier.Models;

namespace AutoApplier.Services
{
    /// <summary>
    /// Kaydedilmiş ilanları sırayla açar, ilana uyan profili seçer ve başvuru formunu doldurur.
    /// Gönderme tuşuna basmaz — her başvuruyu sen gözden geçirip gönderirsin.
    /// </summary>
    public class ApplyAssistant : IDisposable
    {
        private readonly ProfileConfig _config;
        private readonly JobStore _store;
        private readonly FieldMemory _memory = new();
        private readonly AnswerDrafter _drafter;
        private IWebDriver? _driver;

        /// <summary>Son [d] çalıştırmasının raporu — [y] cevapsız soruları buradan alıyor.</summary>
        private FillReport? _lastReport;

        public ApplyAssistant(ProfileConfig config, JobStore store)
        {
            _config = config;
            _store = store;
            _memory.Load();

            var ai = ConfigService.LoadOrCreate(AppPaths.AiFile, AiConfig.CreateDefault, out _);
            _drafter = new AnswerDrafter(ai);
        }

        public async Task RunAsync(List<JobListing> jobs)
        {
            if (jobs.Count == 0)
            {
                Console.WriteLine("İşlenecek ilan yok. Önce ilan çek (menüden 1).");
                return;
            }

            StartBrowser();
            if (_driver == null) return;

            Console.WriteLine();
            Console.WriteLine($"{jobs.Count} ilan sırada.");
            Console.WriteLine("LinkedIn'e giriş yapmadıysan tarayıcıda şimdi giriş yap (oturum sonraki çalıştırmalarda hatırlanır).");
            Console.WriteLine("Hazır olduğunda Enter'a bas...");
            Console.ReadLine();

            for (var i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                var profile = ProfileMatcher.Resolve(_config, job);

                Console.WriteLine();
                Console.WriteLine(new string('=', 70));
                Console.WriteLine($"[{i + 1}/{jobs.Count}] {job.Title}");
                Console.WriteLine($"Şirket : {job.Company}");
                Console.WriteLine($"Konum  : {job.Location}");
                Console.WriteLine($"Tarih  : {job.PostedDisplay}");
                Console.WriteLine($"Link   : {job.Url}");
                Console.WriteLine($"Profil : {profile.ProfileName}" +
                                  (profile.MatchedByKeyword ? "" : " [eşleşme yok — varsayılan profil]") +
                                  (string.IsNullOrWhiteSpace(profile.ResumePath)
                                      ? " (CV tanımlı değil)"
                                      : $" — CV: {Path.GetFileName(profile.ResumePath)}"));
                Console.WriteLine(new string('=', 70));

                Console.Write("[Enter] aç  [a] şimdilik geç  [x] ilgilenmiyorum  [q] çık > ");
                var choice = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();

                if (choice == "q") break;
                if (choice == "a") continue;

                if (choice == "x")
                {
                    _store.MarkDismissed(job.JobId);
                    _store.Save();
                    Console.WriteLine("Elendi — bu ilan bir daha karşına çıkmayacak.");
                    continue;
                }

                if (!Navigate(job.Url))
                {
                    // Eskiden burada sessizce sonraki ilana geçiliyordu: kullanıcı "aç" dediği
                    // halde ilan atlanmış oluyordu ve sebebi ekranda kayboluyordu.
                    Console.Write("İlan açılamadı. [Enter] tekrar dene  [n] sonraki ilan > ");
                    var retry = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();

                    if (retry == "n" || !Navigate(job.Url))
                    {
                        Console.WriteLine("Bu ilan atlanıyor (beklemede kalır).");
                        continue;
                    }
                }

                if (JobStatusChecker.LooksClosed(SafePageSource()))
                {
                    Console.WriteLine();
                    Console.WriteLine("!! Bu ilan artık başvuru kabul etmiyor (LinkedIn kapanmış olarak işaretlemiş).");
                    Console.Write("[x] ele ve devam et  [Enter] yine de aç > ");

                    if ((Console.ReadLine() ?? "").Trim().ToLowerInvariant() == "x")
                    {
                        _store.MarkDismissed(job.JobId, "kapandı");
                        _store.Save();
                        Console.WriteLine("Elendi.");
                        continue;
                    }
                }

                try
                {
                    await HandleJobAsync(job, profile);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _store.Save();
            Console.WriteLine();
            Console.WriteLine("Asistan tamamlandı. İlerleme data/jobs.json içine kaydedildi.");
        }

        /// <summary>Tek bir ilan açıkken kullanıcı komutlarını işler.</summary>
        private async Task HandleJobAsync(JobListing job, ResolvedProfile profile)
        {
            Console.WriteLine();
            Console.WriteLine("İlan açıldı. Tarayıcıda başvuru butonuna tıkla (dış siteye yönlendirebilir).");
            Console.WriteLine("Form ekrandayken:");
            Console.WriteLine("  [d] formu doldur   (çok adımlı formlarda her adımda tekrar bas)");
            Console.WriteLine("  [y] cevapsız sorulara yapay zekâ ile taslak üret");
            Console.WriteLine("  [t] başvurdum — işaretle ve sonraki ilana geç");
            Console.WriteLine("  [x] ilgilenmiyorum — bir daha gösterme");
            Console.WriteLine("  [n] şimdilik geç (beklemede kalır)");
            Console.WriteLine("  [q] çık");

            // Formu doldurduysan büyük ihtimalle başvurdun. "n" ile geçerken bunu bir kez
            // soruyoruz, yoksa başvurduğun ilan beklemede kalıp tekrar tekrar karşına çıkıyor.
            var formFilled = false;

            while (true)
            {
                Console.Write("> ");
                var command = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();

                switch (command)
                {
                    case "d":
                        SwitchToFormTab();
                        FillCurrentPage(profile);
                        formFilled = true;
                        break;

                    case "y":
                        await DraftAnswersAsync(job, profile);
                        break;

                    case "t":
                        _store.MarkProcessed(job.JobId);
                        _store.Save();
                        Console.WriteLine("Başvuruldu olarak işaretlendi.");
                        LearnFromCurrentPage();
                        return;

                    case "x":
                        _store.MarkDismissed(job.JobId);
                        _store.Save();
                        Console.WriteLine("Elendi — bu ilan bir daha karşına çıkmayacak.");
                        return;

                    // Boş Enter bilerek "geç" saymıyor: ilanı açmak için basılan Enter'ın
                    // ikincisi buraya düşüp ilanı sessizce atlıyordu.
                    case "":
                        Console.WriteLine("[d] doldur, [t] başvurdum, [x] ilgilenmiyorum, [n] sonraki, [q] çık");
                        break;

                    case "n":
                        if (formFilled && AskApplied())
                        {
                            _store.MarkProcessed(job.JobId);
                            _store.Save();
                            Console.WriteLine("Başvuruldu olarak işaretlendi.");
                        }
                        return;

                    case "q":
                        throw new OperationCanceledException();

                    default:
                        Console.WriteLine("Bilinmeyen komut. [d] doldur, [t] başvurdum, [x] ilgilenmiyorum, [n] sonraki, [q] çık");
                        break;
                }
            }
        }

        private void FillCurrentPage(ResolvedProfile profile)
        {
            if (_driver == null) return;

            var filler = new FormFiller(_driver, _config, _memory);
            FillReport report;

            try
            {
                report = filler.Fill(profile);
                _lastReport = report;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Form doldurulurken hata: {ex.Message}");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"--- {_driver.Url} ---");

            if (report.Filled.Count > 0)
            {
                Console.WriteLine($"Dolduruldu ({report.Filled.Count}):");
                foreach (var item in report.Filled) Console.WriteLine("  + " + item);
            }
            else
            {
                Console.WriteLine("Hiçbir alan doldurulamadı. Sayfa hazır mı, form görünür mü kontrol et.");
            }

            if (report.RequiredLeftEmpty.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"ZORUNLU ama boş kalan alanlar ({report.RequiredLeftEmpty.Count}) — elle doldur:");
                foreach (var item in report.RequiredLeftEmpty) Console.WriteLine("  ! " + item);
            }

            if (report.Skipped.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"Atlananlar ({report.Skipped.Count}):");
                foreach (var item in report.Skipped.Take(15)) Console.WriteLine("  - " + item);
                if (report.Skipped.Count > 15) Console.WriteLine($"  ... ve {report.Skipped.Count - 15} tane daha");
            }

            Console.WriteLine();
            Console.WriteLine("Formu kontrol et ve göndermeyi SEN yap. Bu araç gönder tuşuna basmaz.");
        }

        /// <summary>
        /// Cevapsız serbest metin sorularına yerel modelle taslak üretir.
        ///
        /// Üretilen metin onay almadan forma YAZILMAZ: model, sende olmayan bir deneyimi
        /// uydurabilir ve bu, başvuruda yanlış beyan olur. Karar her soruda sende.
        /// </summary>
        private async Task DraftAnswersAsync(JobListing job, ResolvedProfile profile)
        {
            if (!_drafter.Enabled)
            {
                Console.WriteLine($"Yapay zekâ kapalı. Açmak için {AppPaths.AiFile} içinde Enabled true yapılmalı.");
                return;
            }

            if (_lastReport == null)
            {
                Console.WriteLine("Önce [d] ile formu doldur; cevapsız sorular ondan sonra belli oluyor.");
                return;
            }

            var questions = _lastReport.Unanswered.Where(f => IsStillEmpty(f.Element)).ToList();

            if (questions.Count == 0)
            {
                Console.WriteLine("Cevapsız serbest metin sorusu yok.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"{questions.Count} soru icin taslak uretilecek (model: {_drafter.Model}).");

            foreach (var field in questions)
            {
                Console.WriteLine();
                Console.WriteLine(new string('-', 70));
                Console.WriteLine((field.Required ? "[zorunlu] " : "") + field.Question);
                Console.WriteLine("Taslak uretiliyor, bekle...");

                var (text, error) = await _drafter.DraftAsync(job, profile, field.Question);

                if (text == null)
                {
                    Console.WriteLine(error);
                    return;
                }

                Console.WriteLine();
                Console.WriteLine(text);
                Console.WriteLine();
                Console.Write("[e] forma yaz  [a] atla  [q] taslak uretmeyi birak > ");

                var choice = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();

                if (choice == "q") return;
                if (choice != "e") continue;

                if (!TypeInto(field.Element, text))
                {
                    Console.WriteLine("Alana yazilamadi (sayfa degismis olabilir).");
                    continue;
                }

                Console.WriteLine("Yazildi.");

                // Onayladığın cevap hafızaya da giriyor: aynı soru bir daha modele sorulmaz.
                _memory.Remember(FieldRules.Normalize(field.Question), field.Question, text, SafeHost());
                _memory.Save();
            }
        }

        private static bool IsStillEmpty(IWebElement element)
        {
            try
            {
                return element.Displayed && string.IsNullOrWhiteSpace(element.GetAttribute("value"));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TypeInto(IWebElement element, string text)
        {
            try
            {
                element.Clear();
                element.SendKeys(text);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string SafeHost()
        {
            try { return new Uri(_driver!.Url).Host; }
            catch (Exception) { return ""; }
        }

        /// <summary>
        /// Başvuru tamamlandığında formda duran cevapları hafızaya alır. Araç bir alanı
        /// dolduramadığında sen elle dolduruyorsun; o bilgi kaybolmasın, aynı soru bir
        /// sonraki formda hazır gelsin diye.
        /// </summary>
        private void LearnFromCurrentPage()
        {
            if (_driver == null) return;

            try
            {
                var site = new Uri(_driver.Url).Host;
                var answers = new FormFiller(_driver, _config, _memory).CaptureAnswers();

                var yeni = 0;
                foreach (var answer in answers)
                {
                    if (_memory.Remember(answer.Normalized, answer.Label, answer.Value, site)) yeni++;
                }

                if (answers.Count == 0) return;

                _memory.Save();
                Console.WriteLine($"Öğrenildi: {yeni} yeni cevap (hafızada toplam {_memory.Count}). " +
                                  "Aynı sorular bir dahaki formda otomatik dolacak.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Cevaplar hafızaya alınamadı: {ex.Message}");
            }
        }

        /// <summary>Formu doldurduktan sonra "n" ile geçerken başvurunun tamamlanıp tamamlanmadığını sorar.</summary>
        private static bool AskApplied()
        {
            Console.Write("Bu ilana başvurdun mu? [e/h] > ");
            var answer = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
            return answer is "e" or "evet" or "y" or "yes";
        }

        /// <summary>
        /// Formun bulunduğu sekmeye geçer: en çok doldurulabilir alanı olan sekme kazanır.
        ///
        /// Önce "en son açılan sekme" varsayılıyordu ama başvuru akışları bunu bozuyor: ilan
        /// sayfası yeni sekmede açılıp form ilk sekmede kalabiliyor, ya da site araya sekme
        /// açıyor. O durumda doldurucu iş tanımı sayfasında çalışıp "hiçbir alan doldurulamadı"
        /// diyordu — form ekranın önünde dururken.
        /// </summary>
        private void SwitchToFormTab()
        {
            if (_driver == null) return;

            try
            {
                var handles = _driver.WindowHandles;
                if (handles.Count <= 1) return;

                var current = _driver.CurrentWindowHandle;
                var bestHandle = current;
                var bestScore = -1;

                foreach (var handle in handles)
                {
                    try
                    {
                        _driver.SwitchTo().Window(handle);
                        var score = CountFillableFields();

                        // Eşitlikte sonraki sekme kazanır: başvuru genelde ileri doğru açılıyor.
                        if (score >= bestScore)
                        {
                            bestScore = score;
                            bestHandle = handle;
                        }
                    }
                    catch (Exception) { }
                }

                _driver.SwitchTo().Window(bestHandle);

                if (bestHandle != current)
                {
                    Console.WriteLine($"Form sekmesine geçildi ({bestScore} alan): {_driver.Url}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Sekme değiştirilemedi: {ex.Message}");
            }
        }

        /// <summary>Sayfadaki görünür ve doldurulabilir alan sayısı.</summary>
        private int CountFillableFields()
        {
            try
            {
                var result = ((IJavaScriptExecutor)_driver!).ExecuteScript("""
                    return [...document.querySelectorAll(
                        "input:not([type=hidden]):not([type=submit]):not([type=button]), select, textarea")]
                        .filter(e => e.offsetParent !== null && !e.disabled && !e.readOnly).length;
                    """);

                return Convert.ToInt32(result);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>Sayfa kaynağı — oturum ölmüşse patlamak yerine null döner.</summary>
        private string? SafePageSource()
        {
            try { return _driver?.PageSource; }
            catch { return null; }
        }

        private bool Navigate(string url)
        {
            if (!EnsureBrowserAlive()) return false;

            try
            {
                _driver!.Navigate().GoToUrl(url);
                Thread.Sleep(2000);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"İlan açılamadı: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Tarayıcı penceresi kapatılırsa oturum ölüyor ve sonraki her komut "invalid session id"
        /// ile patlıyor. Uygulama bunu fark etmezse kuyruktaki tüm ilanlar sırayla boşa geçiyor;
        /// o yüzden her ilandan önce oturumu yokluyor, ölmüşse tarayıcıyı yeniden açıyoruz.
        /// Chrome profili kalıcı olduğu için LinkedIn oturumu korunuyor.
        /// </summary>
        private bool EnsureBrowserAlive()
        {
            if (_driver != null && IsSessionAlive(_driver)) return true;

            Console.WriteLine("Tarayıcı oturumu kapanmış. Yeniden başlatılıyor...");

            try { _driver?.Quit(); } catch { }
            try { _driver?.Dispose(); } catch { }
            _driver = null;

            StartBrowser();

            if (_driver == null)
            {
                Console.WriteLine("Tarayıcı yeniden açılamadı. Asistandan çıkıp tekrar dene.");
                return false;
            }

            Console.WriteLine("Tarayıcı yeniden açıldı.");
            return true;
        }

        private static bool IsSessionAlive(IWebDriver driver)
        {
            try
            {
                _ = driver.WindowHandles.Count;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void StartBrowser()
        {
            var options = new ChromeOptions();
            options.AddArgument("--start-maximized");
            options.AddExcludedArgument("enable-automation");

            // Chrome kendi tanılama günlüğünü stderr'e basıyor (google_apis, mojo, WidgetHost...).
            // Bu satırlar konsolu doldurup asistanın raporunu okunmaz hale getiriyordu.
            options.AddArgument("--log-level=3");
            options.AddExcludedArgument("enable-logging");

            // Kalıcı profil: LinkedIn oturumu her çalıştırmada tekrar açılmasın diye.
            var profileDir = Path.Combine(AppPaths.DataDir, "chrome-profile");
            Directory.CreateDirectory(profileDir);
            options.AddArgument($"--user-data-dir={profileDir}");

            var service = ChromeDriverService.CreateDefaultService();
            service.SuppressInitialDiagnosticInformation = true;
            service.HideCommandPromptWindow = true;

            try
            {
                _driver = new ChromeDriver(service, options);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Chrome başlatılamadı: {ex.Message}");
                Console.WriteLine($"Bu profil klasörünü kullanan başka bir Chrome açıksa kapat: {profileDir}");
            }
        }

        public void Dispose()
        {
            try
            {
                _drafter.Dispose();
                _driver?.Quit();
                _driver?.Dispose();
            }
            catch { }

            GC.SuppressFinalize(this);
        }
    }
}
