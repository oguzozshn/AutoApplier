using System.Net;
using System.Text.RegularExpressions;
using AutoApplier.Models;

namespace AutoApplier.Services
{
    /// <summary>
    /// LinkedIn'in giriş gerektirmeyen "guest" iş arama uç noktasından ilan listesi çeker.
    /// Tarayıcı sürmediği ve oturum kullanmadığı için Selenium'lu yaklaşımdan çok daha hızlı ve düşük riskli.
    /// </summary>
    public class JobSearchService : IDisposable
    {
        private const string GuestSearchUrl =
            "https://www.linkedin.com/jobs-guest/jobs/api/seeMoreJobPostings/search";

        // Guest uç noktası sayfa başına 10 ilan döndürüyor.
        private const int PageSize = 10;

        private readonly HttpClient _http;
        private readonly int _delayMs;

        public JobSearchService(int delayBetweenRequestsMs = 2500)
        {
            _delayMs = Math.Max(500, delayBetweenRequestsMs);

            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

            // Normal bir tarayıcı gibi görünmezsek uç nokta boş cevap dönüyor.
            _http.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            _http.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");
        }

        /// <summary>Tanımlı tüm aramaları sırayla çalıştırır ve sonuçları birleştirir (JobId'ye göre tekilleştirilmiş).</summary>
        public async Task<List<JobListing>> SearchAllAsync(SearchConfig config)
        {
            var all = new Dictionary<string, JobListing>();

            foreach (var query in config.Searches)
            {
                Console.WriteLine();
                Console.WriteLine($"[{query.Name}] \"{query.Keywords}\" / {query.Location} aranıyor...");

                var results = await SearchAsync(query);

                var newCount = 0;
                foreach (var job in results)
                {
                    if (all.TryAdd(job.JobId, job)) newCount++;
                }

                Console.WriteLine($"[{query.Name}] {results.Count} ilan bulundu ({newCount} tanesi yeni).");
            }

            return all.Values
                .OrderByDescending(j => j.PostedDate ?? DateTime.MinValue)
                .ToList();
        }

        /// <summary>Tek bir aramayı sayfa sayfa çeker.</summary>
        public async Task<List<JobListing>> SearchAsync(SearchQuery query)
        {
            var jobs = new Dictionary<string, JobListing>();
            var start = 0;
            var emptyPages = 0;

            while (jobs.Count < query.MaxResults)
            {
                var url = BuildUrl(query, start);

                string html;
                try
                {
                    using var response = await _http.GetAsync(url);

                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        Console.WriteLine("  LinkedIn hız sınırına takıldı (429). Bu arama burada kesiliyor.");
                        Console.WriteLine("  Bir süre bekleyip tekrar dene ya da searches.json'da DelayBetweenRequestsMs değerini artır.");
                        break;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"  Beklenmeyen HTTP durumu: {(int)response.StatusCode}. Bu arama kesiliyor.");
                        break;
                    }

                    html = await response.Content.ReadAsStringAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  İstek hatası: {ex.Message}");
                    break;
                }

                var page = ParseJobCards(html, query.Name);

                if (page.Count == 0)
                {
                    // Arka arkaya iki boş sayfa gelirse sonuçlar bitmiştir.
                    if (++emptyPages >= 2) break;
                }
                else
                {
                    emptyPages = 0;
                }

                foreach (var job in page)
                {
                    if (IsExcluded(job, query)) continue;
                    if (jobs.Count >= query.MaxResults) break;
                    jobs.TryAdd(job.JobId, job);
                }

                Console.Write($"\r  {jobs.Count} ilan...");

                start += PageSize;

                // 1000 üstü guest uç noktasında zaten boş dönüyor.
                if (start >= 1000) break;

                await Task.Delay(_delayMs);
            }

