using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using VBMS.Services.Evaluators;
using VBMS.Services.Localization;
using VBMS.ViewModels.Components;

namespace VBMS.ViewModels
{
    public partial class RackDetailViewModel : ObservableObject
    {
        private readonly IFireVerificationService _fireVerificationService;
        private readonly ILocalizationService? _localizationService;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(WindowTitle))]
        private RackViewModel _rack;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedCell))]
        private CellViewModel? _selectedCell;

        public bool HasSelectedCell => SelectedCell != null;

        public IEnumerable<string>? SimplifiedBayLabels => Rack?.SimplifiedBayLabels;

        public RackViewModel ParentRack => Rack;

        // ==========================================
        // 다국어 반응형 프로퍼티
        // ==========================================
        public string WindowTitle
        {
            get
            {
                string titlePrefix = _localizationService?.GetString("Str_RackDetailTitle") ?? "랙 상세 관제 콘솔";
                return $"{titlePrefix} - {Rack?.RackName}";
            }
        }

        public string TotalCountText
        {
            get
            {
                bool isEng = _localizationService?.CurrentLanguage?.Equals("en-US", StringComparison.OrdinalIgnoreCase) == true;
                return isEng ? $"Total: {TotalCount}" : $"전체 {TotalCount}개";
            }
        }

        public string SelectedCellPromptText =>
            _localizationService?.GetString("Str_SelectCellGuide") ?? "셀을 선택하면 상세 정보가 표시됩니다.";

        // 셀 상태 요약 바인딩 프로퍼티
        [ObservableProperty] private int _totalCellCount;
        [ObservableProperty] private int _totalCount;
        [ObservableProperty] private int _normalCount;
        [ObservableProperty] private int _commErrorCount;
        [ObservableProperty] private int _smokeCount;
        [ObservableProperty] private int _warningCount;
        [ObservableProperty] private int _fireCount;
        [ObservableProperty] private int _disabledCount;

        public RackDetailViewModel(
            RackViewModel rack,
            IFireVerificationService fireVerificationService,
            ILocalizationService? localizationService = null)
        {
            _rack = rack;
            _fireVerificationService = fireVerificationService;
            // DI 컨테이너에서 자동으로 가져오거나 인자로 주입받음
            _localizationService = localizationService ?? App.Services.GetService<ILocalizationService>();

            if (_localizationService != null)
            {
                _localizationService.OnLanguageChanged += (lang) =>
                {
                    OnPropertyChanged(nameof(WindowTitle));
                    OnPropertyChanged(nameof(TotalCountText));
                    OnPropertyChanged(nameof(SelectedCellPromptText));
                };
            }

            SelectedCell = Rack?.CellList?.FirstOrDefault();
            RefreshCounts();
        }

        public void RefreshCounts()
        {
            if (Rack?.CellList == null || Rack.CellList.Count == 0) return;

            int total = Rack.CellList.Count;
            TotalCellCount = total;
            TotalCount = total;
            OnPropertyChanged(nameof(TotalCountText));

            // 정상 (#4CAF50)
            NormalCount = Rack.CellList.Count(c =>
                string.Equals(c.CellColor, "#4CAF50", StringComparison.OrdinalIgnoreCase));

            // 통신오류/미연결 (#9E9E9E, #FFC107)
            CommErrorCount = Rack.CellList.Count(c =>
                string.Equals(c.CellColor, "#9E9E9E", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.CellColor, "#FFC107", StringComparison.OrdinalIgnoreCase));

            // 연기감지 (#FF9800)
            SmokeCount = Rack.CellList.Count(c =>
                string.Equals(c.CellColor, "#FF9800", StringComparison.OrdinalIgnoreCase));
            WarningCount = SmokeCount;

            // 화재발생 (#F44336)
            FireCount = Rack.CellList.Count(c =>
                string.Equals(c.CellColor, "#F44336", StringComparison.OrdinalIgnoreCase));

            // 사용중지 (#607D8B)
            DisabledCount = Rack.CellList.Count(c =>
                string.Equals(c.CellColor, "#607D8B", StringComparison.OrdinalIgnoreCase));
        }

        public void UpdateSummaryCounts() => RefreshCounts();

        [RelayCommand]
        private async Task ResetSelectedCellSensorAsync(CellViewModel? targetCell)
        {
            var cellToReset = targetCell ?? SelectedCell;
            if (cellToReset == null) return;

            bool success = await _fireVerificationService.ManualResetAsync(
                cellToReset.DetectorKey,
                cellToReset.BoardId.ToString(),
                cellToReset.LocalBay,
                cellToReset.Level
            );

            if (success)
            {
                cellToReset.ResetSensor();
                RefreshCounts();
            }
        }
    }
}