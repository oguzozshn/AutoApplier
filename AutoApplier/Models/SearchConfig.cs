namespace AutoApplier.Models
{
    /// <summary>
    /// searches.json dosyasının kök yapısı. Birden fazla arama tanımlayıp hepsini tek seferde çalıştırabilirsin.
    /// </summary>
    public class SearchConfig
    {
        public List<SearchQuery> Searches { get; set; } = new();

        /// <summary>İstekler arasında beklenecek süre (ms). Düşürme — LinkedIn hız sınırına takılırsın.</summary>
        public int DelayBetweenRequestsMs { get; set; } = 2500;

        public static SearchConfig CreateDefault() => new()
        {
            Searches = new List<SearchQuery>
            {
                new()
                {
                    Name = "Backend .NET",
                    Keywords = "backend developer .NET",
                    Location = "Türkiye",
                    MaxResults = 100,
                    PostedWithin = "week",
                    WorkplaceType = "any",
                    EasyApplyOnly = false,
                    SortByDate = true
                },
                new()
                {
                    Name = "Remote Yazılım",
                    Keywords = "software engineer",
                    Location = "Türkiye",
                    MaxResults = 50,
                    PostedWithin = "week",
                    WorkplaceType = "remote",
                    EasyApplyOnly = false,
                    SortByDate = true
                }
            }
        };
    }

    /// <summary>Tek bir arama tanımı. LinkedIn'in arama filtrelerinin karşılığı.</summary>
    public class SearchQuery
    {
        /// <summary>Bu aramaya verdiğin isim. Çıktıda hangi ilanın hangi aramadan geldiğini görürsün.</summary>
        public string Name { get; set; } = "Arama";

        /// <summary>Aranacak kelimeler. LinkedIn arama kutusuna yazdığın şey.</summary>
        public string Keywords { get; set; } = "";

        /// <summary>Konum, ör. "Türkiye", "İstanbul, Türkiye", "Remote".</summary>
        public string Location { get; set; } = "Türkiye";

        /// <summary>Kaç ilan çekilsin. LinkedIn guest uç noktası pratikte ~1000'de kesiyor.</summary>
        public int MaxResults { get; set; } = 100;

        /// <summary>"day" | "week" | "month" | "any"</summary>
        public string PostedWithin { get; set; } = "week";

        /// <summary>"onsite" | "remote" | "hybrid" | "any"</summary>
        public string WorkplaceType { get; set; } = "any";

        /// <summary>"fulltime" | "parttime" | "contract" | "internship" | "any"</summary>
        public string JobType { get; set; } = "any";

        /// <summary>Sadece LinkedIn Kolay Başvuru olan ilanlar.</summary>
        public bool EasyApplyOnly { get; set; }

        /// <summary>true: en yeniden eskiye. false: LinkedIn'in alaka sıralaması.</summary>
        public bool SortByDate { get; set; } = true;

        /// <summary>Başlığında bu kelimelerden biri geçen ilanlar elenir (ör. "senior", "staj").</summary>
        public List<string> ExcludeTitleKeywords { get; set; } = new();
    }
}
