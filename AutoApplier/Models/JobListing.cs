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

        /// <summary>Başvuru asistanı bu ilanı işledi mi.</summary>
        public bool Processed { get; set; }

        [JsonIgnore]
        public string PostedDisplay => PostedDate?.ToString("yyyy-MM-dd") ?? "";

        public override string ToString() => $"{Title} — {Company} ({Location})";
    }
}
