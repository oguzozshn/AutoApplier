using AutoApplier.Models;

namespace AutoApplier.Services
{
    /// <summary>
    /// İlana göre doğru profili seçer ve kişisel bilgilerle birleştirip doldurulacak cevap setini üretir.
    /// "Başvurduğum pozisyona göre bilgiler değişsin" isteğinin karşılığı burasıdır.
    /// </summary>
    public static class ProfileMatcher
    {
        /// <summary>
        /// İlan başlığı ve şirket adına bakarak en iyi eşleşen profili bulur.
        /// Eşleşme yoksa DefaultProfile, o da yoksa listedeki ilk profil kullanılır.
        /// </summary>
        public static PositionProfile? SelectProfile(ProfileConfig config, JobListing job, out bool matchedByKeyword)
        {
            matchedByKeyword = false;
            if (config.Profiles.Count == 0) return null;

            var haystack = $"{job.Title} {job.Company}".ToLowerInvariant();
            var location = job.Location.ToLowerInvariant();

            PositionProfile? best = null;
            var bestScore = 0;

            foreach (var profile in config.Profiles)
            {
                var score = 0;

                foreach (var keyword in profile.MatchKeywords)
                {
                    if (string.IsNullOrWhiteSpace(keyword)) continue;

                    if (haystack.Contains(keyword.Trim().ToLowerInvariant()))
                    {
                        // Uzun anahtar kelime daha spesifiktir: "asp.net" > "api".
                        score += keyword.Trim().Length;
                    }
                }

                foreach (var place in profile.MatchLocations)
                {
                    if (string.IsNullOrWhiteSpace(place)) continue;

                    if (location.Contains(place.Trim().ToLowerInvariant()))
                    {
                        // Konum, başlıktan ağır basmalı: aynı teknolojinin yurtdışındaki ilanı
                        // yerel profille aynı puanı alırsa yanlış (sponsorluk) cevabı gider.
                        score += place.Trim().Length * 3;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = profile;
                }
            }

            if (best != null)
            {
                matchedByKeyword = true;
                return best;
            }

            var fallback = config.Profiles
                .FirstOrDefault(p => p.Name.Equals(config.DefaultProfile, StringComparison.OrdinalIgnoreCase));

            return fallback ?? config.Profiles[0];
        }

        /// <summary>Seçilen profili kişisel bilgilerle birleştirip kanonik cevap sözlüğünü kurar.</summary>
        public static ResolvedProfile Resolve(ProfileConfig config, JobListing job)
        {
            var profile = SelectProfile(config, job, out var matchedByKeyword);
            var personal = config.Personal;

            var answers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [AnswerKeys.FirstName] = personal.FirstName,
                [AnswerKeys.LastName] = personal.LastName,
                [AnswerKeys.FullName] = personal.FullName,
                [AnswerKeys.Email] = personal.Email,
                [AnswerKeys.Phone] = personal.Phone,
                [AnswerKeys.Address] = personal.Address,
                [AnswerKeys.City] = personal.City,
                [AnswerKeys.State] = personal.State,
                [AnswerKeys.Country] = personal.Country,
                [AnswerKeys.PostalCode] = personal.PostalCode,
                [AnswerKeys.LinkedIn] = personal.LinkedInUrl,
                [AnswerKeys.GitHub] = personal.GitHubUrl,
                [AnswerKeys.Website] = string.IsNullOrWhiteSpace(personal.PortfolioUrl)
                    ? personal.GitHubUrl
                    : personal.PortfolioUrl,
                [AnswerKeys.University] = personal.University,
                [AnswerKeys.Degree] = personal.Degree,
                [AnswerKeys.Major] = personal.Major,
                [AnswerKeys.GraduationYear] = personal.GraduationYear,
                [AnswerKeys.Languages] = personal.Languages,
                [AnswerKeys.WorkAuthorization] = personal.WorkAuthorization,
                [AnswerKeys.Sponsorship] = personal.RequiresSponsorship,
                [AnswerKeys.Relocation] = personal.WillingToRelocate,
                [AnswerKeys.Remote] = personal.OpenToRemote,
                [AnswerKeys.HowDidYouHear] = "LinkedIn",
                [AnswerKeys.Eeo] = config.EeoDefaultAnswer
            };

            if (profile != null)
            {
                // Pozisyona özel alanlar ortak bilgilerin üzerine yazılır.
                Set(answers, AnswerKeys.CurrentTitle, profile.CurrentTitle);
                Set(answers, AnswerKeys.CurrentCompany, profile.CurrentCompany);
                Set(answers, AnswerKeys.YearsOfExperience, profile.YearsOfExperience);
                Set(answers, AnswerKeys.ExpectedSalary, profile.ExpectedSalary);
                Set(answers, AnswerKeys.NoticePeriod, profile.NoticePeriod);
                Set(answers, AnswerKeys.StartDate, profile.StartDate);
                // Ülkeye bağlı cevaplar: yurtdışı profillerinde sponsorluk cevabı değişiyor.
                Set(answers, AnswerKeys.WorkAuthorization, profile.WorkAuthorization);
                Set(answers, AnswerKeys.Sponsorship, profile.RequiresSponsorship);

                Set(answers, AnswerKeys.Summary, FillPlaceholders(profile.Summary, job, personal));
                Set(answers, AnswerKeys.CoverLetter, FillPlaceholders(profile.CoverLetter, job, personal));

                if (profile.Skills.Count > 0)
                {
                    Set(answers, AnswerKeys.Skills, string.Join(", ", profile.Skills));
                }
            }

            var extras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (profile != null)
            {
                foreach (var pair in profile.ExtraAnswers)
                {
                    extras[pair.Key] = FillPlaceholders(pair.Value, job, personal);
                }
            }

            return new ResolvedProfile
            {
                ProfileName = profile?.Name ?? "(profil yok)",
                MatchedByKeyword = matchedByKeyword,
                ResumePath = profile?.ResumePath ?? "",
                Job = job,
                Answers = answers,
                ExtraAnswers = extras
            };
        }

        private static void Set(IDictionary<string, string> answers, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) answers[key] = value;
        }

        /// <summary>Ön yazıdaki {position}, {company} ve {name} yer tutucularını doldurur.</summary>
        private static string FillPlaceholders(string template, JobListing job, PersonalInfo personal)
        {
            if (string.IsNullOrWhiteSpace(template)) return "";

            return template
                .Replace("{position}", job.Title, StringComparison.OrdinalIgnoreCase)
                .Replace("{company}", job.Company, StringComparison.OrdinalIgnoreCase)
                .Replace("{name}", personal.FullName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
