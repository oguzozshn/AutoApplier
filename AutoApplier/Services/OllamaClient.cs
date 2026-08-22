using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoApplier.Models;

namespace AutoApplier.Services
{
    /// <summary>
    /// Yerelde çalışan Ollama'ya sohbet isteği atar. Talos da aynı motoru kullanıyor;
    /// buradaki tercihler onun ölçümlerinden geliyor.
    /// </summary>
    public class OllamaClient : IDisposable
    {
        private static readonly Regex ThinkBlock =
            new("<think>.*?</think>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private readonly AiConfig _config;
        private readonly HttpClient _http;

        public OllamaClient(AiConfig config)
        {
            _config = config;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(10, config.TimeoutSeconds)) };
        }

        /// <summary>
        /// Modelden tek seferlik bir cevap ister. Hata durumunda metin null döner ve
        /// ikinci değer kullanıcıya gösterilecek açıklamayı taşır.
        /// </summary>
        public async Task<(string? Text, string? Error)> AskAsync(string systemPrompt, string userPrompt)
        {
            var body = new
            {
                model = _config.Model,
                stream = false,

                // Düşünme kapalı: Talos'un ölçümünde açıkken 12.6 sn, kapalıyken 0.45 sn.
                // Başvuru cevabı için bu farkı ödemeye değmiyor.
                think = false,

                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },

                options = new
                {
                    temperature = _config.Temperature,
                    num_ctx = _config.ContextTokens
                }
            };

            try
            {
                using var response = await _http.PostAsJsonAsync(
                    _config.Endpoint.TrimEnd('/') + "/api/chat", body);

                if (!response.IsSuccessStatusCode)
                {
                    var detay = await response.Content.ReadAsStringAsync();
                    return (null, $"Ollama {(int)response.StatusCode} döndü: {Truncate(detay, 200)}");
                }

                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

                var text = json.RootElement
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                text = Clean(text);

                return string.IsNullOrWhiteSpace(text)
                    ? (null, "Model boş cevap döndü.")
                    : (text, null);
            }
            catch (HttpRequestException)
            {
                return (null, $"Ollama'ya bağlanılamadı ({_config.Endpoint}). Çalışıyor mu? \"ollama serve\" ile başlatabilirsin.");
            }
            catch (TaskCanceledException)
            {
                return (null, $"Model {_config.TimeoutSeconds} saniyede cevap vermedi. ai.json içinde TimeoutSeconds artırılabilir.");
            }
            catch (Exception ex)
            {
                return (null, $"Yapay zekâ hatası: {ex.Message}");
            }
        }

        /// <summary>Modelin düşünme bloğunu ve etrafındaki tırnak/boşlukları atar.</summary>
        private static string Clean(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";

            var text = ThinkBlock.Replace(raw, "").Trim();

            // Model bazen cevabı tırnak içine alıyor; forma tırnakla yazmayalım.
            if (text.Length > 1 && text.StartsWith('"') && text.EndsWith('"'))
            {
                text = text[1..^1].Trim();
            }

            return text;
        }

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max] + "...";

        public void Dispose()
        {
            _http.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
