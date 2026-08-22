namespace AutoApplier.Models
{
    /// <summary>
    /// Öncelikli şirketler. LinkedIn'in giriş gerektirmeyen uç noktası şirket büyüklüğü
    /// vermiyor — elde yalnızca şirket adı var — o yüzden "büyük firma" tanımı elle
    /// tutulan bir listeye dayanıyor.
    ///
    /// Eşleşme ada göre ve içerik bazlı: "TEB" girdisi "TEB Arf"ı da yakalar.
    /// </summary>
    public class CompanyConfig
    {
        public List<string> Preferred { get; set; } = new();

        public bool Matches(string company)
        {
            if (string.IsNullOrWhiteSpace(company)) return false;

            var haystack = company.ToLowerInvariant();

            return Preferred.Any(name =>
                !string.IsNullOrWhiteSpace(name) &&
                haystack.Contains(name.Trim().ToLowerInvariant()));
        }

        public static CompanyConfig CreateDefault() => new()
        {
            Preferred = new List<string>
            {
                // Teknoloji / ürün
                "Trendyol", "Hepsiburada", "Getir", "Peak", "Dream Games", "Insider",
                "Param", "iyzico", "Papara", "Martı", "Marti", "invent.ai", "Sezzle",

                // Bankacılık ve finans teknolojisi
                "Akbank", "Garanti BBVA", "TEB", "Burgan", "ICBC", "Colendi",
                "Mobven", "Token Finansal", "Yapı Kredi", "İş Bankası", "Ziraat",

                // Kurumsal ve sanayi
                "Siemens", "Ericsson", "Turkcell", "Türk Telekom", "Aselsan",
                "TUSAŞ", "TEI", "Arçelik", "Beko", "Koç", "LC Waikiki", "Migros",
                "Sürat Kargo", "Vodafone", "Borusan", "Sabancı",

                // Danışmanlık / servis
                "Accenture", "EPAM", "OBSS", "Innova", "Kartaca", "Amadeus", "Turknet"
            }
        };
    }
}
