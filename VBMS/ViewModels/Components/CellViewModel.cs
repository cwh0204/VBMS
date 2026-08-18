using CommunityToolkit.Mvvm.ComponentModel;

namespace VBMS.ViewModels.Components
{
    public partial class CellViewModel : ObservableObject
    {
        [ObservableProperty]
        private int _bay;

        [ObservableProperty]
        private int _level;

        [ObservableProperty]
        private string _cellColor = CellColors.Offline;

        [ObservableProperty]
        private string _cellTooltip = string.Empty;

        [ObservableProperty]
        private bool _isDimmed;

        [ObservableProperty]
        private double _temperature = 25.0;

        public bool IsException => CellColors.IsSameColor(CellColor, CellColors.Warning) || CellColors.IsSameColor(CellColor, CellColors.Alarm);

        public string ExceptionSummary => CellTooltip;

        public void ResetSensor()
        {
            if (IsException)
            {
                CellColor = CellColors.Normal;
            }
        }
    }
}