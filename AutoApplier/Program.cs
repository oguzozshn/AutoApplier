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
                            RunApplyAssistant();
                            break;

                        case "3":
                            ExportExisting();
                            break;

                        case "4":
                            TestProfileMatching();
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

        private static void RunApplyAssistant()
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

            Console.Write($"{pending.Count} başvurulmamış ilan var. Kaç tanesiyle ilgilenelim? [hepsi] > ");
            var input = (Console.ReadLine() ?? "").Trim();

            var jobs = int.TryParse(input, out var limit) && limit > 0
                ? pending.Take(limit).ToList()
                : pending;

            using var assistant = new ApplyAssistant(profiles, store);
            assistant.Run(jobs);

            Export(store);
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

        private static string Trim(string value, int max) =>
            value.Length <= max ? value : value[..max] + "...";
    }
}
