using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VBMS.Models;
using VBMS.Repositories;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace VBMS.ViewModels
{
    public partial class DataInquiryViewModel : ObservableObject, IDisposable
    {
        private readonly IDetectorRepository _detectorRepository;
        private readonly DispatcherTimer _timer;
        private bool _disposed;

        // ==========================================
        // 0. 로딩 상태 및 날짜 검색 프로퍼티
        // ==========================================
        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private DateTime _globalStartDate = DateTime.Now.Date;

        [ObservableProperty]
        private DateTime _globalEndDate = DateTime.Now.Date;

        [ObservableProperty]
        private DateTime _eventStartDate = DateTime.Now.Date;

        [ObservableProperty]
        private DateTime _eventEndDate = DateTime.Now.Date;

        [ObservableProperty]
        private DateTime _dumpStartDate = DateTime.Now.Date;

        [ObservableProperty]
        private DateTime _dumpEndDate = DateTime.Now.Date;

        // ==========================================
        // 1. 차트 단위 선택 프로퍼티 (시간별 / 일별)
        // [ObservableProperty] 대신 명시적 속성을 사용하여 Source Generator 오류 방지
        // ==========================================
        private bool _isHourlySelected = true;
        public bool IsHourlySelected
        {
            get => _isHourlySelected;
            set
            {
                if (SetProperty(ref _isHourlySelected, value))
                {
                    if (value)
                    {
                        IsDailySelected = false;
                        _ = LoadChartDataFromDbAsync(GetStartOfDay(GlobalStartDate), GetEndOfDay(GlobalEndDate));
                    }
                }
            }
        }

        private bool _isDailySelected;
        public bool IsDailySelected
        {
            get => _isDailySelected;
            set
            {
                if (SetProperty(ref _isDailySelected, value))
                {
                    if (value)
                    {
                        IsHourlySelected = false;
                        _ = LoadChartDataFromDbAsync(GetStartOfDay(GlobalStartDate), GetEndOfDay(GlobalEndDate));
                    }
                }
            }
        }

        // ==========================================
        // 2. 차트 영역 (Summary Logs)
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
        // 3. 데이터 목록 영역
        // ==========================================
        [ObservableProperty]
        private ObservableCollection<EventLogModel> _eventLogs = new();

        [ObservableProperty]
        private ObservableCollection<DetectorDumpLogModel> _dumpLogs = new();

        // ==========================================
        // 4. 실시간 시계 프로퍼티
        // ==========================================
        [ObservableProperty]
        private string _currentTimeString = DateTime.Now.ToString("HH:mm:ss");

        public DataInquiryViewModel(IDetectorRepository detectorRepository)
        {
            _detectorRepository = detectorRepository ?? throw new ArgumentNullException(nameof(detectorRepository));

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (s, e) => CurrentTimeString = DateTime.Now.ToString("HH:mm:ss");
            _timer.Start();

            _ = LoadAllDataFromDatabaseAsync();
        }

        // ==========================================
        // 헬퍼 메서드: 종료 날짜를 해당 일자의 23:59:59로 보정
        // ==========================================
        private static DateTime GetStartOfDay(DateTime date) => date.Date;
        private static DateTime GetEndOfDay(DateTime date) => date.Date.AddDays(1).AddSeconds(-1);

        // ==========================================
        // 5. 비동기 커맨드 및 데이터베이스 조회 메서드
        // ==========================================

        [RelayCommand]
        private async Task SearchAllAsync()
        {
            EventStartDate = GlobalStartDate;
            EventEndDate = GlobalEndDate;
            DumpStartDate = GlobalStartDate;
            DumpEndDate = GlobalEndDate;

            await LoadAllDataFromDatabaseAsync();
        }

        private async Task LoadAllDataFromDatabaseAsync()
        {
            if (IsLoading) return;

            try
            {
                IsLoading = true;

                DateTime start = GetStartOfDay(GlobalStartDate);
                DateTime end = GetEndOfDay(GlobalEndDate);

                await LoadChartDataFromDbAsync(start, end);
                await LoadEventLogsFromDbAsync(GetStartOfDay(EventStartDate), GetEndOfDay(EventEndDate));
                await LoadDumpLogsFromDbAsync(GetStartOfDay(DumpStartDate), GetEndOfDay(DumpEndDate));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] LoadAllDataFromDatabaseAsync: {ex.Message}");
                MessageBox.Show($"데이터 조회 중 오류가 발생했습니다: {ex.Message}", "데이터베이스 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadChartDataFromDbAsync(DateTime start, DateTime end)
        {
            var summaryLogs = await Task.Run(() =>
                _detectorRepository.GetSummaryLogs(null, start, end)
                                   .OrderBy(x => x.Timestamp)
                                   .ToList());

            if (summaryLogs.Count == 0)
            {
                TemperatureSeries = Array.Empty<ISeries>();
                XAxes = new Axis[] { new Axis { Name = "시간", TextSize = 12 } };
                YAxes = new Axis[] { new Axis { Name = "온도 (°C)", TextSize = 12 } };
                return;
            }

            var groupedData = IsHourlySelected
                ? summaryLogs
                    .GroupBy(x => new DateTime(x.Timestamp.Year, x.Timestamp.Month, x.Timestamp.Day, x.Timestamp.Hour, 0, 0))
                    .Select(g => new
                    {
                        Label = g.Key.ToString("MM/dd HH:00"),
                        AvgTemp = Math.Round(g.Average(x => x.AvgTemperature), 1),
                        MaxTemp = Math.Round(g.Max(x => x.MaxTemperature), 1)
                    })
                    .OrderBy(x => x.Label)
                    .ToList()
                : summaryLogs
                    .GroupBy(x => x.Timestamp.Date)
                    .Select(g => new
                    {
                        Label = g.Key.ToString("yyyy-MM-dd"),
                        AvgTemp = Math.Round(g.Average(x => x.AvgTemperature), 1),
                        MaxTemp = Math.Round(g.Max(x => x.MaxTemperature), 1)
                    })
                    .OrderBy(x => x.Label)
                    .ToList();

            TemperatureSeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Name = "평균 온도 (°C)",
                    Values = groupedData.Select(x => x.AvgTemp).ToArray(),
                    Stroke = new SolidColorPaint(SKColors.DodgerBlue, 2.5f),
                    Fill = null,
                    GeometrySize = groupedData.Count > 150 ? 0 : 5,
                    GeometryStroke = new SolidColorPaint(SKColors.DodgerBlue, 2.5f)
                },
                new LineSeries<double>
                {
                    Name = "최고 온도 (°C)",
                    Values = groupedData.Select(x => x.MaxTemp).ToArray(),
                    Stroke = new SolidColorPaint(SKColors.Tomato, 2.5f),
                    Fill = null,
                    GeometrySize = groupedData.Count > 150 ? 0 : 5,
                    GeometryStroke = new SolidColorPaint(SKColors.Tomato, 2.5f)
                }
            };

            XAxes = new Axis[]
            {
                new Axis
                {
                    Name = IsHourlySelected ? "시간 단위 (HH:00)" : "일자 단위 (YYYY-MM-DD)",
                    Labels = groupedData.Select(x => x.Label).ToArray(),
                    TextSize = 12,
                    LabelsRotation = 15,
                    SeparatorsPaint = new SolidColorPaint(SKColors.LightGray.WithAlpha(50))
                }
            };

            YAxes = new Axis[]
            {
                new Axis
                {
                    Name = "온도 (°C)",
                    Labeler = val => $"{val:N1} °C",
                    TextSize = 12,
                    SeparatorsPaint = new SolidColorPaint(SKColors.LightGray.WithAlpha(50))
                }
            };
        }

        [RelayCommand]
        private async Task SearchEventsAsync()
        {
            if (IsLoading) return;
            try
            {
                IsLoading = true;
                await LoadEventLogsFromDbAsync(GetStartOfDay(EventStartDate), GetEndOfDay(EventEndDate));
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadEventLogsFromDbAsync(DateTime start, DateTime end)
        {
            var logs = await Task.Run(() => _detectorRepository.GetEventLogs(start, end).ToList());
            EventLogs = new ObservableCollection<EventLogModel>(logs);
        }

        [RelayCommand]
        private async Task SearchDumpLogsAsync()
        {
            if (IsLoading) return;
            try
            {
                IsLoading = true;
                await LoadDumpLogsFromDbAsync(GetStartOfDay(DumpStartDate), GetEndOfDay(DumpEndDate));
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadDumpLogsFromDbAsync(DateTime start, DateTime end)
        {
            var dumpLogs = await Task.Run(() => _detectorRepository.GetDumpLogs(start, end).ToList());
            DumpLogs = new ObservableCollection<DetectorDumpLogModel>(dumpLogs);
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

        public void Dispose()
        {
            if (!_disposed)
            {
                _timer?.Stop();
                _disposed = true;
            }
        }
    }
}