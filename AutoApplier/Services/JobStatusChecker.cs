using System.Net;

namespace AutoApplier.Services
{
    /// <summary>
    /// İlanın hâlâ başvuruya açık olup olmadığını LinkedIn'in ilan sayfasından kontrol eder.
    ///
    /// Kuyrukta iki haftalık ilanlar birikiyor ve bir kısmı çoktan kapanmış oluyor; kapalı
    /// ilanı açıp başvurmaya çalışmak boşa zaman. LinkedIn kapanan ilanlarda sayfaya
    /// "closed-job" bloğunu koyuyor — sınıf adı üzerinden bakıyoruz, çünkü görünen metin
    /// sayfanın diline göre değişiyor ("Artık başvuru kabul etmiyor" / "No longer accepting").
    ///
    /// Tarayıcı sürmüyor: düz HTTP isteği yeterli ve çok daha hızlı.
    /// </summary>
    public class JobStatusChecker : IDisposable
    {
        private const string ClosedMarker = "closed-job__flavor--closed";

        private readonly HttpClient _http;
        private readonly int _delayMs;

        public JobStatusChecker(int delayBetweenRequestsMs = 1500)
        {
            _delayMs = Math.Max(500, delayBetweenRequestsMs);

            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };

            _http.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.Add("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");
        }

        /// <summary>Sayfa metninde kapalı ilan işareti var mı.</summary>
        public static bool LooksClosed(string? pageSource) =>
            pageSource != null && pageSource.Contains(ClosedMarker, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// true: ilan kapanmış. false: açık. null: karar verilemedi (ağ hatası, 404, hız sınırı).
        /// Kararsız kalınca ilana dokunulmuyor — emin olmadan eleme yapmak, açık ilanı kaybettirir.
        /// </summary>
        public async Task<bool?> IsClosedAsync(string url)
        {
            try
            {
                using var response = await _http.GetAsync(url);

                if (response.StatusCode == HttpStatusCode.TooManyRequests) return null;
                if (!response.IsSuccessStatusCode) return null;

                var html = await response.Content.ReadAsStringAsync();
                return LooksClosed(html);
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                await Task.Delay(_delayMs);
            }
        }

        public void Dispose()
        {
            _http.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
