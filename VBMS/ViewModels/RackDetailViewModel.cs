using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using VBMS.Services.Evaluators;
using VBMS.ViewModels.Components;

namespace VBMS.ViewModels
{
    public partial class RackDetailViewModel : ObservableObject
    {
        private readonly IFireVerificationService _fireVerificationService;

        [ObservableProperty]
        private RackViewModel _rack;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedCell))] // ⭐ SelectedCell 변경 시 HasSelectedCell 갱신 통지
        private CellViewModel? _selectedCell;

        // ⭐ XAML의 IsEnabled="{Binding HasSelectedCell}" 바인딩용
        public bool HasSelectedCell => SelectedCell != null;

        // ⭐ XAML의 ItemsSource="{Binding SimplifiedBayLabels}" 바인딩용
        public IEnumerable<string>? SimplifiedBayLabels => Rack?.SimplifiedBayLabels;

        // XAML 호환용 래퍼
        public RackViewModel ParentRack => Rack;

        // 셀 상태 요약 바인딩 프로퍼티
        [ObservableProperty] private int _totalCellCount;
        [ObservableProperty] private int _totalCount;
        [ObservableProperty] private int _normalCount;
        [ObservableProperty] private int _commErrorCount;
        [ObservableProperty] private int _smokeCount;
        [ObservableProperty] private int _warningCount;
        [ObservableProperty] private int _fireCount;
        [ObservableProperty] private int _disabledCount;

        public RackDetailViewModel(RackViewModel rack, IFireVerificationService fireVerificationService)
        {
            _rack = rack;
            _fireVerificationService = fireVerificationService;

            SelectedCell = Rack?.CellList?.FirstOrDefault();
            RefreshCounts();
        }

        /// <summary>
        /// CellList의 색상을 분석하여 요약 개수를 계산합니다.
        /// </summary>
        public void RefreshCounts()
        {
            if (Rack?.CellList == null || Rack.CellList.Count == 0) return;

            int total = Rack.CellList.Count;
            TotalCellCount = total;
            TotalCount = total;

            // 정상 (#4CAF50)
            NormalCount = Rack.CellList.Count(c =>
                string.Equals(c.CellColor, "#4CAF50", StringComparison.OrdinalIgnoreCase));

            // 통신오류/미연결 (회색 #9E9E9E, 노랑 #FFC107)
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