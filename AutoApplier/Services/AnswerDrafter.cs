using AutoApplier.Models;

namespace AutoApplier.Services
{
    /// <summary>
    /// Serbest metin sorularına taslak cevap üretir: ilan metni + adayın profili + soru,
    /// yerel modele gider. Ürettiği metin doğrudan forma yazılmaz — onayı asistan ister.
    ///
    /// Modelin uydurma riski bu sınıfın tasarımını belirliyor: istem, yalnızca verilen
    /// bilgilerle cevap vermesini söylüyor ve cevap her zaman kullanıcıya gösteriliyor.
    /// </summary>
    public class AnswerDrafter : IDisposable
    {
        private readonly AiConfig _config;
        private readonly OllamaClient _client;
        private readonly JobDescriptionFetcher _fetcher = new();

        public AnswerDrafter(AiConfig config)
        {
            _config = config;
            _client = new OllamaClient(config);
        }

        public bool Enabled => _config.Enabled;

        public string Model => _config.Model;

        public async Task<(string? Text, string? Error)> DraftAsync(
            JobListing job, ResolvedProfile profile, string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return (null, "Sorunun metni okunamadı.");

            // İlan metni bir kez çekilir, sonra jobs.json'da saklanır.
            if (string.IsNullOrWhiteSpace(job.Description))
            {
                job.Description = await _fetcher.FetchAsync(job.Url);
            }

            var turkish = IsTurkish(question);
            var language = turkish ? "Türkçe" : "İngilizce";

            var system =
                "Sen bir iş başvurusu asistanısın. Adayın bilgilerini ve ilan metnini kullanarak " +
                "başvuru formundaki soruya cevap yazarsın. Kurallar: " +
                "(1) Yalnızca sana verilen bilgileri kullan, adaya ait olmayan deneyim, teknoloji ya da " +
                "rakam UYDURMA. (2) Cevabın doğrudan forma yazılacak; selamlama, imza, başlık ya da " +
                "\"işte cevabınız\" gibi açıklama ekleme. (3) Kısa bir soruya 1-2 cümle, ön yazı türü " +
                "bir soruya tek paragraf yaz. (4) Cevabı " + language + " yaz.";

            var user =
                $"POZİSYON: {job.Title}\n" +
                $"ŞİRKET: {job.Company}\n\n" +
                $"İLAN METNİ:\n{Shorten(job.Description, _config.MaxDescriptionChars)}\n\n" +
                $"ADAYIN BİLGİLERİ:\n{ProfileSummary(profile)}\n\n" +
                $"FORMDAKİ SORU:\n{question}";

            return await _client.AskAsync(system, user);
        }

        /// <summary>Modele verilecek aday özeti — profildeki kanonik cevaplardan derleniyor.</summary>
        private static string ProfileSummary(ResolvedProfile profile)
        {
            var lines = new List<string>();

            void Add(string label, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value)) lines.Add($"- {label}: {value}");
            }

            Add("Ad", profile.Get(AnswerKeys.FullName));
            Add("Unvan", profile.Get(AnswerKeys.CurrentTitle));
            Add("Şirket", profile.Get(AnswerKeys.CurrentCompany));
            Add("Deneyim (yıl)", profile.Get(AnswerKeys.YearsOfExperience));
            Add("Yetenekler", profile.Get(AnswerKeys.Skills));
            Add("Eğitim", profile.Get(AnswerKeys.Degree) + " " + profile.Get(AnswerKeys.Major));
            Add("Üniversite", profile.Get(AnswerKeys.University));
            Add("Diller", profile.Get(AnswerKeys.Languages));
            Add("Özet", profile.Get(AnswerKeys.Summary));

            return string.Join("\n", lines);
        }

        /// <summary>Soruda Türkçeye özgü harf varsa cevap da Türkçe olsun.</summary>
        private static bool IsTurkish(string question) =>
            question.Any(c => "çğıöşüÇĞİÖŞÜ".Contains(c));

        private static string Shorten(string? text, int max)
        {
            if (string.IsNullOrWhiteSpace(text)) return "(ilan metni alınamadı)";
            return text.Length <= max ? text : text[..max] + "...";
        }

        public void Dispose()
        {
            _client.Dispose();
            _fetcher.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
