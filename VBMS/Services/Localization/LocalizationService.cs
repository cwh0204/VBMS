using System;
using System.Linq;
using System.Windows;

namespace VBMS.Services.Localization
{
    public class LocalizationService : ILocalizationService
    {
        private const string LanguageResourceDictionaryPrefix = "Resources/Strings.";

        public string CurrentLanguage { get; private set; } = "ko-KR";

        public event Action<string>? OnLanguageChanged;

        public LocalizationService()
        {
            // 기본 한국어로 초기화
            SetLanguage("ko-KR");
        }

        public void SetLanguage(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode)) return;

            string targetCulture = languageCode.Equals("en-US", StringComparison.OrdinalIgnoreCase)
                ? "en-US"
                : "ko-KR";

            var app = Application.Current;
            if (app == null) return;

            var resourceDictionaries = app.Resources.MergedDictionaries;

            // 기존 Strings.*.xaml 딕셔너리 탐색 및 제거
            var existingDict = resourceDictionaries.FirstOrDefault(d =>
                d.Source != null && d.Source.OriginalString.Contains(LanguageResourceDictionaryPrefix));

            var newDict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/VBMS;component/Resources/Strings.{targetCulture}.xaml", UriKind.Absolute)
            };

            if (existingDict != null)
            {
                int index = resourceDictionaries.IndexOf(existingDict);
                resourceDictionaries[index] = newDict;
            }
            else
            {
                resourceDictionaries.Add(newDict);
            }

            CurrentLanguage = targetCulture;
            OnLanguageChanged?.Invoke(CurrentLanguage);
        }

        public string GetString(string key)
        {
            var value = Application.Current?.TryFindResource(key);
            return value?.ToString() ?? key;
        }
    }
}