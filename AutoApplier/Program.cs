using AutoApplier.Models;
using AutoApplier.Services;

namespace AutoApplier
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            Console.WriteLine("=== AutoApplier — LinkedIn İş Başvuru Yardımcısı ===");
            Console.WriteLine($"Çalışma klasörü: {AppPaths.Root}");

            AppPaths.EnsureDirectories();

            while (true)
            {
                Console.WriteLine();
                Console.WriteLine("1) İlanları çek ve listele (giriş gerektirmez)");
                Console.WriteLine("2) Başvuru asistanı — ilanı aç, formu profile göre doldur");
                Console.WriteLine("3) Kaydedilen ilanları tekrar dışa aktar (CSV / Markdown)");
                Console.WriteLine("4) Profil eşleşmesini test et (hangi ilana hangi profil?)");
                Console.WriteLine("5) Profile uymayan ilanları topluca ele");
                Console.WriteLine("6) İlanlar hâlâ açık mı kontrol et (kapananları eler)");
                Console.WriteLine("0) Çıkış");
                Console.Write("Seçim > ");

                var choice = (Console.ReadLine() ?? "").Trim();

                try
                {
                    switch (choice)
                    {
                        case "1":
                            await FetchJobsAsync();
                            break;

                        case "2":
                            await RunApplyAssistantAsync();
                            break;

                        case "3":
                            ExportExisting();
                            break;

                        case "4":
                            TestProfileMatching();
                            break;

                        case "5":
                            DismissUnmatched();
                            break;

                        case "6":
                            await DismissClosedAsync();
                            break;

                        case "0":
                            return;

                        default:
                            Console.WriteLine("Geçersiz seçim.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine($"Hata: {ex.Message}");
                }
            }
        }

        // --- 1) İlan çekme -----------------------------------------------------

        private static async Task FetchJobsAsync()
        {
            var config = ConfigService.LoadOrCreate(
                AppPaths.SearchesFile, SearchConfig.CreateDefault, out var created);

            if (created)
            {
                Console.WriteLine();
                Console.WriteLine($"Örnek arama dosyası oluşturuldu: {AppPaths.SearchesFile}");
                Console.WriteLine("Aramalarını buradan düzenleyip tekrar çalıştır.");
                Console.WriteLine("Şimdilik örnek aramalarla devam ediliyor.");
                Console.WriteLine();
            }

            if (config.Searches.Count == 0)
            {
                Console.WriteLine("searches.json içinde tanımlı arama yok.");
                return;
            }

            var store = new JobStore();
            store.Load();

            using var service = new JobSearchService(config.DelayBetweenRequestsMs);
            var fetched = await service.SearchAllAsync(config);

            var newJobs = store.Merge(fetched);
            store.Save();

            Console.WriteLine();
            Console.WriteLine($"Toplam {fetched.Count} ilan çekildi, {newJobs.Count} tanesi daha önce görülmemiş.");

            if (newJobs.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("--- Yeni ilanlar ---");
                foreach (var job in newJobs.Take(25))
                {
                    Console.WriteLine($"  {job.PostedDisplay}  {job.Title} — {job.Company} ({job.Location})");
                    Console.WriteLine($"      {job.Url}");
                }

                if (newJobs.Count > 25)
                {
                    Console.WriteLine($"  ... ve {newJobs.Count - 25} tane daha (tam liste dosyalarda)");
                }
            }

            Export(store);
        }

        // --- 2) Başvuru asistanı -----------------------------------------------

        private static async Task RunApplyAssistantAsync()
        {
            var profiles = ConfigService.LoadOrCreate(
                AppPaths.ProfilesFile, ProfileConfig.CreateDefault, out var created);

            if (created)
            {
                Console.WriteLine();
                Console.WriteLine($"Örnek profil dosyası oluşturuldu: {AppPaths.ProfilesFile}");
                Console.WriteLine("Devam etmeden önce kendi bilgilerinle ve CV yollarınla doldur.");
                return;
            }

            var store = new JobStore();
            store.Load();

            var pending = store.Pending;
            if (pending.Count == 0)
            {
                Console.WriteLine("Başvurulmamış ilan yok. Önce menüden 1'i çalıştır.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"Durum: {pending.Count} bekleyen  |  {store.AppliedCount} başvuruldu  |  {store.DismissedCount} elendi");

            pending = FilterByRegion(pending, profiles);
            if (pending.Count == 0)
            {
                Console.WriteLine("Bu bölgede bekleyen ilan yok.");
                return;
            }

            pending = FilterByCompany(pending);
            if (pending.Count == 0)
            {
                Console.WriteLine("Bu süzgeçte bekleyen ilan yok.");
                return;
            }

            Console.Write($"Kaç ilanla ilgilenelim? [hepsi] > ");
            var input = (Console.ReadLine() ?? "").Trim();

            var jobs = int.TryParse(input, out var limit) && limit > 0
                ? pending.Take(limit).ToList()
                : pending;

            using var assistant = new ApplyAssistant(profiles, store);
            await assistant.RunAsync(jobs);

            Export(store);
        }

        /// <summary>
        /// Türkiye ve yurtdışı başvuruları farklı zihin durumları: yurtdışında sponsorluk,
        /// dil ve saat dilimi soruları var. Karışık bir kuyrukta gezmek yerine bölge seçilebiliyor.
        /// </summary>
        private static List<JobListing> FilterByRegion(List<JobListing> pending, ProfileConfig profiles)
        {
            var abroad = pending.Count(j => ProfileMatcher.IsAbroad(profiles, j));
            var local = pending.Count - abroad;

            Console.WriteLine();
            Console.WriteLine($"1) Hepsi ({pending.Count})   2) Türkiye ({local})   3) Yurtdışı ({abroad})");
            Console.Write("Bölge [1] > ");

            var choice = (Console.ReadLine() ?? "").Trim();

            return choice switch
            {
                "2" => pending.Where(j => !ProfileMatcher.IsAbroad(profiles, j)).ToList(),
                "3" => pending.Where(j => ProfileMatcher.IsAbroad(profiles, j)).ToList(),
                _ => pending
            };
        }

        /// <summary>
        /// Öncelikli şirket süzgeci. Kuyruk yüzlerce ilana çıkınca hepsini gezmek pratik
        /// değil; tanıdığın şirketlerle başlamak isteyebilirsin. Liste config/companies.json
        /// içinde ve elle düzenlenebilir.
        /// </summary>
        private static List<JobListing> FilterByCompany(List<JobListing> pending)
        {
            var companies = ConfigService.LoadOrCreate(
                AppPaths.CompaniesFile, CompanyConfig.CreateDefault, out var created);

            if (created)
            {
                Console.WriteLine();
                Console.WriteLine($"Öncelikli şirket listesi oluşturuldu: {AppPaths.CompaniesFile}");
            }

            var preferred = pending.Where(j => companies.Matches(j.Company)).ToList();
            if (preferred.Count == 0) return pending;

            Console.WriteLine();
            Console.WriteLine($"1) Tüm şirketler ({pending.Count})   2) Sadece öncelikli şirketler ({preferred.Count})");
            Console.Write("Şirket [1] > ");

            return (Console.ReadLine() ?? "").Trim() == "2" ? preferred : pending;
        }

        // --- 3) Dışa aktarma ---------------------------------------------------

        private static void ExportExisting()
        {
            var store = new JobStore();
            store.Load();

            if (store.All.Count == 0)
            {
                Console.WriteLine("Kayıtlı ilan yok.");
                return;
            }

            Export(store);
        }

        private static void Export(JobStore store)
        {
            var jobs = store.All
                .OrderByDescending(j => j.PostedDate ?? DateTime.MinValue)
                .ToList();

            JobExporter.ExportCsv(jobs, AppPaths.JobsCsvFile);
            JobExporter.ExportMarkdown(jobs, AppPaths.JobsMarkdownFile);

            Console.WriteLine();
            Console.WriteLine($"Kaydedildi ({jobs.Count} ilan):");
            Console.WriteLine($"  {AppPaths.JobsCsvFile}");
            Console.WriteLine($"  {AppPaths.JobsMarkdownFile}");
        }

        // --- 4) Profil eşleşme testi -------------------------------------------

        private static void TestProfileMatching()
        {
            var profiles = ConfigService.LoadOrCreate(
                AppPaths.ProfilesFile, ProfileConfig.CreateDefault, out _);

            var store = new JobStore();
            store.Load();

            if (store.All.Count == 0)
            {
                Console.WriteLine("Kayıtlı ilan yok. Önce menüden 1'i çalıştır.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("İlan başlığı → seçilen profil:");

            var fallbackCount = 0;

            foreach (var job in store.All.Take(40))
            {
                var resolved = ProfileMatcher.Resolve(profiles, job);
                var suffix = resolved.MatchedByKeyword ? "" : "   << eşleşme yok, varsayılan";
                if (!resolved.MatchedByKeyword) fallbackCount++;

                Console.WriteLine($"  {Trim(job.Title, 45),-47} → {resolved.ProfileName}{suffix}");
            }

            Console.WriteLine();

            if (fallbackCount > 0)
            {
                Console.WriteLine($"{fallbackCount} ilan hiçbir profile uymadı ve varsayılana düştü.");
                Console.WriteLine("Bunlara başvurursan ilanla alakasız bir CV/ön yazı gidebilir.");
            }

            Console.WriteLine("Yanlış eşleşenler varsa profiles.json içindeki MatchKeywords listelerini düzenle.");
        }

        // --- 6) Kapanmış ilanları ele -------------------------------------------

        /// <summary>Bu süre içinde bakılmış ilan yeniden sorgulanmaz.</summary>
        private const int RecheckAfterHours = 24;

        /// <summary>
        /// Bekleyen ilanları LinkedIn'den tek tek yoklayıp kapanmış olanları eler.
        /// En eskiden başlıyor: kapanma ihtimali en yüksek olanlar onlar.
        /// </summary>
        private static async Task DismissClosedAsync()
        {
            var store = new JobStore();
            store.Load();

            var pending = store.Pending
                .OrderBy(j => j.PostedDate ?? DateTime.MinValue)
                .ToList();

            if (pending.Count == 0)
            {
                Console.WriteLine("Bekleyen ilan yok.");
                return;
            }

            Console.WriteLine();
            Console.Write("Kaç günden eski ilanlar kontrol edilsin? [7] > ");

            var input = (Console.ReadLine() ?? "").Trim();
            var days = int.TryParse(input, out var parsed) && parsed >= 0 ? parsed : 7;
            var cutoff = DateTime.Now.Date.AddDays(-days);

            // Tarihi bilinmeyen ilan da kontrol edilir: yaşı belli değilse kapanmış olabilir.
            var aged = pending
                .Where(j => j.PostedDate == null || j.PostedDate.Value.Date <= cutoff)
                .ToList();

            // Yakında bakılmış ilanı tekrar sorgulamak boşuna: bir ilan bir günde kapanıp
            // açılmıyor. Bu olmadan her tarama, önceki taramada açık çıkan yüzlerce ilanı
            // baştan indiriyordu.
            var fresh = DateTime.Now.AddHours(-RecheckAfterHours);
            var targets = aged.Where(j => j.LastCheckedAt == null || j.LastCheckedAt < fresh).ToList();
            var skipped = aged.Count - targets.Count;

            if (targets.Count == 0)
            {
                Console.WriteLine(skipped > 0
                    ? $"{days} günden eski {skipped} ilan var ama hepsine son {RecheckAfterHours} saatte bakılmış."
                    : $"{days} günden eski bekleyen ilan yok.");
                return;
            }

            var minutes = Math.Ceiling(targets.Count * 1.8 / 60);
            Console.WriteLine($"{pending.Count} bekleyen ilanın {aged.Count} tanesi {days} günden eski.");

            if (skipped > 0)
            {
                Console.WriteLine($"Bunlardan {skipped} tanesine son {RecheckAfterHours} saatte bakılmıştı, atlanıyor.");
            }

            Console.WriteLine($"Her biri için bir istek atılacak; tahmini süre ~{minutes:0} dakika.");
            Console.Write("Başlansın mı? [e/h] > ");

            if ((Console.ReadLine() ?? "").Trim().ToLowerInvariant() is not ("e" or "evet" or "y" or "yes"))
            {
                Console.WriteLine("Vazgeçildi.");
                return;
            }

            using var checker = new JobStatusChecker();

            var closed = 0;
            var unknown = 0;

            for (var i = 0; i < targets.Count; i++)
            {
                var job = targets[i];
                var result = await checker.IsClosedAsync(job.Url);

                if (result != null) store.MarkChecked(job.JobId);

                if (result == true)
                {
                    store.MarkDismissed(job.JobId, "kapandı");
                    closed++;
                    Console.WriteLine($"  kapanmış: {Trim(job.Title, 42),-44} {job.Company}");
                }
                else if (result == null)
                {
                    unknown++;
                }

                Console.Write($"\r  {i + 1}/{targets.Count} kontrol edildi, {closed} kapalı...");
            }

            store.Save();

            Console.WriteLine();
            Console.WriteLine($"{targets.Count} ilan kontrol edildi, {closed} tanesi kapanmış — elendi. Kuyrukta {store.Pending.Count} ilan kaldı.");

            if (unknown > 0)
            {
                Console.WriteLine($"{unknown} ilanda karar verilemedi (ağ hatası ya da hız sınırı); onlara dokunulmadı.");
            }
        }

        // --- 5) Toplu eleme -----------------------------------------------------

        /// <summary>
        /// Hiçbir profille eşleşmeyen bekleyen ilanları topluca eler. Bunlar başvurulsa
        /// varsayılan profile düşeceği, yani ilanla alakasız bir CV gideceği ilanlar —
        /// kuyrukta durup her seferinde tekrar karşına çıkmalarının bir faydası yok.
        /// </summary>
        private static void DismissUnmatched()
        {
            var profiles = ConfigService.LoadOrCreate(
                AppPaths.ProfilesFile, ProfileConfig.CreateDefault, out _);

            var store = new JobStore();
            store.Load();

            var targets = store.Pending
                .Where(job => !ProfileMatcher.Resolve(profiles, job).MatchedByKeyword)
                .ToList();

            if (targets.Count == 0)
            {
                Console.WriteLine("Bekleyen ilanların hepsi bir profille eşleşiyor. Elenecek ilan yok.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"{store.Pending.Count} bekleyen ilandan {targets.Count} tanesi hiçbir profille eşleşmiyor:");

            foreach (var job in targets.Take(10))
            {
                Console.WriteLine($"  {Trim(job.Title, 45),-47} {job.Company}");
            }

            if (targets.Count > 10) Console.WriteLine($"  ... ve {targets.Count - 10} tane daha");

            Console.WriteLine();
            Console.WriteLine("Elenen ilanlar bir daha asistanın kuyruğuna girmez (jobs.json'da kalırlar).");
            Console.Write($"Bu {targets.Count} ilan elensin mi? [e/h] > ");

            var answer = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
            if (answer is not ("e" or "evet" or "y" or "yes"))
            {
                Console.WriteLine("Vazgeçildi, hiçbir şey değişmedi.");
                return;
            }

            foreach (var job in targets) store.MarkDismissed(job.JobId);
            store.Save();

            Console.WriteLine($"{targets.Count} ilan elendi. Kuyrukta {store.Pending.Count} ilan kaldı.");
        }

        private static string Trim(string value, int max) =>
            value.Length <= max ? value : value[..max] + "...";
    }
}
