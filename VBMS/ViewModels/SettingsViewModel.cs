using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using VBMS.Models;
using VBMS.Services.Evaluators;
using VBMS.Services.Localization;
using VBMS.Services.Settings;
using VBMS.Services.Theme;

namespace VBMS.ViewModels
{
    public class LanguageOption
    {
        public string Code { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class ThemeOption
    {
        public string Code { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;
        private readonly ILocalizationService _localizationService;
        private readonly IFireSignalEvaluator _fireSignalEvaluator;

        // 1. 일반 (General)
        [ObservableProperty] private string _selectedLanguage = "ko-KR";
        [ObservableProperty] private string _selectedTheme = "Dark";

        public List<LanguageOption> LanguageList { get; } = new()
        {
            new LanguageOption { Code = "ko-KR", DisplayName = "한국어 (Korean)" },
            new LanguageOption { Code = "en-US", DisplayName = "English (영어)" }
        };

        public List<ThemeOption> ThemeList { get; } = new()
        {
            new ThemeOption { Code = "Dark", DisplayName = "다크 모드 (Dark)" },
            new ThemeOption { Code = "Light", DisplayName = "라이트 모드 (Light)" }
        };

        // 2. 안전/임계치 (Safety)
        [ObservableProperty] private double _highTempThreshold;
        [ObservableProperty] private double _warningTempThreshold;
        [ObservableProperty] private double _deltaTempThreshold;

        // 3. 네트워크 (Network)
        [ObservableProperty] private int _serverPort;
        [ObservableProperty] private int _boardTimeoutSeconds;

        // 4. 저장소 (Storage)
        [ObservableProperty] private string _exportDirectory = string.Empty;
        [ObservableProperty] private int _maxEventLogCount;

        // 창 닫기 요청 이벤트
        public event Action? RequestClose;

        public SettingsViewModel(
            ISettingsService settingsService,
            ILocalizationService localizationService,
            IFireSignalEvaluator fireSignalEvaluator)
        {
            _settingsService = settingsService;
            _localizationService = localizationService;
            _fireSignalEvaluator = fireSignalEvaluator;

            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            var s = _settingsService.CurrentSettings;
            SelectedLanguage = s.Language;
            SelectedTheme = s.Theme;
            HighTempThreshold = s.HighTempThreshold;
            WarningTempThreshold = s.WarningTempThreshold;
            DeltaTempThreshold = s.DeltaTempThreshold;
            ServerPort = s.ServerPort;
            BoardTimeoutSeconds = s.BoardTimeoutSeconds;
            ExportDirectory = s.ExportDirectory;
            MaxEventLogCount = s.MaxEventLogCount;
        }

        [RelayCommand]
        private void BrowseExportDirectory()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "데이터 저장 및 엑셀 내보내기 폴더 선택",
                InitialDirectory = Directory.Exists(ExportDirectory) ? ExportDirectory : @"D:\"
            };

            if (dialog.ShowDialog() == true)
            {
                ExportDirectory = dialog.FolderName;
            }
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            // 유효성 검증
            if (HighTempThreshold <= WarningTempThreshold)
            {
                MessageBox.Show(
                    "고온 화재 임계치는 주의 온도 임계치보다 높아야 합니다.",
                    "입력 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (ServerPort < 1024 || ServerPort > 65535)
            {
                MessageBox.Show(
                    "서버 포트는 1024 ~ 65535 사이의 유효한 번호여야 합니다.",
                    "입력 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (MaxEventLogCount < 50 || MaxEventLogCount > 5000)
            {
                MessageBox.Show(
                    "실시간 로그 유지 개수는 50 ~ 5000 사이여야 합니다.",
                    "입력 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // 1. 모델 객체 생성
            var model = new SystemSettingsModel
            {
                Language = SelectedLanguage,
                Theme = SelectedTheme,
                HighTempThreshold = HighTempThreshold,
                WarningTempThreshold = WarningTempThreshold,
                DeltaTempThreshold = DeltaTempThreshold,
                ServerPort = ServerPort,
                BoardTimeoutSeconds = BoardTimeoutSeconds,
                ExportDirectory = ExportDirectory,
                MaxEventLogCount = MaxEventLogCount
            };

            // 2. appsettings.json 파일 저장
            await _settingsService.SaveSettingsAsync(model);

            // 3. 다국어 즉시 반영
            _localizationService.SetLanguage(SelectedLanguage);

            // 4. 화면 테마 즉시 반영
            bool isDark = SelectedTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase);
            if (isDark && ThemeService.Instance.CurrentTheme != AppTheme.Dark)
            {
                ThemeService.Instance.ToggleTheme();
            }
            else if (!isDark && ThemeService.Instance.CurrentTheme != AppTheme.Light)
            {
                ThemeService.Instance.ToggleTheme();
            }

            // 5. 화재 평가 엔진 임계치 실시간 갱신
            _fireSignalEvaluator.UpdateThresholds(HighTempThreshold, WarningTempThreshold, DeltaTempThreshold);

            // 완료 알림
            string msg = $"{_localizationService.GetString("Str_MsgSaved")}\n{_localizationService.GetString("Str_MsgPortRestartNotice")}";
            MessageBox.Show(msg, _localizationService.GetString("Str_SettingsTitle"), MessageBoxButton.OK, MessageBoxImage.Information);

            RequestClose?.Invoke();
        }

        [RelayCommand]
        private void Cancel()
        {
            RequestClose?.Invoke();
        }
    }
}