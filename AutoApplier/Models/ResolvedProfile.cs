namespace AutoApplier.Models
{
    /// <summary>
    /// Form doldurucunun kullandığı kanonik alan adları. Her biri bir "bilgi türünü" temsil eder;
    /// FieldRules bu anahtarları sitedeki gerçek form etiketleriyle eşleştirir.
    /// </summary>
    public static class AnswerKeys
    {
        public const string FirstName = "firstName";
        public const string LastName = "lastName";
        public const string FullName = "fullName";
        public const string Email = "email";
        public const string Phone = "phone";
        public const string Address = "address";
        public const string City = "city";
        public const string State = "state";
        public const string Country = "country";
        public const string PostalCode = "postalCode";

        public const string LinkedIn = "linkedin";
        public const string GitHub = "github";
        public const string Website = "website";

        public const string CurrentCompany = "currentCompany";
        public const string CurrentTitle = "currentTitle";
        public const string YearsOfExperience = "yearsOfExperience";
        public const string ExpectedSalary = "expectedSalary";
        public const string NoticePeriod = "noticePeriod";
        public const string StartDate = "startDate";

        public const string University = "university";
        public const string Degree = "degree";
        public const string Major = "major";
        public const string GraduationYear = "graduationYear";

        public const string Languages = "languages";
        public const string Skills = "skills";
        public const string Summary = "summary";
        public const string CoverLetter = "coverLetter";

        public const string WorkAuthorization = "workAuthorization";
        public const string Sponsorship = "sponsorship";
        public const string Relocation = "relocation";
        public const string Remote = "remote";
        public const string HowDidYouHear = "howDidYouHear";
        public const string Eeo = "eeo";
    }

    /// <summary>
    /// Bir ilan için çözülmüş cevap seti: ortak kişisel bilgiler + o pozisyona ait profil,
    /// tek bir sözlükte birleştirilmiş hali.
    /// </summary>
    public class ResolvedProfile
    {
        public string ProfileName { get; init; } = "";

        /// <summary>
        /// true: profil ilan başlığıyla gerçekten eşleşti. false: hiçbir anahtar kelime tutmadı,
        /// varsayılan profile düşüldü — bu durumda CV ve ön yazı ilana uygun olmayabilir.
        /// </summary>
        public bool MatchedByKeyword { get; init; }

        /// <summary>Bu pozisyon için yüklenecek CV. Boşsa dosya yükleme adımı atlanır.</summary>
        public string ResumePath { get; init; } = "";

        public JobListing Job { get; init; } = new();

        /// <summary>Kanonik alan adı → cevap.</summary>
        public Dictionary<string, string> Answers { get; init; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Profile özel serbest soru cevapları (etiketin içinde geçen ifade → cevap).</summary>
        public Dictionary<string, string> ExtraAnswers { get; init; } =
            new(StringComparer.OrdinalIgnoreCase);

        public string? Get(string key) =>
            Answers.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }
}
