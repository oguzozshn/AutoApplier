using System.Text;
using AutoApplier.Models;

namespace AutoApplier.Services
{
    /// <summary>Bir form alanının etiketini kanonik bir cevap anahtarına bağlayan kural.</summary>
    public record FieldRule(string AnswerKey, string[] Keywords, string[]? Excludes = null);

    /// <summary>
    /// Form alanı etiketlerini (label, aria-label, name, id, placeholder...) hangi bilgiyle
    /// dolduracağımıza çeviren kural tablosu.
    ///
    /// Eşleştirme sıraya değil, en uzun eşleşen anahtar kelimeye göre yapılır (bkz. MatchKey).
    /// Yani "last name" kuralı, "name" kuralı listede nerede olursa olsun kazanır.
    /// Kısa ve genel bir kelimenin (ör. "address") yanlış alana bulaşmasını engellemek için
    /// kuralın Excludes listesini kullan.
    /// </summary>
    public static class FieldRules
    {
        public static readonly IReadOnlyList<FieldRule> Rules = new List<FieldRule>
        {
            // --- İsim: en spesifikten genele ---
            new(AnswerKeys.FirstName, new[] { "first name", "firstname", "given name", "forename", "ad ", "adiniz", "isim" }),
            new(AnswerKeys.LastName,  new[] { "last name", "lastname", "surname", "family name", "soyad" }),
            new(AnswerKeys.FullName,  new[] { "full name", "fullname", "your name", "ad soyad", "isim soyisim" }),

            // --- İletişim ---
            new(AnswerKeys.Email, new[] { "email", "e-mail", "eposta", "e-posta" }),
            new(AnswerKeys.Phone, new[] { "phone", "mobile", "telephone", "telefon", "cep", "gsm" }),

            // Posta kodu "address" kuralından önce gelmeli.
            new(AnswerKeys.PostalCode, new[] { "postal code", "postcode", "zip", "posta kodu" }),
            // "location" burada güvenli: "relocation" daha uzun eşleştiği için taşınma
            // sorusunu çalmıyor (bkz. MatchKey'in en-uzun-eşleşme kuralı).
            new(AnswerKeys.City,       new[] { "city", "town", "location", "sehir", "ilce" }),
            new(AnswerKeys.State,      new[] { "state", "province", "region", "il " }),
            new(AnswerKeys.Country,    new[] { "country", "ulke" }),
            // "email address" ve "linkedin address" bu kurala düşmesin.
            new(AnswerKeys.Address, new[] { "address", "street", "adres" },
                Excludes: new[] { "email", "e-mail", "eposta", "linkedin", "url", "web" }),

            // --- Linkler: website kuralından önce ---
            new(AnswerKeys.LinkedIn, new[] { "linkedin" }),
            new(AnswerKeys.GitHub,   new[] { "github", "git hub" }),
            new(AnswerKeys.Website,  new[] { "portfolio", "website", "personal site", "web site", "blog", "url" }),

            // --- Deneyim: "years of experience" genel "experience"tan önce ---
            new(AnswerKeys.YearsOfExperience, new[]
            {
                "years of experience", "years experience", "yrs of experience",
                "how many years", "total experience", "deneyim yili", "kac yil"
            }),
            new(AnswerKeys.ExpectedSalary, new[]
            {
                "salary", "compensation", "expected pay", "desired pay", "rate expectation",
                "maas", "ucret"
            }),
            new(AnswerKeys.NoticePeriod, new[] { "notice period", "notice", "ihbar" }),
            new(AnswerKeys.StartDate, new[]
            {
                "start date", "available to start", "availability", "when can you start", "baslangic"
            }),
            new(AnswerKeys.CurrentTitle, new[]
            {
                "current title", "current position", "job title", "your title", "current role", "unvan"
            }),
            new(AnswerKeys.CurrentCompany, new[]
            {
                "current company", "current employer", "employer", "company name", "mevcut sirket"
            }),

            // --- Eğitim ---
            new(AnswerKeys.University, new[] { "school", "university", "college", "institution", "universite", "okul" }),
            new(AnswerKeys.Degree,     new[] { "degree", "education level", "egitim" }),
            new(AnswerKeys.Major,      new[] { "major", "field of study", "discipline", "bolum" }),
            new(AnswerKeys.GraduationYear, new[]
            {
                "graduation", "year of graduation", "end date", "mezuniyet"
            }),

            // --- Uzun metinler ---
            new(AnswerKeys.CoverLetter, new[]
            {
                "cover letter", "motivation", "why do you want", "tell us about yourself",
                "additional information", "on yazi", "niye", "neden"
            }),
            new(AnswerKeys.Summary,   new[] { "summary", "about you", "bio", "ozet" }),
            new(AnswerKeys.Skills,    new[] { "skills", "technologies", "yetenek", "beceri" }),
            new(AnswerKeys.Languages, new[] { "languages", "language proficiency", "dil" }),

            // --- Evet/hayır soruları ---
            // Sponsorluk, çalışma izninden önce: "will you require sponsorship" ikisini de içerebiliyor.
            new(AnswerKeys.Sponsorship, new[]
            {
                "sponsorship", "sponsor", "visa", "require support", "vize"
            }),
            new(AnswerKeys.WorkAuthorization, new[]
            {
                "authorized to work", "work authorization", "legally authorized",
                "right to work", "eligible to work", "calisma izni"
            }),
            new(AnswerKeys.Relocation, new[] { "relocate", "relocation", "tasin" }),
            new(AnswerKeys.Remote,     new[] { "remote", "work from home", "hybrid", "uzaktan" }),

            // --- Diğer ---
            new(AnswerKeys.HowDidYouHear, new[]
            {
                "how did you hear", "referral source", "where did you find", "source", "nereden duydunuz"
            }),
            new(AnswerKeys.Eeo, new[]
            {
                "gender", "race", "ethnicity", "hispanic", "veteran", "disability",
                "self-identify", "self identification", "cinsiyet", "engel"
            })
        };

