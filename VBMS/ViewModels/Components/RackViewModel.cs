using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore.SkiaSharpView.WPF;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using VBMS.Models;
using VBMS.Services.Evaluators; // ⭐ IFireSignalEvaluator 네임스페이스 추가
using VBMS.ViewModels.Components;
using VBMS.Views;

namespace VBMS.ViewModels
{
    public partial class RackViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _rackName = string.Empty;

        [ObservableProperty]
        private double _temperature;

        [ObservableProperty]
        private int _gridColumns = 16; // 연 (Bay)

        [ObservableProperty]
        private int _gridRows = 16;    // 단 (Level, 0-based)

        [ObservableProperty]
        private string _rackStatusText = "대기";

        [ObservableProperty]
        private string _rackStatusBgColor = "#ECEFF1";

        [ObservableProperty]
        private string _rackStatusFgColor = "#607D8B";

        [ObservableProperty]
        private bool _isFireAlarm;

        [ObservableProperty]
        private string _borderColor = "Transparent";

        [ObservableProperty]
        private double _borderThickness = 2;

        public List<int> BoardIds { get; private set; } = new();

        public ObservableCollection<CellViewModel> CellList { get; set; } = new();

        public ObservableCollection<string> BayLabels { get; } = new();
        public ObservableCollection<string> LevelLabels { get; } = new();

        // ⭐ XAML 및 RackDetailViewModel 연동을 위한 간소화된 연(Bay) 라벨 목록
        public IEnumerable<string> SimplifiedBayLabels =>
            GridColumns > 0
                ? Enumerable.Range(1, GridColumns).Select(i => $"{i}연")
                : Enumerable.Empty<string>();

        public RackViewModel(string name, int columns, int rows, double temp)
            : this(name, columns, rows, temp, null)
        {
        }

        public RackViewModel(string name, int columns, int rows, double temp, IEnumerable<int>? boardIds)
        {
            RackName = name;
            Temperature = temp;
            if (boardIds != null)
            {
                BoardIds = boardIds.ToList();
            }
            BuildGrid(columns, rows);
        }

        public void SetBoardIds(IEnumerable<int> boardIds)
        {
            BoardIds = boardIds?.ToList() ?? new List<int>();
            BuildGrid(GridColumns, GridRows);
        }

        [RelayCommand]
        private void OpenDetailWindow()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                var fireVerificationService = App.Services.GetRequiredService<IFireVerificationService>();
                var detailVm = new RackDetailViewModel(this, fireVerificationService);
                var detailWindow = new RackDetailWindow
                {
                    DataContext = detailVm,
                    Owner = Application.Current.MainWindow
                };

                detailWindow.ShowDialog();
            });
        }

        public void SetFireAlarmState(bool isAlarm)
        {
            IsFireAlarm = isAlarm;
            BorderColor = isAlarm ? "#F44336" : "Transparent";
            BorderThickness = 2;

            if (isAlarm)
            {
                RackStatusText = " 🚨 화재 발생 ";
                RackStatusBgColor = "#F44336";
                RackStatusFgColor = "#FFFFFF";
            }
            else
            {
                RackStatusText = "대기";
                RackStatusBgColor = "#ECEFF1";
                RackStatusFgColor = "#607D8B";
            }
        }

        public void ResizeIfNeeded(int columns, int rows)
        {
            if (GridColumns == columns && GridRows == rows && CellList.Count == columns * rows)
            {
                return;
            }
            BuildGrid(columns, rows);
        }

        private void BuildGrid(int columns, int rows)
        {
            GridColumns = columns;
            GridRows = rows;

            CellList.Clear();
            for (int level = 0; level < rows; level++)
            {
                for (int bay = 1; bay <= columns; bay++)
                {
                    CellList.Add(new CellViewModel
                    {
                        Bay = bay,
                        Level = level,
                        BoardId = 0,
                        LocalBay = 0,
                        DetectorKey = string.Empty,
                        CellColor = "#9E9E9E",
                        CellTooltip = $"[ 위치: {bay}연 {level}단 ] 수신 대기 중..."
                    });
                }
            }
            BayLabels.Clear();
            for (int bay = 1; bay <= columns; bay++)
            {
                if (bay == 1 || bay % 5 == 0)
                {
                    BayLabels.Add($"{bay}연");
                }
                else
                {
                    BayLabels.Add(string.Empty);
                }
            }

            LevelLabels.Clear();
            for (int level = rows - 1; level >= 0; level--)
            {
                LevelLabels.Add($"{level:00}단");
            }

            // GridColumns가 변경되었음을 UI에 통지
            OnPropertyChanged(nameof(SimplifiedBayLabels));
        }

        /// <summary>
        /// ⭐ 인자 3개 지원 오버로드 메서드
        /// 수신 데이터를 받아 CellViewModel 상태를 갱신하고, 해당 랙 내 화재 발생 여부를 반환합니다.
        /// </summary>
        public bool UpdateDetectorCells(int bayOffset, List<DetectorData> detectors, IFireSignalEvaluator? evaluator)
        {
            if (detectors == null || detectors.Count == 0 || CellList.Count == 0)
            {
                return false;
            }

            bool hasAnyFire = false;

            foreach (var det in detectors)
            {
                int globalBay = det.Bay + bayOffset;
                int globalLevel = det.Level;

                var targetCell = CellList.FirstOrDefault(c => c.Bay == globalBay && c.Level == globalLevel);
                if (targetCell == null) continue;

                // 감지기 정보 할당
                targetCell.BoardId = det.BoardId;
                targetCell.LocalBay = det.Bay;
                targetCell.Level = det.Level;
                targetCell.DetectorKey = $"{det.BoardId:D3}_{det.Bay:D2}_{det.Level:D2}";

                // 신호 평가 (2: 고온/온도화재, 1: 연기화재, 0: 정상)
                byte fireStatus = evaluator?.Evaluate(det.Status, det.Temperature) ?? 0;
                if (fireStatus == 1 || fireStatus == 2)
                {
                    hasAnyFire = true;
                }

                // 셀 색상 할당
                if (det.Status == 3)
                {
                    targetCell.CellColor = "#FFC107"; // 통신/연결 오류 (노랑)
                }
                else if (fireStatus == 2)
                {
                    targetCell.CellColor = "#F44336"; // 온도 화재 (빨강)
                }
                else if (fireStatus == 1)
                {
                    targetCell.CellColor = "#FF9800"; // 연기 감지 (주황)
                }
                else if (det.Status == 0 && fireStatus == 0)
                {
                    targetCell.CellColor = "#4CAF50"; // 정상 (초록)
                }
                else
                {
                    targetCell.CellColor = "#9E9E9E"; // 미연결 (회색)
                }

                // 툴팁 텍스트 설정
                string statusText = det.Status == 3 ? "통신오류" : fireStatus switch
                {
                    2 => "화재(온도)",
                    1 => "화재(연기)",
                    _ => det.Status == 0 ? "정상" : "미연결"
                };

                targetCell.CellTooltip = $"[ 위치: {targetCell.Bay}연 {targetCell.Level}단 ]\n• 센서 ID: #{det.Index}\n• 상태: {statusText}\n• 온도: {det.Temperature:F1}℃";
            }

            return hasAnyFire;
        }
    }
}