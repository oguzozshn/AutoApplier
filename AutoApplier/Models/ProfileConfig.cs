namespace AutoApplier.Models
{
    /// <summary>
    /// profiles.json'un kök yapısı. Her başvuruda aynı kalan kişisel bilgiler bir kez yazılır;
    /// pozisyona göre değişen her şey (CV, unvan, deneyim, maaş, ön yazı) ayrı profillerde tutulur.
    /// </summary>
    public class ProfileConfig
    {
        public PersonalInfo Personal { get; set; } = new();

        public List<PositionProfile> Profiles { get; set; } = new();

        /// <summary>Hiçbir profil ilanla eşleşmezse kullanılacak profilin adı.</summary>
        public string DefaultProfile { get; set; } = "";

        /// <summary>
        /// Ayrımcılık karşıtı (EEO) sorularında varsayılan cevap. ABD merkezli sistemlerde
        /// cinsiyet/etnik köken/gazilik/engellilik soruları için kullanılır.
        /// </summary>
        public string EeoDefaultAnswer { get; set; } = "Decline to self-identify";

        public static ProfileConfig CreateDefault() => new()
        {
            Personal = new PersonalInfo
            {
                FirstName = "Adınız",
                LastName = "Soyadınız",
                Email = "email@example.com",
                Phone = "+90 555 123 45 67",
                Address = "Örnek Mah. Örnek Sk. No:1",
                City = "İstanbul",
                State = "İstanbul",
                Country = "Türkiye",
                PostalCode = "34000",
                LinkedInUrl = "https://www.linkedin.com/in/kullaniciadi",
                GitHubUrl = "https://github.com/kullaniciadi",
                PortfolioUrl = "",
                University = "İstanbul Teknik Üniversitesi",
                Degree = "Bachelor's Degree",
                Major = "Bilgisayar Mühendisliği",
                GraduationYear = "2023",
                Languages = "Turkish (Native), English (Fluent)",
                WorkAuthorization = "Yes",
                RequiresSponsorship = "No",
                WillingToRelocate = "Yes",
                OpenToRemote = "Yes"
            },
            DefaultProfile = "Backend .NET",
            Profiles = new List<PositionProfile>
            {
                new()
                {
                    Name = "Backend .NET",
                    MatchKeywords = new List<string>
                    {
                        "backend", "back-end", ".net", "dotnet", "c#", "asp.net", "api", "microservice"
                    },
                    // Kendi CV dosyanın tam yolunu yaz; dosya yoksa yükleme adımı atlanır.
                    ResumePath = @"C:\Users\KULLANICI\Desktop\CV\CV-Backend.pdf",
                    CurrentTitle = "Backend Developer",
                    CurrentCompany = "Mevcut Şirket",
                    YearsOfExperience = "3",
                    ExpectedSalary = "80000",
                    NoticePeriod = "1 month",
                    StartDate = "Immediately",
                    Skills = new List<string> { "C#", "ASP.NET Core", "Entity Framework", "SQL Server", "Docker", "Git" },
                    Summary = "3 yıllık .NET deneyimiyle ölçeklenebilir servisler geliştiriyorum.",
                    CoverLetter =
                        "Merhaba,\n\n{company} bünyesindeki {position} pozisyonuna başvurmak istiyorum. " +
                        "C# ve ASP.NET Core ile ölçeklenebilir backend servisleri geliştirme konusunda deneyimliyim.\n\n" +
                        "İlginiz için teşekkür ederim.\n{name}",
                    ExtraAnswers = new Dictionary<string, string>
                    {
                        { "how did you hear", "LinkedIn" }
                    }
                },
                new()
                {
                    Name = "Frontend",
                    MatchKeywords = new List<string>
                    {
                        "frontend", "front-end", "react", "angular", "vue", "javascript", "typescript", "ui developer"
                    },
                    ResumePath = @"C:\Users\KULLANICI\Desktop\CV\CV-Frontend.pdf",
                    CurrentTitle = "Frontend Developer",
                    CurrentCompany = "Mevcut Şirket",
                    YearsOfExperience = "2",
                    ExpectedSalary = "70000",
                    NoticePeriod = "1 month",
                    StartDate = "Immediately",
                    Skills = new List<string> { "JavaScript", "TypeScript", "React", "HTML", "CSS", "Git" },
                    Summary = "Modern React uygulamaları geliştiriyorum.",
                    CoverLetter =
                        "Merhaba,\n\n{company} bünyesindeki {position} pozisyonuna başvurmak istiyorum. " +
                        "React ve TypeScript ile kullanıcı arayüzleri geliştirme konusunda deneyimliyim.\n\n" +
                        "İlginiz için teşekkür ederim.\n{name}",
                    ExtraAnswers = new Dictionary<string, string>
                    {
                        { "how did you hear", "LinkedIn" }
                    }
                }
            }
        };
    }

    /// <summary>Her başvuruda değişmeyen bilgiler.</summary>
    public class PersonalInfo
    {
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Address { get; set; } = "";
        public string City { get; set; } = "";
        public string State { get; set; } = "";
        public string Country { get; set; } = "";
        public string PostalCode { get; set; } = "";

        public string LinkedInUrl { get; set; } = "";
        public string GitHubUrl { get; set; } = "";
        public string PortfolioUrl { get; set; } = "";

        public string University { get; set; } = "";
        public string Degree { get; set; } = "";
        public string Major { get; set; } = "";
        public string GraduationYear { get; set; } = "";
        public string Languages { get; set; } = "";

        /// <summary>"Yes" / "No" — çalışma iznin var mı.</summary>
        public string WorkAuthorization { get; set; } = "Yes";

        /// <summary>"Yes" / "No" — vize sponsorluğuna ihtiyacın var mı.</summary>
        public string RequiresSponsorship { get; set; } = "No";

        public string WillingToRelocate { get; set; } = "Yes";
        public string OpenToRemote { get; set; } = "Yes";

        /// <summary>Ad ve soyaddan türetilir; ayar dosyasına yazılmaz, elle düzenlenecek bir alan değil.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string FullName => $"{FirstName} {LastName}".Trim();
    }

    /// <summary>
    /// Pozisyona özel bilgiler. Başvurduğun ilanın başlığı MatchKeywords ile eşleşirse
    /// bu profildeki CV, unvan, maaş ve ön yazı kullanılır.
    /// </summary>
    public class PositionProfile
    {
        public string Name { get; set; } = "";

        /// <summary>İlan başlığında/şirket adında bunlardan biri geçerse bu profil seçilir.</summary>
        public List<string> MatchKeywords { get; set; } = new();

        /// <summary>Bu pozisyon için yüklenecek CV dosyasının tam yolu.</summary>
        public string ResumePath { get; set; } = "";

        public string CurrentTitle { get; set; } = "";
        public string CurrentCompany { get; set; } = "";
        public string YearsOfExperience { get; set; } = "";
        public string ExpectedSalary { get; set; } = "";
        public string NoticePeriod { get; set; } = "";
        public string StartDate { get; set; } = "";

        public List<string> Skills { get; set; } = new();
        public string Summary { get; set; } = "";

        /// <summary>{position}, {company} ve {name} yer tutucuları başvuru anında doldurulur.</summary>
        public string CoverLetter { get; set; } = "";

        /// <summary>
        /// Bu profile özel serbest sorular. Anahtar, form alanının etiketinde geçen bir ifade
        /// olmalı (ör. "why do you want to work"). Buradaki cevaplar diğer tüm kuralları ezer.
        /// </summary>
        public Dictionary<string, string> ExtraAnswers { get; set; } = new();
    }
}