        /// <summary>
        /// Etikete uyan kuralı bulur. İlk eşleşeni değil, **en uzun anahtar kelimeyle** eşleşeni seçer.
        ///
        /// Bu önemli: form etiketleri çoğu zaman tam cümle oluyor ve kısa genel kelimeler cümlenin
        /// içinde kazara geçiyor. "Are you legally authorized to work in this country?" sorusunda
        /// hem "country" (Ülke) hem "legally authorized" (çalışma izni) tutuyor; uzun olan doğrudur.
        /// Sıraya güvenmek bu tür çakışmalarda sessizce yanlış alanı doldururdu.
        /// </summary>
        public static string? MatchKey(string normalizedLabel)
        {
            if (string.IsNullOrWhiteSpace(normalizedLabel)) return null;

            string? bestKey = null;
            var bestLength = 0;

            foreach (var rule in Rules)
            {
                if (rule.Excludes != null &&
                    rule.Excludes.Any(ex => normalizedLabel.Contains(ex, StringComparison.Ordinal)))
                {
                    continue;
                }

                foreach (var keyword in rule.Keywords)
                {
                    if (keyword.Length <= bestLength) continue;
                    if (!normalizedLabel.Contains(keyword, StringComparison.Ordinal)) continue;

                    bestKey = rule.AnswerKey;
                    bestLength = keyword.Length;
                }
            }

            return bestKey;
        }

        /// <summary>
        /// Karşılaştırmayı güvenilir kılmak için metni sadeleştirir: küçük harf, Türkçe karakterler
        /// ASCII karşılığına, noktalama boşluğa, tekrarlı boşluklar teke.
        /// Böylece kural listesini ASCII yazabiliyoruz ve "Şehir" ile "sehir" eşleşiyor.
        /// </summary>
        public static string Normalize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            var sb = new StringBuilder(raw.Length + 2);

            // Baştaki boşluk, "ad " gibi sonu boşluklu kuralların kelime başında da eşleşmesini sağlar.
            sb.Append(' ');

            foreach (var ch in raw.ToLowerInvariant())
            {
                var mapped = ch switch
                {
                    'ı' or 'i' or 'î' => 'i',
                    'ş' => 's',
                    'ğ' => 'g',
                    'ü' or 'û' => 'u',
                    'ö' => 'o',
                    'ç' => 'c',
                    'â' => 'a',
                    _ => ch
                };

                if (char.IsLetterOrDigit(mapped))
                {
                    sb.Append(mapped);
                }
                else if (sb.Length > 0 && sb[^1] != ' ')
                {
                    sb.Append(' ');
                }
            }

            sb.Append(' ');
            return sb.ToString();
        }

        /// <summary>"Yes/No" tipi cevapları tanır.</summary>
        public static bool IsAffirmative(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var v = Normalize(value).Trim();
            return v is "yes" or "y" or "true" or "evet" or "1";
        }

        public static bool IsNegative(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var v = Normalize(value).Trim();
            return v is "no" or "n" or "false" or "hayir" or "0";
        }
    }
}
