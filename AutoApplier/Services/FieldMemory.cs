using System.Text.Json;

namespace AutoApplier.Services
{
    /// <summary>
    /// Araç bir alanı dolduramadığında sen elle dolduruyorsun ve o bilgi kayboluyordu:
    /// aynı soru bir sonraki formda yine boş kalıyordu. Bu sınıf, başvuruyu "başvuruldu"
    /// olarak işaretlediğin anda formda duran cevapları okuyup saklıyor; sonraki formlarda
    /// aynı etiket görülünce cevap hazır geliyor.
    ///
    /// Yalnızca kural tablosunun cevaplayamadığı alanlar saklanıyor — bilinen alanları
    /// (ad, e-posta, telefon) tekrar tekrar kaydetmenin faydası yok.
    ///
    /// Dosya data/ altında, yani gitignore kapsamında: öğrenilen cevaplar kişisel veridir.
    /// </summary>
    public class FieldMemory
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public class Entry
        {
            /// <summary>Formda göründüğü haliyle etiket — dosyayı elle düzenlerken okunabilsin diye.</summary>
            public string Label { get; set; } = "";

            public string Value { get; set; } = "";

            /// <summary>Kaç farklı başvuruda bu cevapla karşılaşıldı.</summary>
            public int SeenCount { get; set; }

            public string LastSite { get; set; } = "";
            public DateTime UpdatedAt { get; set; } = DateTime.Now;
        }

        /// <summary>Anahtar: normalize edilmiş etiket.</summary>
        private Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        public int Count => _entries.Count;

        public void Load()
        {
            AppPaths.EnsureDirectories();

            if (!File.Exists(AppPaths.FieldMemoryFile))
            {
                _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
                return;
            }

            try
            {
                var json = File.ReadAllText(AppPaths.FieldMemoryFile);
                _entries = JsonSerializer.Deserialize<Dictionary<string, Entry>>(json)
                           ?? new Dictionary<string, Entry>(StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"field-memory.json okunamadı ({ex.Message}). Boş hafızayla devam ediliyor.");
                _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            }
        }

        public void Save()
        {
            AppPaths.EnsureDirectories();
            File.WriteAllText(AppPaths.FieldMemoryFile, JsonSerializer.Serialize(_entries, JsonOptions));
        }

        /// <summary>
        /// Etikete uyan öğrenilmiş cevap. Önce birebir, sonra en uzun kapsayan kayıt.
        /// Kısa anahtarlar rastgele alana bulaşmasın diye en az 10 karakter şartı var.
        /// </summary>
        public string? Lookup(string normalizedLabel)
        {
            if (string.IsNullOrWhiteSpace(normalizedLabel)) return null;

            if (_entries.TryGetValue(normalizedLabel, out var exact)) return exact.Value;

            string? best = null;
            var bestLength = 0;

            foreach (var pair in _entries)
            {
                var key = pair.Key.Trim();
                if (key.Length < 10 || key.Length <= bestLength) continue;

                if (normalizedLabel.Contains(key, StringComparison.Ordinal))
                {
                    best = pair.Value.Value;
                    bestLength = key.Length;
                }
            }

            return best;
        }

        /// <summary>Yeni bir cevap öğrenir. Zaten aynı cevap kayıtlıysa sadece sayacı artırır.</summary>
        public bool Remember(string normalizedLabel, string label, string value, string site)
        {
            if (string.IsNullOrWhiteSpace(normalizedLabel) || string.IsNullOrWhiteSpace(value)) return false;

            var key = normalizedLabel.Trim();
            if (key.Length < 10) return false;

            if (_entries.TryGetValue(key, out var existing))
            {
                existing.SeenCount++;
                existing.LastSite = site;
                existing.UpdatedAt = DateTime.Now;

                // Cevabı değiştirdiysen yenisi geçerli: en son verdiğin cevap en doğrusudur.
                var changed = !string.Equals(existing.Value, value, StringComparison.Ordinal);
                existing.Value = value;
                return changed;
            }

            _entries[key] = new Entry
            {
                Label = label.Trim(),
                Value = value,
                SeenCount = 1,
                LastSite = site,
                UpdatedAt = DateTime.Now
            };

            return true;
        }
    }
}
