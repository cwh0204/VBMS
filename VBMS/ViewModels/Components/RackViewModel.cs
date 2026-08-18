using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input; // ⭐ [Step 2] RelayCommand 사용을 위해 추가
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows; // ⭐ [Step 2] Application, Window 참조용
using VBMS.Models;
using VBMS.Views; // ⭐ [Step 2] RackDetailWindow 참조용
using VBMS.ViewModels.Components;

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

        // ⭐ 화재 경보 및 테두리 강조용 프로퍼티 추가
        [ObservableProperty]
        private bool _isFireAlarm;

        [ObservableProperty]
        private string _borderColor = "Transparent"; // 기본 테두리 색상 (어두운 회색)

        [ObservableProperty]
        private double _borderThickness = 2;     // 기본 테두리 두께

        public ObservableCollection<CellViewModel> CellList { get; set; } = new();

        // 축 라벨 (엑셀표 스타일 헤더용)
        public ObservableCollection<string> BayLabels { get; } = new();   // 좌→우: "1연","2연",...,"N연"
        public ObservableCollection<string> LevelLabels { get; } = new(); // 위→아래: 최상단이 먼저 오도록 내림차순 ("15단"...."00단")

        public RackViewModel(string name, int columns, int rows, double temp)
        {
            RackName = name;
            Temperature = temp;
            BuildGrid(columns, rows);
        }

        [RelayCommand]
        private void OpenDetailWindow()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                // 상세창 ViewModel 및 Window 생성
                var detailVm = new RackDetailViewModel(this);
                var detailWindow = new RackDetailWindow
                {
                    DataContext = detailVm,
                    Owner = Application.Current.MainWindow // 메인 윈도우의 자식 팝업으로 지정
                };

                // 모달 팝업으로 열기
                detailWindow.ShowDialog();
            });
        }

        /// <summary>
        /// ⭐ 화재 감지 상태에 따라 랙 테두리 색상, 두께 및 상태 텍스트를 업데이트합니다.
        /// </summary>
        public void SetFireAlarmState(bool isAlarm)
        {
            IsFireAlarm = isAlarm;
            BorderColor = isAlarm ? "#F44336" : "Transparent";
            BorderThickness = 2; // 두께를 2로 고정하여 레이아웃 크기가 바뀌는 현상(깜빡임) 방지

            if (isAlarm)
            {
                RackStatusText = " 🚨 화재 발생 ";
                RackStatusBgColor = "#F44336";
                RackStatusFgColor = "#FFFFFF";
            }
            else
            {
                RackStatusText = "🚨 화재 발생";
                RackStatusBgColor = "#ECEFF1";
                RackStatusFgColor = "#607D8B";
            }
        }

        /// <summary>
        /// 실제 장비 패킷 헤더(MaxLine/MaxStage)에 맞춰 그리드 크기를 다시 잡습니다.
        /// 기존 그리드 크기와 같으면 아무 것도 하지 않습니다 (매 패킷마다 재생성 방지).
        /// </summary>
        public void ResizeIfNeeded(int columns, int rows)
        {
            System.Diagnostics.Debug.WriteLine($"[ResizeCheck] 현재:{GridColumns}x{GridRows} -> 요청:{columns}x{rows}");
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
                        CellColor = "#9E9E9E",
                        CellTooltip = $"[ 위치: {bay}연 {level}단 ] 미연결"
                    });
                }
            }

            // ⭐ 연(Bay) 라벨: 1연 및 5단위(5연, 10연, 15연...)에만 텍스트를 넣고 나머지는 빈 문자열 처리
            BayLabels.Clear();
            for (int bay = 1; bay <= columns; bay++)
            {
                if (bay == 1 || bay % 5 == 0)
                {
                    BayLabels.Add($"{bay}연");
                }
                else
                {
                    BayLabels.Add(string.Empty); // 칸 위치 보정을 위해 빈 값 추가
                }
            }

            LevelLabels.Clear();
            for (int level = rows - 1; level >= 0; level--)
            {
                LevelLabels.Add($"{level:00}단");
            }
        }

        /// <summary>
        /// CRP 장비 패킷의 Detectors 데이터를 Bay Offset 반영하여 화면 셀에 업데이트합니다.
        /// </summary>
        public void UpdateDetectorCells(int bayOffset, List<DetectorData> detectors)
        {
            if (detectors == null || detectors.Count == 0 || CellList.Count == 0) return;

            foreach (var det in detectors)
            {
                // 1. CRP 내부 Bay(1~16) + Offset = 화면 전체 Bay (1-based)
                int globalBay = det.Bay + bayOffset;

                // 2. Level(단) 보정 (0-based)
                int globalLevel = (det.Level >= 13) ? det.Level - 1 : det.Level;

                // 3. 현재 화면 그리드(70x13) 범위를 벗어나는 경우 예외 처리
                if (globalBay < 1 || globalBay > GridColumns || globalLevel < 0 || globalLevel >= GridRows)
                    continue;

                // 4. 셀 목록에서 해당 위치(Bay, Level) 셀 검색
                var targetCell = CellList.FirstOrDefault(c => c.Bay == globalBay && c.Level == globalLevel);
                if (targetCell != null)
                {
                    // 5. 상태 및 온도에 따른 색상/툴팁 변경
                    string color = "#4CAF50"; // 기본 정상 (초록)

                    if (det.Temperature >= 60.0 || det.Status == 2)
                    {
                        color = "#F44336"; // 화재/위험 (빨강)
                    }
                    else if (det.Status == 1)
                    {
                        color = "#FF9800"; // 경고/주의 (주황)
                    }

                    targetCell.CellColor = color;
                    targetCell.CellTooltip = $"[ 위치: {globalBay}연 {globalLevel:00}단 ] 온도: {det.Temperature:F1}℃ / 상태: {det.Status}";
                }
            }
        }
    }
}