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
        private IWebDriver? _driver;

        public ApplyAssistant(ProfileConfig config, JobStore store)
        {
            _config = config;
            _store = store;
        }

        public void Run(List<JobListing> jobs)
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

                if (!Navigate(job.Url)) continue;

                try
                {
                    HandleJob(job, profile);
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
        private void HandleJob(JobListing job, ResolvedProfile profile)
        {
            Console.WriteLine();
            Console.WriteLine("İlan açıldı. Tarayıcıda başvuru butonuna tıkla (dış siteye yönlendirebilir).");
            Console.WriteLine("Form ekrandayken:");
            Console.WriteLine("  [d] formu doldur   (çok adımlı formlarda her adımda tekrar bas)");
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
                        SwitchToNewestTab();
                        FillCurrentPage(profile);
                        formFilled = true;
                        break;

                    case "t":
                        _store.MarkProcessed(job.JobId);
                        _store.Save();
                        Console.WriteLine("Başvuruldu olarak işaretlendi.");
                        return;

                    case "x":
                        _store.MarkDismissed(job.JobId);
                        _store.Save();
                        Console.WriteLine("Elendi — bu ilan bir daha karşına çıkmayacak.");
                        return;

                    case "n":
                    case "":
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

            var filler = new FormFiller(_driver, _config);
            FillReport report;

            try
            {
                report = filler.Fill(profile);
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

        /// <summary>Formu doldurduktan sonra "n" ile geçerken başvurunun tamamlanıp tamamlanmadığını sorar.</summary>
        private static bool AskApplied()
        {
            Console.Write("Bu ilana başvurdun mu? [e/h] > ");
            var answer = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
            return answer is "e" or "evet" or "y" or "yes";
        }

        /// <summary>Başvuru butonu yeni sekmede açılmış olabilir; en son açılan sekmeye geç.</summary>
        private void SwitchToNewestTab()
        {
            if (_driver == null) return;

            try
            {
                var handles = _driver.WindowHandles;
                if (handles.Count > 1 && _driver.CurrentWindowHandle != handles[^1])
                {
                    _driver.SwitchTo().Window(handles[^1]);
                    Console.WriteLine($"Yeni sekmeye geçildi: {_driver.Url}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Sekme değiştirilemedi: {ex.Message}");
            }
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
                _driver?.Quit();
                _driver?.Dispose();
            }
            catch { }

            GC.SuppressFinalize(this);
        }
    }
}