            Console.WriteLine();
            return jobs.Values.ToList();
        }

        private static bool IsExcluded(JobListing job, SearchQuery query)
        {
            if (query.ExcludeTitleKeywords.Count == 0) return false;

            return query.ExcludeTitleKeywords.Any(keyword =>
                !string.IsNullOrWhiteSpace(keyword) &&
                job.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildUrl(SearchQuery query, int start)
        {
            var parameters = new List<string>
            {
                "keywords=" + Uri.EscapeDataString(query.Keywords ?? ""),
                "location=" + Uri.EscapeDataString(query.Location ?? ""),
                "start=" + start
            };

            var postedSeconds = query.PostedWithin?.ToLowerInvariant() switch
            {
                "day" => "r86400",
                "week" => "r604800",
                "month" => "r2592000",
                _ => null
            };
            if (postedSeconds != null) parameters.Add("f_TPR=" + postedSeconds);

            var workplace = query.WorkplaceType?.ToLowerInvariant() switch
            {
                "onsite" => "1",
                "remote" => "2",
                "hybrid" => "3",
                _ => null
            };
            if (workplace != null) parameters.Add("f_WT=" + workplace);

            var jobType = query.JobType?.ToLowerInvariant() switch
            {
                "fulltime" => "F",
                "parttime" => "P",
                "contract" => "C",
                "internship" => "I",
                _ => null
            };
            if (jobType != null) parameters.Add("f_JT=" + jobType);

            if (query.EasyApplyOnly) parameters.Add("f_AL=true");
            if (query.SortByDate) parameters.Add("sortBy=DD");

            return GuestSearchUrl + "?" + string.Join("&", parameters);
        }

        // --- HTML ayrıştırma -------------------------------------------------
        // Uç nokta tam sayfa değil, <li> kartlarından oluşan bir HTML parçası döndürüyor.
        // Ek bağımlılık getirmemek için düzenli ifadelerle ayrıştırıyoruz; kart yapısı
        // değişirse güncellenmesi gereken yer burası.

        private static readonly Regex JobIdRegex =
            new(@"urn:li:jobPosting:(\d+)", RegexOptions.Compiled);

        private static readonly Regex LinkRegex =
            new(@"href=""(https://[a-z]{0,3}\.?linkedin\.com/jobs/view/[^""]+)""",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex TitleRegex =
            new(@"<h3[^>]*base-search-card__title[^>]*>(.*?)</h3>",
                RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex CompanyRegex =
            new(@"<h4[^>]*base-search-card__subtitle[^>]*>(.*?)</h4>",
                RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex LocationRegex =
            new(@"<span[^>]*job-search-card__location[^>]*>(.*?)</span>",
                RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex DateRegex =
            new(@"<time[^>]*datetime=""([^""]+)""",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex SalaryRegex =
            new(@"<span[^>]*job-search-card__salary-info[^>]*>(.*?)</span>",
                RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

        public static List<JobListing> ParseJobCards(string html, string searchName)
        {
            var jobs = new List<JobListing>();
            if (string.IsNullOrWhiteSpace(html)) return jobs;

            // Kartlar <li> ile ayrılıyor; ilk parça genelde boş başlık kısmı.
            var chunks = Regex.Split(html, "<li[ >]", RegexOptions.IgnoreCase);

            foreach (var chunk in chunks)
            {
                var idMatch = JobIdRegex.Match(chunk);
                var linkMatch = LinkRegex.Match(chunk);
                var titleMatch = TitleRegex.Match(chunk);

                // Kimlik ve başlık yoksa bu bir ilan kartı değil.
                if (!idMatch.Success || !titleMatch.Success) continue;

                var jobId = idMatch.Groups[1].Value;

                var url = linkMatch.Success
                    ? CleanUrl(linkMatch.Groups[1].Value)
                    : $"https://www.linkedin.com/jobs/view/{jobId}/";

                var job = new JobListing
                {
                    JobId = jobId,
                    Title = CleanText(titleMatch.Groups[1].Value),
                    Company = CleanText(CompanyRegex.Match(chunk).Groups[1].Value),
                    Location = CleanText(LocationRegex.Match(chunk).Groups[1].Value),
                    Url = url,
                    SearchName = searchName
                };

                var dateMatch = DateRegex.Match(chunk);
                if (dateMatch.Success &&
                    DateTime.TryParse(dateMatch.Groups[1].Value, out var posted))
                {
                    job.PostedDate = posted;
                }

                var salaryMatch = SalaryRegex.Match(chunk);
                if (salaryMatch.Success)
                {
                    var salary = CleanText(salaryMatch.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(salary)) job.SalaryInfo = salary;
                }

                if (!string.IsNullOrWhiteSpace(job.Title)) jobs.Add(job);
            }

            return jobs;
        }

        /// <summary>İlan linkindeki takip parametrelerini atar.</summary>
        private static string CleanUrl(string url)
        {
            var decoded = WebUtility.HtmlDecode(url);
            var questionMark = decoded.IndexOf('?');
            return questionMark > 0 ? decoded[..questionMark] : decoded;
        }

        /// <summary>HTML etiketlerini temizler, entity'leri çözer, fazla boşlukları toplar.</summary>
        public static string CleanText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var withoutTags = TagRegex.Replace(raw, " ");
            var decoded = WebUtility.HtmlDecode(withoutTags);
            return WhitespaceRegex.Replace(decoded, " ").Trim();
        }

        public void Dispose()
        {
            _http.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
