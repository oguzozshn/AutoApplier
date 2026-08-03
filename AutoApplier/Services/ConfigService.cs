using System.Text.Json;

namespace AutoApplier.Services
{
    /// <summary>
    /// JSON ayar dosyalarını okur; dosya yoksa örnek bir tane oluşturup kullanıcıyı düzenlemeye yönlendirir.
    /// Ayarları koda gömmek yerine dosyada tutmak, her değişiklikte yeniden derleme ihtiyacını kaldırıyor.
    /// </summary>
    public static class ConfigService
    {
        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        /// <summary>
        /// Dosyayı okur. Yoksa <paramref name="factory"/> ile örnek üretip diske yazar ve
        /// <paramref name="created"/> bayrağını true döndürür.
        /// </summary>
        public static T LoadOrCreate<T>(string path, Func<T> factory, out bool created)
        {
            AppPaths.EnsureDirectories();
            created = false;

            if (!File.Exists(path))
            {
                var sample = factory();
                Save(path, sample);
                created = true;
                return sample;
            }

            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<T>(json, ReadOptions);

                if (loaded == null)
                {
                    Console.WriteLine($"{Path.GetFileName(path)} boş görünüyor. Varsayılanlar kullanılıyor.");
                    return factory();
                }

                return loaded;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"{Path.GetFileName(path)} okunamadı: {ex.Message}");
                Console.WriteLine("Dosyadaki JSON hatasını düzeltmen gerekiyor. Şimdilik varsayılanlar kullanılıyor.");
                return factory();
            }
        }

        public static void Save<T>(string path, T value)
        {
            AppPaths.EnsureDirectories();
            File.WriteAllText(path, JsonSerializer.Serialize(value, WriteOptions));
        }
    }
}
