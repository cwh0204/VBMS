using CommunityToolkit.Mvvm.ComponentModel;

namespace VBMS.ViewModels.Components
{
    // ⭐ partial 키워드가 반드시 있어야 [ObservableProperty]가 PascalCase 프로퍼티를 자동 생성합니다.
    public partial class CellViewModel : ObservableObject
    {
        [ObservableProperty]
        private int _bay;

        [ObservableProperty]
        private int _localBay;

        [ObservableProperty]
        private int _level;

        [ObservableProperty]
        private int _boardId;

        [ObservableProperty]
        private string _detectorKey = string.Empty;

        [ObservableProperty]
        private string _cellColor = "#9E9E9E";

        [ObservableProperty]
        private string _cellTooltip = string.Empty;

        public void ResetSensor()
        {
            CellColor = "#4CAF50";
            CellTooltip = $"[ 위치: {Bay}연 {Level:00}단 ] 정상 (리셋됨)";
        }
    }
}