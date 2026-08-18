using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        private CellViewModel? _selectedCell;

        [ObservableProperty]
        private int _totalCellCount;

        [ObservableProperty]
        private int _normalCount;

        [ObservableProperty]
        private int _warningCount;

        [ObservableProperty]
        private int _fireCount;

        public RackDetailViewModel(RackViewModel rack, IFireVerificationService fireVerificationService)
        {
            _rack = rack;
            _fireVerificationService = fireVerificationService;

            SelectedCell = Rack?.CellList?.FirstOrDefault();
            RefreshCounts();
        }

        public void RefreshCounts()
        {
            if (Rack?.CellList == null) return;

            TotalCellCount = Rack.CellList.Count;
            NormalCount = Rack.CellList.Count(c => c.CellColor == "#4CAF50");
            WarningCount = Rack.CellList.Count(c => c.CellColor == "#FF9800");
            FireCount = Rack.CellList.Count(c => c.CellColor == "#F44336");
        }

        [RelayCommand]
        private async Task ResetSelectedCellSensorAsync(CellViewModel? targetCell)
        {
            var cellToReset = targetCell ?? SelectedCell;

            // 1. 셀 선택 여부 확인
            if (cellToReset == null)
            {
                return;
            }

            // 3. 서비스 호출 결과 확인
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
            else
            {
            }
        }
    }
}