namespace AutoApplier.Services
{
    /// <summary>
    /// Ayar ve çıktı dosyalarının konumu. Çalışma dizinine göre çözülür; `dotnet run` ile
    /// çalıştırınca bu proje klasörüdür, yani dosyaları yeniden derlemeden düzenleyebilirsin.
    /// </summary>
    public static class AppPaths
    {
        public static string Root => Directory.GetCurrentDirectory();

        public static string ConfigDir => Path.Combine(Root, "config");
        public static string DataDir => Path.Combine(Root, "data");

        public static string SearchesFile => Path.Combine(ConfigDir, "searches.json");
        public static string ProfilesFile => Path.Combine(ConfigDir, "profiles.json");

        /// <summary>Öncelikli şirketler listesi (bkz. CompanyConfig).</summary>
        public static string CompaniesFile => Path.Combine(ConfigDir, "companies.json");

        /// <summary>Yerel yapay zekâ ayarları (bkz. AiConfig).</summary>
        public static string AiFile => Path.Combine(ConfigDir, "ai.json");

        public static string JobsFile => Path.Combine(DataDir, "jobs.json");
        public static string JobsCsvFile => Path.Combine(DataDir, "jobs.csv");
        public static string JobsMarkdownFile => Path.Combine(DataDir, "jobs.md");

        /// <summary>Formlarda elle doldurulan cevapların hafızası (bkz. FieldMemory).</summary>
        public static string FieldMemoryFile => Path.Combine(DataDir, "field-memory.json");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(ConfigDir);
            Directory.CreateDirectory(DataDir);
        }
    }
}
