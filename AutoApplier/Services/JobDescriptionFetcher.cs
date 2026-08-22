using System.Net;
using System.Text.RegularExpressions;

namespace AutoApplier.Services
{
    /// <summary>
    /// İlan açıklamasını girişsiz ilan sayfasından çeker. Arama uç noktası yalnızca kart
    /// bilgisi (başlık, şirket, tarih) veriyor; yapay zekânın işe yarar bir cevap üretmesi
    /// için ilanın kendi metni gerekiyor.
    ///
    /// Açıklama bir kez çekilip jobs.json'a yazılıyor, her seferinde indirilmiyor.
    /// </summary>
    public class JobDescriptionFetcher : IDisposable
    {
        private static readonly Regex DescriptionRegex =
            new("show-more-less-html__markup[^>]*>(.*?)</div>",
                RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private readonly HttpClient _http;

        public JobDescriptionFetcher()
        {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };

            _http.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");
        }

        /// <summary>İlan metni; bulunamazsa null.</summary>
        public async Task<string?> FetchAsync(string url)
        {
            try
            {
                using var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;

                var html = await response.Content.ReadAsStringAsync();
                var match = DescriptionRegex.Match(html);

                if (!match.Success) return null;

                var text = JobSearchService.CleanText(match.Groups[1].Value);
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Dispose()
        {
            _http.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
