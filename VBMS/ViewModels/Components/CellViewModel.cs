using CommunityToolkit.Mvvm.ComponentModel;

namespace VBMS.ViewModels.Components
{
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

        [ObservableProperty]
        private double _temperature;

        public void ResetSensor()
        {
            CellColor = "#4CAF50";
            CellTooltip = $"[ 위치: {Bay}연 {Level:00}단 ] 정상 (리셋됨)";
        }
    }
}