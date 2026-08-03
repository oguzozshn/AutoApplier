using System.Text;
using AutoApplier.Models;

namespace AutoApplier.Services
{
    /// <summary>
    /// İlan listesini Excel'de açılabilir CSV ve tıklanabilir linkli Markdown olarak yazar.
    /// </summary>
    public static class JobExporter
    {
        public static void ExportCsv(IEnumerable<JobListing> jobs, string path)
        {
            var sb = new StringBuilder();

            // Türkçe Excel noktalı virgülü ayraç olarak bekliyor.
            sb.AppendLine("Tarih;Başlık;Şirket;Konum;Maaş;Arama;Başvuruldu;Link");

            foreach (var job in jobs)
            {
                sb.Append(Escape(job.PostedDisplay)).Append(';')
                  .Append(Escape(job.Title)).Append(';')
                  .Append(Escape(job.Company)).Append(';')
                  .Append(Escape(job.Location)).Append(';')
                  .Append(Escape(job.SalaryInfo ?? "")).Append(';')
                  .Append(Escape(job.SearchName)).Append(';')
                  .Append(job.Processed ? "Evet" : "Hayır").Append(';')
                  .Append(Escape(job.Url))
                  .AppendLine();
            }

            // BOM'lu UTF-8: Excel Türkçe karakterleri doğru göstersin.
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        public static void ExportMarkdown(IEnumerable<JobListing> jobs, string path)
        {
            var list = jobs.ToList();
            var sb = new StringBuilder();

            sb.AppendLine("# LinkedIn İlanları");
            sb.AppendLine();
            sb.AppendLine($"Güncelleme: {DateTime.Now:yyyy-MM-dd HH:mm} — toplam {list.Count} ilan");
            sb.AppendLine();

            foreach (var group in list.GroupBy(j => j.SearchName))
            {
                sb.AppendLine($"## {group.Key}");
                sb.AppendLine();
                sb.AppendLine("| Tarih | Pozisyon | Şirket | Konum | Durum |");
                sb.AppendLine("|---|---|---|---|---|");

                foreach (var job in group.OrderByDescending(j => j.PostedDate ?? DateTime.MinValue))
                {
                    var title = $"[{EscapeMarkdown(job.Title)}]({job.Url})";
                    var status = job.Processed ? "başvuruldu" : "-";

                    sb.Append("| ").Append(job.PostedDisplay)
                      .Append(" | ").Append(title)
                      .Append(" | ").Append(EscapeMarkdown(job.Company))
                      .Append(" | ").Append(EscapeMarkdown(job.Location))
                      .Append(" | ").Append(status)
                      .AppendLine(" |");
                }

                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            // CSV: içinde ayraç, tırnak veya satır sonu varsa tırnakla ve iç tırnakları ikile.
            if (value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        private static string EscapeMarkdown(string value) =>
            string.IsNullOrEmpty(value) ? "" : value.Replace("|", "\\|");
    }
}
