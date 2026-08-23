using System.Text.Json.Serialization;

namespace AutoApplier.Models
{
    /// <summary>
    /// LinkedIn arama sonucundan çekilen tek bir ilan.
    /// </summary>
    public class JobListing
    {
        /// <summary>LinkedIn'in ilan kimliği (urn:li:jobPosting:XXXX içindeki sayı). Tekrarları elemek için kullanılır.</summary>
        public string JobId { get; set; } = "";

        public string Title { get; set; } = "";
        public string Company { get; set; } = "";
        public string Location { get; set; } = "";

        /// <summary>İlanın doğrudan linki. Tarayıcıda açılıp başvurulabilir.</summary>
        public string Url { get; set; } = "";

        /// <summary>İlanın yayın tarihi (LinkedIn'in verdiği datetime alanı).</summary>
        public DateTime? PostedDate { get; set; }

        /// <summary>İlanda maaş bilgisi varsa.</summary>
        public string? SalaryInfo { get; set; }

        /// <summary>Bu ilanın hangi aramadan geldiği — birden fazla arama çalıştırıldığında ayırt etmek için.</summary>
        public string SearchName { get; set; } = "";

        public DateTime FetchedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// İlan metni. Arama uç noktası vermiyor; yapay zekâya soru sorulacağı zaman
        /// ilan sayfasından bir kez çekilip burada saklanıyor.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// İlanın hâlâ açık olup olmadığına en son ne zaman bakıldı. Boşsa hiç bakılmamış.
        /// Bu olmadan her tarama, daha önce açık çıkmış ilanları baştan sorguluyordu.
        /// </summary>
        public DateTime? LastCheckedAt { get; set; }

        /// <summary>
        /// Neden elendi: "kapandı" (LinkedIn başvuru kabul etmiyor) ya da "ilgilenmiyorum".
        /// İkisi de kuyruktan çıkarır ama sebebi bilmek dışa aktarımı okurken işe yarıyor.
        /// </summary>
        public string? DismissedReason { get; set; }

        /// <summary>Başvuru asistanı bu ilanı işledi mi (başvuruldu).</summary>
        public bool Processed { get; set; }

        /// <summary>
        /// İlana hiç başvurulmayacak — pozisyon alakasız, seviye uymuyor vb.
        /// Başvurulmuş olmaktan ayrı tutuluyor: "neye başvurdum" sorusunun cevabı bozulmasın,
        /// ama elenen ilan bir daha kuyruğa girmesin.
        /// </summary>
        public bool Dismissed { get; set; }

        [JsonIgnore]
        public string PostedDisplay => PostedDate?.ToString("yyyy-MM-dd") ?? "";

        public override string ToString() => $"{Title} — {Company} ({Location})";
    }
}
