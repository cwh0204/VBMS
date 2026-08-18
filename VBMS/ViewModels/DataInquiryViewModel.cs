using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using VBMS.Models;
// LiveCharts2 네임스페이스
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace VBMS.ViewModels
{
    public partial class DataInquiryViewModel : ObservableObject
    {
        // ==========================================
        // 1. 차트 영역
        // ==========================================
        [ObservableProperty]
        private DateTime _chartStartDate = DateTime.Now.Date;

        [ObservableProperty]
        private DateTime _chartEndDate = DateTime.Now.Date;

        // 소스 생성기 충돌 방지를 위해 명시적 프로퍼티로 선언
        private ISeries[] _temperatureSeries = Array.Empty<ISeries>();
        public ISeries[] TemperatureSeries
        {
            get => _temperatureSeries;
            set => SetProperty(ref _temperatureSeries, value);
        }

        private Axis[] _xAxes = Array.Empty<Axis>();
        public Axis[] XAxes
        {
            get => _xAxes;
            set => SetProperty(ref _xAxes, value);
        }

        private Axis[] _yAxes = Array.Empty<Axis>();
        public Axis[] YAxes
        {
            get => _yAxes;
            set => SetProperty(ref _yAxes, value);
        }

        // ==========================================
        // 2. Event 영역
        // ==========================================
        [ObservableProperty]
        private DateTime _eventStartDate = DateTime.Now.Date;

        [ObservableProperty]
        private DateTime _eventEndDate = DateTime.Now.Date;

        [ObservableProperty]
        private ObservableCollection<EventLogModel> _eventLogs = new();

        // ==========================================
        // 3. 화재보고 영역
        // ==========================================
        [ObservableProperty]
        private DateTime _fireStartDate = DateTime.Now.Date;

        [ObservableProperty]
        private DateTime _fireEndDate = DateTime.Now.Date;

        [ObservableProperty]
        private ObservableCollection<EventLogModel> _fireReports = new();

        // ==========================================
        // 4. 상단 실시간 시계 프로퍼티 및 타이머
        // ==========================================
        [ObservableProperty]
        private string _currentTimeString = DateTime.Now.ToString("HH:mm:ss");

        private readonly DispatcherTimer _timer;

        public DataInquiryViewModel()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (s, e) => CurrentTimeString = DateTime.Now.ToString("HH:mm:ss");
            _timer.Start();

            InitDummyChartData();
            LoadSampleData();
        }

        /// <summary>
        /// 열별 평균온도 시계열 차트 더미 데이터 설정
        /// </summary>
        private void InitDummyChartData()
        {
            var times = new[] { "10:30", "10:31", "10:32", "10:33", "10:34", "10:35", "10:36", "10:37", "10:38", "10:39" };

            TemperatureSeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Name = "상온 1열",
                    Values = new double[] { 25.1, 25.3, 25.2, 25.5, 25.8, 25.6, 25.7, 25.9, 26.0, 25.8 },
                    Stroke = new SolidColorPaint(SKColors.DodgerBlue, 2),
                    Fill = null,
                    GeometrySize = 6
                },
                new LineSeries<double>
                {
                    Name = "상온 2열",
                    Values = new double[] { 24.8, 24.9, 25.0, 25.1, 25.3, 25.2, 25.4, 25.5, 25.6, 25.5 },
                    Stroke = new SolidColorPaint(SKColors.DarkOrange, 2),
                    Fill = null,
                    GeometrySize = 6
                },
                new LineSeries<double>
                {
                    Name = "상온 3열",
                    Values = new double[] { 26.2, 26.1, 26.3, 26.5, 26.4, 26.7, 26.8, 26.6, 26.9, 27.0 },
                    Stroke = new SolidColorPaint(SKColors.MediumSeaGreen, 2),
                    Fill = null,
                    GeometrySize = 6
                },
                new LineSeries<double>
                {
                    Name = "상온 4열",
                    Values = new double[] { 23.9, 24.1, 24.0, 24.2, 24.3, 24.5, 24.4, 24.6, 24.5, 24.7 },
                    Stroke = new SolidColorPaint(SKColors.Crimson, 2),
                    Fill = null,
                    GeometrySize = 6
                }
            };

            XAxes = new Axis[]
            {
                new Axis
                {
                    Name = "시간",
                    Labels = times,
                    LabelsRotation = 0,
                    TextSize = 12
                }
            };

            YAxes = new Axis[]
            {
                new Axis
                {
                    Name = "온도 (°C)",
                    MinLimit = 20,
                    MaxLimit = 30,
                    TextSize = 12
                }
            };
        }

        private void LoadSampleData()
        {
            EventLogs.Add(new EventLogModel
            {
                DateTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"),
                Row = 1,
                Bay = 1,
                Level = 1,
                Content = "Fire (1000/3)"
            });

            FireReports.Add(new EventLogModel
            {
                DateTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"),
                Row = 1,
                Bay = 1,
                Level = 1,
                Content = "화재감지 (MANUAL->EMS)"
            });
        }

        [RelayCommand]
        private void SearchChart()
        {
            InitDummyChartData();
        }

        [RelayCommand]
        private void SearchEvents() { }

        [RelayCommand]
        private void ExportEventsExcel() { }

        [RelayCommand]
        private void SearchFireReports() { }

        [RelayCommand]
        private void ExportFireReportsExcel() { }
    }
}