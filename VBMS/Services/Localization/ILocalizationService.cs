using System;
using System.Collections.Generic;

namespace VBMS.Services.Localization
{
    public interface ILocalizationService
    {
        string CurrentLanguage { get; }
        event Action<string>? OnLanguageChanged;
        void SetLanguage(string languageCode);
        string GetString(string key);
    }
}