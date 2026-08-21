using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using AutoApplier.Models;

namespace AutoApplier.Services
{
    /// <summary>Bir sayfada ne doldurulduğunun / neyin atlandığının raporu.</summary>
    public class FillReport
    {
        public List<string> Filled { get; } = new();
        public List<string> Skipped { get; } = new();
        public List<string> RequiredLeftEmpty { get; } = new();
        public bool ResumeUploaded { get; set; }

        public bool NeedsAttention => RequiredLeftEmpty.Count > 0;
    }

    /// <summary>
    /// Siteden bağımsız form doldurucu. Alanın etiketini (label, aria-label, name, placeholder,
    /// Workday'in data-automation-id'si vb.) okuyup FieldRules üzerinden hangi bilginin
    /// gireceğine karar verir. Greenhouse, Lever, Workday gibi sistemlerde çalışır.
    ///
    /// Bilinçli olarak HİÇBİR ZAMAN gönder/submit butonuna basmaz — son kontrol ve gönderme sende.
    /// </summary>
    public class FormFiller
    {
        private readonly IWebDriver _driver;
        private readonly ProfileConfig _config;

        public FormFiller(IWebDriver driver, ProfileConfig config)
        {
            _driver = driver;
            _config = config;
        }

        public FillReport Fill(ResolvedProfile profile)
        {
            var report = new FillReport();

            UploadResume(profile, report);
            FillTextFields(profile, report);
            FillSelects(profile, report);
            FillRadioGroups(profile, report);
            FillCheckboxes(profile, report);
            FillCustomDropdowns(profile, report);

            return report;
        }

        // --- CV yükleme --------------------------------------------------------

        private void UploadResume(ResolvedProfile profile, FillReport report)
        {
            if (string.IsNullOrWhiteSpace(profile.ResumePath)) return;

            if (!File.Exists(profile.ResumePath))
            {
                report.Skipped.Add($"CV yüklenemedi — dosya yok: {profile.ResumePath}");
                return;
            }

            var fileInputs = _driver.FindElements(By.CssSelector("input[type='file']"));

            foreach (var input in fileInputs)
            {
                try
                {
                    // Dosya girdileri genelde CSS ile gizlenir; SendKeys için görünür yapmamız gerekiyor.
                    ExecuteScript(
                        "arguments[0].style.display='block';" +
                        "arguments[0].style.visibility='visible';" +
                        "arguments[0].style.height='1px';" +
                        "arguments[0].style.width='1px';" +
                        "arguments[0].style.opacity='1';",
                        input);

                    input.SendKeys(profile.ResumePath);
                    report.ResumeUploaded = true;
                    report.Filled.Add($"CV yüklendi: {Path.GetFileName(profile.ResumePath)}");
                    return;
                }
                catch (Exception ex)
                {
                    report.Skipped.Add($"CV yükleme başarısız: {ex.Message}");
                }
            }
        }

        // --- Metin alanları ----------------------------------------------------

