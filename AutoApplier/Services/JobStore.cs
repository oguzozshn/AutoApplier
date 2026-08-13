using System.Text.Json;
using AutoApplier.Models;

namespace AutoApplier.Services
{
    /// <summary>
    /// Çekilen ilanları data/jobs.json içinde saklar. Her çalıştırmada sadece yeni ilanları
    /// ayırt edebilmeni ve hangi ilana başvurduğunu hatırlamayı sağlar.
    /// </summary>
    public class JobStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private Dictionary<string, JobListing> _jobs = new();

        public IReadOnlyCollection<JobListing> All => _jobs.Values;

        /// <summary>Ne başvurulmuş ne de elenmiş ilanlar — asistanın kuyruğu budur.</summary>
        public List<JobListing> Pending => _jobs.Values
            .Where(j => !j.Processed && !j.Dismissed)
            .OrderByDescending(j => j.PostedDate ?? DateTime.MinValue)
            .ToList();

        public int AppliedCount => _jobs.Values.Count(j => j.Processed);
        public int DismissedCount => _jobs.Values.Count(j => j.Dismissed);

        public void Load()
        {
            AppPaths.EnsureDirectories();

            if (!File.Exists(AppPaths.JobsFile))
            {
                _jobs = new Dictionary<string, JobListing>();
                return;
            }

            try
            {
                var json = File.ReadAllText(AppPaths.JobsFile);
                var list = JsonSerializer.Deserialize<List<JobListing>>(json) ?? new List<JobListing>();
                _jobs = list
                    .Where(j => !string.IsNullOrEmpty(j.JobId))
                    .GroupBy(j => j.JobId)
                    .ToDictionary(g => g.Key, g => g.First());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"jobs.json okunamadı ({ex.Message}). Boş liste ile devam ediliyor.");
                _jobs = new Dictionary<string, JobListing>();
            }
        }

        public void Save()
        {
            AppPaths.EnsureDirectories();
            var json = JsonSerializer.Serialize(_jobs.Values.ToList(), JsonOptions);
            File.WriteAllText(AppPaths.JobsFile, json);
        }

        /// <summary>
        /// Yeni çekilen ilanları kaydeder. Daha önce görülmemiş olanları döndürür —
        /// çıktıda "bu sefer ne çıktı" sorusunun cevabı budur.
        /// </summary>
        public List<JobListing> Merge(IEnumerable<JobListing> fetched)
        {
            var newJobs = new List<JobListing>();

            foreach (var job in fetched)
            {
                if (string.IsNullOrEmpty(job.JobId)) continue;

                if (_jobs.TryGetValue(job.JobId, out var existing))
                {
                    // Zaten bilinen ilan: sadece eksik alanları tazele, Processed bayrağını koru.
                    if (string.IsNullOrWhiteSpace(existing.SalaryInfo)) existing.SalaryInfo = job.SalaryInfo;
                    if (existing.PostedDate == null) existing.PostedDate = job.PostedDate;
                    continue;
                }

                _jobs[job.JobId] = job;
                newJobs.Add(job);
            }

            return newJobs
                .OrderByDescending(j => j.PostedDate ?? DateTime.MinValue)
                .ToList();
        }

        public void MarkProcessed(string jobId)
        {
            if (_jobs.TryGetValue(jobId, out var job)) job.Processed = true;
        }

        /// <summary>İlanı kuyruktan kalıcı olarak çıkarır; başvuruldu olarak işaretlemez.</summary>
        public void MarkDismissed(string jobId)
        {
            if (_jobs.TryGetValue(jobId, out var job)) job.Dismissed = true;
        }
    }
}
