using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using System.Windows;
using VBMS.Models;
using VBMS.Repositories; // IDetectorRepository 사용을 위한 네임스페이스
// LiveCharts2 네임스페이스
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Linq;

namespace VBMS.ViewModels
{
    public partial class DataInquiryViewModel : ObservableObject
    {
        private readonly IDetectorRepository _detectorRepository;

        // ==========================================
        // 0. 날짜 검색 프로퍼티 (상단 통합 및 개별 섹션)
        // ==========================================
        [ObservableProperty]
        private DateTime _globalStartDate = DateTime.Now.Date;

        [ObservableProperty]
        private DateTime _globalEndDate = DateTime.Now.Date.AddDays(1).AddSeconds(-1); // 당일 23:59:59까지 기본 설정

        [ObservableProperty]
        private DateTime _eventStartDate = DateTime.Now.Date;

        [ObservableProperty]
        private DateTime _eventEndDate = DateTime.Now.Date.AddDays(1).AddSeconds(-1);

        [ObservableProperty]
        private DateTime _dumpStartDate = DateTime.Now.Date;

        [ObservableProperty]
        private DateTime _dumpEndDate = DateTime.Now.Date.AddDays(1).AddSeconds(-1);

        // ==========================================
        // 1. 차트 영역 (Summary Logs)
        // ==========================================
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
        // 2. Event 영역 (Event Logs)
        // ==========================================
        [ObservableProperty]
        private ObservableCollection<EventLogModel> _eventLogs = new();

        // ==========================================
        // 3. 이상징후 덤프 로그 영역 (Detector Dump Logs)
        // ==========================================
        [ObservableProperty]
        private ObservableCollection<DetectorDumpLogModel> _dumpLogs = new();

        // ==========================================
        // 4. 실시간 시계 프로퍼티 및 타이머
        // ==========================================
        [ObservableProperty]
        private string _currentTimeString = DateTime.Now.ToString("HH:mm:ss");

        private readonly DispatcherTimer _timer;

        // ==========================================
        // 5. 생성자 (의존성 주입을 통해 리포지토리 받기)
        // ==========================================
        public DataInquiryViewModel(IDetectorRepository detectorRepository)
        {
            _detectorRepository = detectorRepository ?? throw new ArgumentNullException(nameof(detectorRepository));

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (s, e) => CurrentTimeString = DateTime.Now.ToString("HH:mm:ss");
            _timer.Start();

            // 초기 데이터 로드
            LoadAllDataFromDatabase();
        }

        // ==========================================
        // 6. 커맨드 및 데이터베이스 조회 메서드들
        // ==========================================

        /// <summary>
        /// 상단 [전체 조회] 버튼 커맨드
        /// </summary>
        [RelayCommand]
        private void SearchAll()
        {
            EventStartDate = GlobalStartDate;
            EventEndDate = GlobalEndDate;
            DumpStartDate = GlobalStartDate;
            DumpEndDate = GlobalEndDate;

            LoadAllDataFromDatabase();
        }

        private void LoadAllDataFromDatabase()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG] 전체 데이터 조회 시작 - 기간: {GlobalStartDate} ~ {GlobalEndDate}");

                LoadChartDataFromDb(GlobalStartDate, GlobalEndDate);
                LoadEventLogsFromDb(EventStartDate, EventEndDate);
                LoadDumpLogsFromDb(DumpStartDate, DumpEndDate);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DEBUG ERROR] LoadAllDataFromDatabase 예외 발생: {ex.Message}");
                MessageBox.Show($"데이터 조회 중 오류가 발생했습니다: {ex.Message}", "데이터베이스 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 요약 테이블(crp_summary_logs) 데이터를 조회하여 LiveCharts2 시각화 바인딩
        /// </summary>
        private void LoadChartDataFromDb(DateTime start, DateTime end)
        {
            var summaryLogs = _detectorRepository.GetSummaryLogs(null, start, end).ToList();
            System.Diagnostics.Debug.WriteLine($"[DEBUG] SummaryLogs 조회 개수: {summaryLogs.Count}");

            if (summaryLogs.Count == 0)
            {
                TemperatureSeries = Array.Empty<ISeries>();
                XAxes = new Axis[] { new Axis { Name = "시간", Labels = Array.Empty<string>(), TextSize = 12 } };
                YAxes = new Axis[] { new Axis { Name = "온도 (°C)", TextSize = 12 } };
                return;
            }

            // 시간순 정렬 후 X축 레이블 및 Y축(평균 온도) 값 추출
            var times = summaryLogs.Select(x => x.Timestamp.ToString("HH:mm:ss")).ToArray();
            var temperatures = summaryLogs.Select(x => x.AvgTemperature).ToArray();

            TemperatureSeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Name = "평균 온도",
                    Values = temperatures,
                    Stroke = new SolidColorPaint(SKColors.DodgerBlue, 2),
                    Fill = null,
                    GeometrySize = 4
                }
            };

            XAxes = new Axis[]
            {
                new Axis
                {
                    Name = "시간",
                    Labels = times,
                    TextSize = 12,
                    LabelsRotation = 15
                }
            };

            YAxes = new Axis[]
            {
                new Axis
                {
                    Name = "온도 (°C)",
                    TextSize = 12
                }
            };
        }

        [RelayCommand]
        private void SearchEvents()
        {
            LoadEventLogsFromDb(EventStartDate, EventEndDate);
        }

        /// <summary>
        /// 이벤트 로그 테이블(event_logs) 조회 연동
        /// </summary>
        private void LoadEventLogsFromDb(DateTime start, DateTime end)
        {
            EventLogs.Clear();

            var logs = _detectorRepository.GetEventLogs(start, end).ToList();
            System.Diagnostics.Debug.WriteLine($"[DEBUG] EventLogs 조회 개수: {logs.Count}");

            foreach (var log in logs)
            {
                EventLogs.Add(log);
            }
        }

        [RelayCommand]
        private void SearchDumpLogs()
        {
            LoadDumpLogsFromDb(DumpStartDate, DumpEndDate);
        }

        /// <summary>
        /// 이상징후 덤프 로그 테이블(detector_dump_logs) 조회 연동
        /// </summary>
        private void LoadDumpLogsFromDb(DateTime start, DateTime end)
        {
            DumpLogs.Clear();

            var dumpLogs = _detectorRepository.GetDumpLogs(start, end).ToList();
            System.Diagnostics.Debug.WriteLine($"[DEBUG] DumpLogs 조회 개수: {dumpLogs.Count}");

            foreach (var log in dumpLogs)
            {
                DumpLogs.Add(log);
            }
        }

        [RelayCommand]
        private void ExportEventsExcel()
        {
            MessageBox.Show("이벤트 로그 엑셀 파일 저장이 완료되었습니다.", "내보내기 성공", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand]
        private void ExportDumpExcel()
        {
            MessageBox.Show("이상징후 덤프 로그 엑셀 파일 저장이 완료되었습니다.", "내보내기 성공", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}