        private void FillTextFields(ResolvedProfile profile, FillReport report)
        {
            var selector = "input[type='text'], input[type='email'], input[type='tel'], " +
                           "input[type='url'], input[type='number'], input:not([type]), textarea";

            foreach (var element in SafeFind(selector))
            {
                try
                {
                    if (!IsInteractable(element)) continue;

                    var label = GetLabelText(element);
                    var normalized = FieldRules.Normalize(label);

                    // Zaten dolu olan alanı ezmeyelim — site kendi doldurmuş olabilir.
                    var existing = element.GetAttribute("value");
                    if (!string.IsNullOrWhiteSpace(existing) && !IsDialCodeOnly(existing, normalized))
                    {
                        report.Skipped.Add($"{Describe(label)} — zaten dolu ({Truncate(existing, 40)})");
                        continue;
                    }

                    var value = ResolveValue(normalized, profile);
                    if (value == null)
                    {
                        RecordUnanswered(element, label, report);
                        continue;
                    }

                    element.Clear();
                    element.SendKeys(value);
                    report.Filled.Add($"{Describe(label)} = {Truncate(value, 60)}");
                }
                catch (StaleElementReferenceException)
                {
                    // Sayfa yeniden çizildi; bu alanı atla.
                }
                catch (Exception ex)
                {
                    report.Skipped.Add($"Alan doldurulamadı: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Telefon alanları sık sık ülke koduyla ("+90") önden doldurulmuş geliyor. Bu bir cevap
        /// değil, numaranın başlangıcı — "zaten dolu" sayılırsa numara hiç yazılmıyor ve alan
        /// sessizce eksik kalıyor.
        ///
        /// Kontrol bilerek dar tutuldu: yalnızca etiketi telefon kuralına düşen alanlarda ve
        /// yalnızca değer "+" ile birkaç rakamdan ibaretse geçerli. Aksi halde "ülke kodu"
        /// alanındaki doğru "+90" değerinin üstüne şehir/ülke cevabı yazılabilirdi.
        /// </summary>
        private static bool IsDialCodeOnly(string existing, string normalizedLabel)
        {
            if (FieldRules.MatchKey(normalizedLabel) != AnswerKeys.Phone) return false;

            var digitsOnly = new string(existing.Where(char.IsLetterOrDigit).ToArray());

            return digitsOnly.Length <= 4 && digitsOnly.All(char.IsDigit);
        }

        // --- Açılır listeler (gerçek <select>) ---------------------------------

        private void FillSelects(ResolvedProfile profile, FillReport report)
        {
            foreach (var element in SafeFind("select"))
            {
                try
                {
                    if (!IsInteractable(element)) continue;

                    var label = GetLabelText(element);
                    var normalized = FieldRules.Normalize(label);

                    var select = new SelectElement(element);

                    // Seçili olan anlamlı bir seçenekse dokunma.
                    var current = select.SelectedOption?.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(current) && !IsPlaceholderOption(current))
                    {
                        report.Skipped.Add($"{Describe(label)} — zaten seçili ({current})");
                        continue;
                    }

                    var value = ResolveValue(normalized, profile);
                    if (value == null)
                    {
                        RecordUnanswered(element, label, report);
                        continue;
                    }

                    if (TrySelectOption(select, value, out var chosen))
                    {
                        report.Filled.Add($"{Describe(label)} = {chosen}");
                    }
                    else
                    {
                        report.Skipped.Add($"{Describe(label)} — \"{value}\" seçeneklerde bulunamadı");
                        RecordRequired(element, label, report);
                    }
                }
                catch (StaleElementReferenceException) { }
                catch (Exception ex)
                {
                    report.Skipped.Add($"Liste seçilemedi: {ex.Message}");
                }
            }
        }

        /// <summary>Önce birebir, sonra kısmi, sonra sayısal eşleşme dener ("3" → "3-5 years").</summary>
        private static bool TrySelectOption(SelectElement select, string value, out string chosen)
        {
            chosen = "";
            var options = select.Options
                .Where(o => !string.IsNullOrWhiteSpace(o.Text))
                .ToList();

            foreach (var target in CandidateValues(value))
            {
                var exact = options.FirstOrDefault(o => FieldRules.Normalize(o.Text).Trim() == target);
                if (exact != null)
                {
                    select.SelectByText(exact.Text);
                    chosen = exact.Text.Trim();
                    return true;
                }

                var partial = options.FirstOrDefault(o =>
                {
                    var text = FieldRules.Normalize(o.Text).Trim();

                    // Boş/simgesel seçenek her hedefin içinde "bulunur"; onu eşleşme sayma.
                    if (text.Length == 0 || target.Length == 0) return false;

                    return text.Contains(target, StringComparison.Ordinal) ||
                           target.Contains(text, StringComparison.Ordinal);
                });

                if (partial != null && !IsPlaceholderOption(partial.Text))
                {
                    select.SelectByText(partial.Text);
                    chosen = partial.Text.Trim();
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Bir cevabın denenecek biçimleri. Profildeki bazı alanlar birden fazla değeri tek
        /// metinde tutuyor ("Turkish (Native), English (C1)") ama form tek seçim bekliyor;
        /// önce metnin tamamı, sonra virgülle ayrılmış parçalar, sonra parantezsiz halleri
        /// deneniyor ki "Türkçe" seçeneği bulunabilsin.
        /// </summary>
        private static IEnumerable<string> CandidateValues(string value)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            IEnumerable<string> Yield(string raw)
            {
                var normalized = FieldRules.Normalize(raw).Trim();
                if (normalized.Length > 0 && seen.Add(normalized)) yield return normalized;
            }

            foreach (var candidate in Yield(value)) yield return candidate;

            var parts = value.Split(new[] { ',', ';', '/', '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) yield break;

            foreach (var part in parts)
            {
                foreach (var candidate in Yield(part)) yield return candidate;

                // "English (IELTS 7.0 - C1)" → "English"
                var parenthesis = part.IndexOf('(');
                if (parenthesis > 0)
                {
                    foreach (var candidate in Yield(part[..parenthesis])) yield return candidate;
                }
            }
        }

        /// <summary>
        /// "Seçiniz" tipi boş seçenekleri tanır. Bunu kaçırmak sessiz bir hataya yol açıyor:
        /// yer tutucu gerçek bir cevap sanılırsa alan "zaten dolu" diye atlanıyor ve zorunlu
        /// olsa bile raporun "boş kalanlar" listesine girmiyor — form eksik gönderiliyor.
        /// </summary>
        private static bool IsPlaceholderOption(string text)
        {
            var t = FieldRules.Normalize(text).Trim();

            if (t.Length == 0) return true;

            string[] exact =
            {
                "select", "select one", "select an option", "choose", "choose one",
                "please select", "please choose", "none", "n a",
                "seciniz", "secim yapiniz", "lutfen seciniz",
                "bir opsiyon secin", "bir opsiyon seciniz",
                "bir secenek secin", "bir secenek seciniz", "sec"
            };

            return exact.Contains(t);
        }

        // --- Radio grupları ----------------------------------------------------

        private void FillRadioGroups(ResolvedProfile profile, FillReport report)
        {
            var radios = SafeFind("input[type='radio']")
                .Where(IsInteractable)
                .ToList();

            // Aynı "name" değerini paylaşan radio'lar tek bir soruyu temsil eder.
            var groups = radios.GroupBy(r =>
            {
                try { return r.GetAttribute("name") ?? ""; }
                catch { return ""; }
            });

            foreach (var group in groups)
            {
                try
                {
                    var members = group.ToList();
                    if (members.Count == 0) continue;

                    if (members.Any(m => { try { return m.Selected; } catch { return false; } }))
                    {
                        continue; // Bu soru zaten cevaplanmış.
                    }

                    var groupLabel = GetGroupLabelText(members[0]);
                    var normalized = FieldRules.Normalize(groupLabel);

                    var value = ResolveValue(normalized, profile);
                    if (value == null)
                    {
                        report.Skipped.Add($"{Describe(groupLabel)} — cevap eşleşmedi (seçenekli soru)");
                        RecordRequired(members[0], groupLabel, report);
                        continue;
                    }

                    var target = FieldRules.Normalize(value).Trim();
                    IWebElement? match = null;

                    foreach (var radio in members)
                    {
                        var optionText = FieldRules.Normalize(GetOwnLabelText(radio)).Trim();
                        if (optionText.Length == 0) continue;

                        if (optionText == target || optionText.StartsWith(target, StringComparison.Ordinal))
                        {
                            match = radio;
                            break;
                        }
                    }

                    if (match == null)
                    {
                        report.Skipped.Add($"{Describe(groupLabel)} — \"{value}\" seçeneklerde yok");
                        RecordRequired(members[0], groupLabel, report);
                        continue;
                    }

                    ClickSafely(match);
                    report.Filled.Add($"{Describe(groupLabel)} = {value}");
                }
                catch (StaleElementReferenceException) { }
                catch (Exception ex)
                {
                    report.Skipped.Add($"Seçenek işaretlenemedi: {ex.Message}");
                }
            }
        }

        // --- Onay kutuları -----------------------------------------------------

        private void FillCheckboxes(ResolvedProfile profile, FillReport report)
        {
            foreach (var element in SafeFind("input[type='checkbox']"))
            {
                try
                {
                    if (!IsInteractable(element) || element.Selected) continue;

                    var label = GetLabelText(element);
                    var normalized = FieldRules.Normalize(label);

                    // Şartlar/gizlilik onayları kasıtlı olarak atlanır: bunları sen okuyup onaylamalısın.
                    if (IsConsentCheckbox(normalized))
                    {
                        report.Skipped.Add($"{Describe(label)} — onay kutusu, senin işaretlemen gerekiyor");
                        continue;
                    }

                    var value = ResolveValue(normalized, profile);
                    if (value == null) continue;

                    if (FieldRules.IsAffirmative(value))
                    {
                        ClickSafely(element);
                        report.Filled.Add($"{Describe(label)} = işaretlendi");
                    }
                }
                catch (StaleElementReferenceException) { }
                catch (Exception ex)
                {
                    report.Skipped.Add($"Onay kutusu işaretlenemedi: {ex.Message}");
                }
            }
        }

        private static bool IsConsentCheckbox(string normalized)
        {
            string[] consentWords =
            {
                "terms", "privacy", "consent", "agree", "gdpr", "kvkk",
                "acik riza", "kabul ediyorum", "onayliyorum", "aydinlatma"
            };

            return consentWords.Any(w => normalized.Contains(w, StringComparison.Ordinal));
        }

        // --- Özel (div tabanlı) açılır listeler --------------------------------

        private void FillCustomDropdowns(ResolvedProfile profile, FillReport report)
        {
            // Workday/Greenhouse gibi sistemler <select> yerine erişilebilirlik rolleri kullanıyor.
            var comboboxes = SafeFind("[role='combobox'], [aria-haspopup='listbox']")
                .Where(IsInteractable)
                .ToList();

            foreach (var combo in comboboxes)
            {
                try
                {
                    var label = GetLabelText(combo);
                    var normalized = FieldRules.Normalize(label);

                    var value = ResolveValue(normalized, profile);
                    if (value == null) continue;

                    var existing = combo.GetAttribute("value") ?? combo.Text;
                    if (!string.IsNullOrWhiteSpace(existing) && !IsPlaceholderOption(existing)) continue;

                    ClickSafely(combo);
                    Thread.Sleep(600);

                    var options = _driver.FindElements(By.CssSelector("[role='option']"))
                        .Where(o => { try { return o.Displayed; } catch { return false; } })
                        .ToList();

                    var target = FieldRules.Normalize(value).Trim();

                    var match = options.FirstOrDefault(o =>
                        FieldRules.Normalize(o.Text).Trim() == target)
                        ?? options.FirstOrDefault(o =>
                            FieldRules.Normalize(o.Text).Contains(target, StringComparison.Ordinal));

                    if (match != null)
                    {
                        ClickSafely(match);
                        report.Filled.Add($"{Describe(label)} = {match.Text.Trim()}");
                    }
                    else
                    {
                        // Listeyi açık bırakmayalım.
                        try { combo.SendKeys(Keys.Escape); } catch { }
                        report.Skipped.Add($"{Describe(label)} — özel liste, \"{value}\" bulunamadı");
                    }
                }
                catch (StaleElementReferenceException) { }
                catch (Exception ex)
                {
                    report.Skipped.Add($"Özel liste doldurulamadı: {ex.Message}");
                }
            }
        }

        // --- Cevap çözümleme ---------------------------------------------------

        /// <summary>
        /// Etikete karşılık gelen cevabı bulur. Önce profile özel serbest cevaplara,
        /// sonra genel kural tablosuna bakar.
        /// </summary>
        private string? ResolveValue(string normalizedLabel, ResolvedProfile profile)
        {
            if (string.IsNullOrWhiteSpace(normalizedLabel)) return null;

            // Profildeki ExtraAnswers her şeyi ezer.
            foreach (var pair in profile.ExtraAnswers)
            {
                var key = FieldRules.Normalize(pair.Key).Trim();
                if (key.Length > 0 && normalizedLabel.Contains(key, StringComparison.Ordinal))
                {
                    return pair.Value;
                }
            }

            var answerKey = FieldRules.MatchKey(normalizedLabel);
            if (answerKey == null) return null;

            if (answerKey == AnswerKeys.Eeo) return _config.EeoDefaultAnswer;

            return profile.Get(answerKey);
        }

        // --- Etiket okuma ------------------------------------------------------

        private const string LabelScript = """
            const el = arguments[0];
            const parts = [];
            const push = (t) => { if (t && t.trim()) parts.push(t.trim().slice(0, 200)); };

            push(el.getAttribute('aria-label'));

            const labelledBy = el.getAttribute('aria-labelledby');
            if (labelledBy) {
                labelledBy.split(/\s+/).forEach(id => {
                    const node = document.getElementById(id);
                    if (node) push(node.innerText);
                });
            }

            if (el.id) {
                try {
                    const forLabel = document.querySelector('label[for="' + CSS.escape(el.id) + '"]');
                    if (forLabel) push(forLabel.innerText);
                } catch (e) {}
            }

            const parentLabel = el.closest('label');
            if (parentLabel) push(parentLabel.innerText);

            const fieldset = el.closest('fieldset');
            if (fieldset) {
                const legend = fieldset.querySelector('legend');
                if (legend) push(legend.innerText);
            }

            const group = el.closest('[role="group"],[role="radiogroup"]');
            if (group) push(group.getAttribute('aria-label'));

            push(el.getAttribute('placeholder'));

            // Standart etiket kaynaklarının hiçbiri tutmadıysa alanı saran kapsayıcının
            // metnine düş. Lever gibi sistemler soruyu <label> yerine ayrı bir div'de
            // tutuyor; bu olmadan alan adı olarak "cards[uuid][field0]" kalıyor.
            if (parts.length === 0) push(containerText(el));

            ['name', 'data-automation-id', 'title'].forEach(attr => {
                push(el.getAttribute(attr));
            });

            return parts.join(' | ');
            """;

        /// <summary>
        /// Alanı saran en yakın kapsayıcının metnini çıkarır. Girdi elemanlarının kendi
        /// değerleri temizlenir, geriye sadece etiket/soru metni kalır.
        /// Her iki etiket betiğinin başına eklenir.
        /// </summary>
        private const string ContainerTextHelper = """
            function containerText(el) {
                let node = el.parentElement;
                for (let depth = 0; node && depth < 4; depth++, node = node.parentElement) {
                    const clone = node.cloneNode(true);
                    clone.querySelectorAll('input,select,textarea,button,option,svg').forEach(n => n.remove());
                    const text = (clone.textContent || '').replace(/\s+/g, ' ').trim();
                    if (text.length > 0 && text.length < 300) return text;
                }
                return '';
            }

            // Radio grupları için: aynı gruptaki birden fazla seçeneği kapsayan ilk kutuyu bul.
            // O kutunun metninden seçenek etiketlerini çıkarınca geriye sorunun kendisi kalır.
            // Tek seçeneğin kapsayıcısına bakmak "Yes, I have." gibi cevabı soru sanmaya yol açıyor.
            function radioQuestion(el) {
                const name = el.getAttribute('name');
                if (!name) return '';

                let node = el.parentElement;
                for (let depth = 0; node && depth < 6; depth++, node = node.parentElement) {
                    let siblings;
                    try {
                        siblings = node.querySelectorAll(
                            'input[type="radio"][name="' + CSS.escape(name) + '"]');
                    } catch (e) { return ''; }

                    if (siblings.length < 2) continue;

                    const clone = node.cloneNode(true);
                    // Seçenek metinleri kendi label'ları içinde; onları at, soru kalsın.
                    clone.querySelectorAll('label').forEach(l => {
                        if (l.querySelector('input')) l.remove();
                    });
                    clone.querySelectorAll('input,select,textarea,button,option,svg').forEach(n => n.remove());

                    const text = (clone.textContent || '').replace(/\s+/g, ' ').trim();
                    if (text.length > 0 && text.length < 300) return text;
                }
                return '';
            }
            """;

        /// <summary>
        /// Radio grupları için: sorunun kendisini okur, seçeneğin metnini DEĞİL.
        /// GetLabelText burada kullanılamaz çünkü closest('label') ilk seçeneğin metnini
        /// ("Yes") döndürüp hem raporu hem eşleştirmeyi bozuyor.
        /// </summary>
        private const string GroupLabelScript = """
            const el = arguments[0];
            const parts = [];
            const push = (t) => { if (t && t.trim()) parts.push(t.trim().slice(0, 200)); };

            const fieldset = el.closest('fieldset');
            if (fieldset) {
                const legend = fieldset.querySelector('legend');
                if (legend) push(legend.innerText);
            }

            const group = el.closest('[role="group"],[role="radiogroup"]');
            if (group) {
                push(group.getAttribute('aria-label'));
                const labelledBy = group.getAttribute('aria-labelledby');
                if (labelledBy) {
                    labelledBy.split(/\s+/).forEach(id => {
                        const node = document.getElementById(id);
                        if (node) push(node.innerText);
                    });
                }
            }

            // Fieldset/legend yoksa önce grubu saran kutudan soruyu çıkarmayı dene,
            // olmazsa genel kapsayıcı metnine düş — Lever'ın radio grupları böyle.
            if (parts.length === 0) push(radioQuestion(el));
            if (parts.length === 0) push(containerText(el));

            push(el.getAttribute('name'));
            return parts.join(' | ');
            """;

        private const string OwnLabelScript = """
            const el = arguments[0];
            const parentLabel = el.closest('label');
            if (parentLabel && parentLabel.innerText.trim()) return parentLabel.innerText.trim();
            if (el.id) {
                try {
                    const forLabel = document.querySelector('label[for="' + CSS.escape(el.id) + '"]');
                    if (forLabel && forLabel.innerText.trim()) return forLabel.innerText.trim();
                } catch (e) {}
            }
            return el.getAttribute('aria-label') || el.value || '';
            """;

        /// <summary>Alanın tüm etiket kaynaklarını tek metinde toplar.</summary>
        private string GetLabelText(IWebElement element)
        {
            try
            {
                var result = ExecuteScript(ContainerTextHelper + "\n" + LabelScript, element) as string;
                return result ?? "";
            }
            catch
            {
                return SafeAttribute(element, "name");
            }
        }

        /// <summary>Radio grubunun sorusu. Boş dönerse genel etiket okumasına düşer.</summary>
        private string GetGroupLabelText(IWebElement element)
        {
            try
            {
                var result = ExecuteScript(ContainerTextHelper + "\n" + GroupLabelScript, element) as string;
                return string.IsNullOrWhiteSpace(result) ? GetLabelText(element) : result;
            }
            catch
            {
                return GetLabelText(element);
            }
        }

        /// <summary>Radio/checkbox'ın kendi seçenek metni (grup etiketi değil).</summary>
        private string GetOwnLabelText(IWebElement element)
        {
            try
            {
                return ExecuteScript(OwnLabelScript, element) as string ?? "";
            }
            catch
            {
                return SafeAttribute(element, "value");
            }
        }

        // --- Yardımcılar -------------------------------------------------------

        private void RecordUnanswered(IWebElement element, string label, FillReport report)
        {
            if (IsRequired(element))
            {
                report.RequiredLeftEmpty.Add(Describe(label));
            }
            else
            {
                report.Skipped.Add($"{Describe(label)} — eşleşen cevap yok");
            }
        }

        private void RecordRequired(IWebElement element, string label, FillReport report)
        {
            if (IsRequired(element)) report.RequiredLeftEmpty.Add(Describe(label));
        }

        private bool IsRequired(IWebElement element)
        {
            try
            {
                if (element.GetAttribute("required") != null) return true;
                if (element.GetAttribute("aria-required") == "true") return true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsInteractable(IWebElement element)
        {
            try
            {
                return element.Displayed && element.Enabled &&
                       element.GetAttribute("readonly") == null;
            }
            catch
            {
                return false;
            }
        }

        private List<IWebElement> SafeFind(string cssSelector)
        {
            try
            {
                return _driver.FindElements(By.CssSelector(cssSelector)).ToList();
            }
            catch
            {
                return new List<IWebElement>();
            }
        }

        private void ClickSafely(IWebElement element)
        {
            try
            {
                element.Click();
            }
            catch (ElementClickInterceptedException)
            {
                // Üstünü başka bir katman kapatıyorsa JS ile tıkla.
                ExecuteScript("arguments[0].click();", element);
            }
            catch (ElementNotInteractableException)
            {
                ExecuteScript("arguments[0].click();", element);
            }
        }

        private object? ExecuteScript(string script, params object[] args) =>
            ((IJavaScriptExecutor)_driver).ExecuteScript(script, args);

        private static string SafeAttribute(IWebElement element, string name)
        {
            try { return element.GetAttribute(name) ?? ""; }
            catch { return ""; }
        }

        /// <summary>Etiket metni raporda okunabilir kalsın diye kısaltılır.</summary>
        private static string Describe(string label)
        {
            var first = label.Split('|', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            var text = string.IsNullOrWhiteSpace(first) ? label.Trim() : first;
            text = text.Replace("\n", " ").Replace("\r", " ");
            return string.IsNullOrWhiteSpace(text) ? "(isimsiz alan)" : Truncate(text, 50);
        }

        private static string Truncate(string value, int max)
        {
            var single = value.Replace("\n", " ").Replace("\r", " ").Trim();
            return single.Length <= max ? single : single[..max] + "...";
        }
    }
}
