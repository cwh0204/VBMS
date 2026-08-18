using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Linq;

namespace VBMS.ViewModels
{
    public partial class RackDetailViewModel : ObservableObject
    {
        [ObservableProperty]
        private RackViewModel _rack;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedCell))]
        private CellViewModel? _selectedCell;

        public bool HasSelectedCell => SelectedCell != null;

        public int TotalCount => Rack?.CellList?.Count ?? 0;
        public int NormalCount => Rack?.CellList?.Count(c => CellColors.IsSameColor(c.CellColor, CellColors.Normal)) ?? 0;
        public int CommErrorCount => Rack?.CellList?.Count(c => CellColors.IsSameColor(c.CellColor, CellColors.Offline)) ?? 0;
        public int SmokeCount => Rack?.CellList?.Count(c => CellColors.IsSameColor(c.CellColor, CellColors.Warning)) ?? 0;
        public int FireCount => Rack?.CellList?.Count(c => CellColors.IsSameColor(c.CellColor, CellColors.Alarm)) ?? 0;
        public int DisabledCount => Rack?.CellList?.Count(c => CellColors.IsSameColor(c.CellColor, CellColors.Disabled)) ?? 0;

        public List<string> SimplifiedBayLabels
        {
            get
            {
                if (Rack == null) return new();
                var labels = new List<string>();
                for (int i = 1; i <= Rack.GridColumns; i++)
                {
                    if (i == 1 || i % 5 == 0)
                        labels.Add($"{i:D2}연");
                    else
                        labels.Add(string.Empty);
                }
                return labels;
            }
        }

        public RackDetailViewModel(RackViewModel rack)
        {
            _rack = rack;
            SelectedCell = Rack?.CellList?.FirstOrDefault();

            //  셀의 색상(CellColor)이 실시간으로 변할 때 하단 요약판 자동 재집계
            if (Rack?.CellList != null)
            {
                foreach (var cell in Rack.CellList)
                {
                    cell.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(CellViewModel.CellColor))
                        {
                            RefreshCounts();
                        }
                    };
                }
            }

            // 최초 실행 시 집계 갱신
            RefreshCounts();
        }

        public void RefreshCounts()
        {
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(NormalCount));
            OnPropertyChanged(nameof(CommErrorCount));
            OnPropertyChanged(nameof(SmokeCount));
            OnPropertyChanged(nameof(FireCount));
            OnPropertyChanged(nameof(DisabledCount));
        }

        [RelayCommand]
        private void ResetSelectedCellSensor(CellViewModel? targetCell)
        {
            var cellToReset = targetCell ?? SelectedCell;
            if (cellToReset != null)
            {
                cellToReset.ResetSensor();
                RefreshCounts();
            }
        }

        [RelayCommand]
        private void SelectCell(CellViewModel cell)
        {
            if (cell != null)
            {
                SelectedCell = cell;
            }
        }
    }
}