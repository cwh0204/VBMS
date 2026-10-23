using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using VBMS.Services.Localization;

namespace VBMS.ViewModels
{
    public enum AlarmType
    {
        Smoke = 1,
        Fire = 2
    }

    public partial class FireAlarmPopupViewModel : ObservableObject
    {
        private readonly Action? _onClose;
        private readonly ILocalizationService? _localizationService;

        // 알람 세부 데이터 (언어 변경 시 메시지 재구성에 사용)
        private readonly AlarmType _alarmType;
        private readonly string _rackName = string.Empty;
        private readonly int _line;
        private readonly int _bay;
        private readonly int _level;
        private readonly double _temperature;
        private readonly bool _isStructured;

        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private string _message = string.Empty;

        public FireAlarmPopupViewModel() { }

        // 1. 단순 텍스트 기반 생성자 (하위 호환성 유지)
        public FireAlarmPopupViewModel(string title, string message, Action? onClose = null)
        {
            Title = title;
            Message = message;
            _onClose = onClose;
        }

        // 2. ⭐ 구조화된 데이터 기반 생성자 (다국어 실시간 전환 지원)
        public FireAlarmPopupViewModel(
            AlarmType alarmType,
            string rackName,
            int line,
            int bay,
            int level,
            double temperature = 0.0,
            Action? onClose = null,
            ILocalizationService? localizationService = null)
        {
            _alarmType = alarmType;
            _rackName = rackName;
            _line = line;
            _bay = bay;
            _level = level;
            _temperature = temperature;
            _onClose = onClose;
            _isStructured = true;

            _localizationService = localizationService ?? App.Services.GetService<ILocalizationService>();

            if (_localizationService != null)
            {
                _localizationService.OnLanguageChanged += HandleLanguageChanged;
            }

            BuildLocalizedContent();
        }

        private void HandleLanguageChanged(string lang)
        {
            if (_isStructured)
            {
                BuildLocalizedContent();
            }
        }

        private void BuildLocalizedContent()
        {
            string bayUnit = _localizationService?.GetString("Str_UnitBay") ?? "연";
            string levelUnit = _localizationService?.GetString("Str_UnitLevel") ?? "단";
            string tempLabel = _localizationService?.GetString("Str_PopupTempLabel") ?? "온도";

            if (_alarmType == AlarmType.Fire)
            {
                Title = _localizationService?.GetString("Str_PopupFireTitle") ?? "🚨 화재 발생";
                Message = $"[ {_rackName} ]\nLine : {_line}   {_bay}{bayUnit}   {_level:00}{levelUnit}\n{tempLabel} : {_temperature:F1}℃";
            }
            else
            {
                Title = _localizationService?.GetString("Str_PopupSmokeTitle") ?? "⚠️ 연기 감지";
                Message = $"[ {_rackName} ]\nLine : {_line}   {_bay}{bayUnit}   {_level:00}{levelUnit}";
            }
        }

        [RelayCommand]
        private void Close()
        {
            if (_localizationService != null && _isStructured)
            {
                _localizationService.OnLanguageChanged -= HandleLanguageChanged;
            }
            _onClose?.Invoke();
        }
    }
}