namespace AutoApplier.Models
{
    /// <summary>
    /// Yerel yapay zekâ ayarları. Model makinede, Ollama üzerinde çalışıyor: CV metni ve
    /// ilan açıklaması bilgisayardan çıkmıyor, API anahtarı ve ücret yok.
    ///
    /// Varsayılan olarak KAPALI. Açmak için config/ai.json içinde Enabled değerini true yap
    /// ve Ollama'nın çalıştığından emin ol (ollama serve).
    /// </summary>
    public class AiConfig
    {
        public bool Enabled { get; set; }

        public string Endpoint { get; set; } = "http://localhost:11434";

        /// <summary>Talos'un da kullandığı model; 8 GB VRAM'e sığıyor ve Türkçe/İngilizce karışıkta iyi.</summary>
        public string Model { get; set; } = "qwen3:8b";

        public int TimeoutSeconds { get; set; } = 120;

        /// <summary>Düşük tutuldu: başvuru cevabı yaratıcı değil, isabetli olmalı.</summary>
        public double Temperature { get; set; } = 0.4;

        public int ContextTokens { get; set; } = 8192;

        /// <summary>İlan açıklamasının modele verilen kısmı. Uzadıkça cevap süresi büyüyor.</summary>
        public int MaxDescriptionChars { get; set; } = 4000;

        public static AiConfig CreateDefault() => new();
    }
}
