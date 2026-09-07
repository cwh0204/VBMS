using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Win32;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VBMS.Models;
using VBMS.Repositories;
using VBMS.Services.Export;
using VBMS.Services.Theme;

namespace VBMS.ViewModels
{
    public partial class DataInquiryViewModel : ObservableObject, IDisposable
    {
        private readonly IDetectorRepository _detectorRepository;
        private readonly IExcelExportService _excelExportService;
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

        // 범례 글자색 바인딩 프로퍼티 (테마 호환)
        [ObservableProperty]
        private SolidColorPaint? _legendTextPaint;

        // ==========================================
        // 2. 데이터 목록 영역
        // ==========================================
        [ObservableProperty]
        private ObservableCollection<EventLogModel> _eventLogs = new();

        [ObservableProperty]
        private ObservableCollection<DetectorDumpLogModel> _dumpLogs = new();

        // ==========================================
        // 3. 실시간 시계 프로퍼티
        // ==========================================
        [ObservableProperty]
        private string _currentTimeString = DateTime.Now.ToString("HH:mm:ss");

        public DataInquiryViewModel(
            IDetectorRepository detectorRepository,
            IExcelExportService excelExportService)
        {
            _detectorRepository = detectorRepository ?? throw new ArgumentNullException(nameof(detectorRepository));
            _excelExportService = excelExportService ?? throw new ArgumentNullException(nameof(excelExportService));

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (s, e) => CurrentTimeString = DateTime.Now.ToString("HH:mm:ss");
            _timer.Start();

            _ = LoadAllDataFromDatabaseAsync();
        }

        private static DateTime GetStartOfDay(DateTime date) => date.Date;
        private static DateTime GetEndOfDay(DateTime date) => date.Date.AddDays(1).AddSeconds(-1);

        // ==========================================
        // 테마별 SkiaSharp 차트 브러시 생성 헬퍼
        // ==========================================
        private (SolidColorPaint textPaint, SolidColorPaint namePaint, SolidColorPaint separatorPaint) GetThemePaints()
        {
            bool isDark = ThemeService.Instance.CurrentTheme == AppTheme.Dark;

            // 다크모드: 밝은 실버그레이 / 라이트모드: 차콜 슬레이트
            var textPaint = new SolidColorPaint(isDark ? new SKColor(226, 232, 240) : new SKColor(51, 65, 85));
            var namePaint = new SolidColorPaint(isDark ? new SKColor(148, 163, 184) : new SKColor(100, 116, 139));
            var separatorPaint = new SolidColorPaint(isDark ? new SKColor(71, 85, 105, 80) : new SKColor(226, 232, 240));

            LegendTextPaint = textPaint;

            return (textPaint, namePaint, separatorPaint);
        }

        // ==========================================
        // 4. 비동기 커맨드 및 데이터베이스 조회 메서드
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
            var (textPaint, namePaint, separatorPaint) = GetThemePaints();

            var rawLogs = await _detectorRepository.GetSummaryLogsAsync(null, start, end);
            var summaryLogs = rawLogs.OrderBy(x => x.Timestamp).ToList();

            if (summaryLogs.Count == 0)
            {
                TemperatureSeries = Array.Empty<ISeries>();
                XAxes = new Axis[]
                {
                    new Axis
                    {
                        Name = "측정 일시",
                        TextSize = 12,
                        NamePaint = namePaint,
                        LabelsPaint = textPaint,
                        SeparatorsPaint = separatorPaint
                    }
                };
                YAxes = new Axis[]
                {
                    new Axis
                    {
                        Name = "온도 (°C)",
                        TextSize = 12,
                        NamePaint = namePaint,
                        LabelsPaint = textPaint,
                        SeparatorsPaint = separatorPaint
                    }
                };
                return;
            }

            DateTime minTimestamp = summaryLogs.First().Timestamp;
            DateTime maxTimestamp = summaryLogs.Last().Timestamp;
            TimeSpan actualDataSpan = maxTimestamp - minTimestamp;

            // 데이터 구간에 맞추어 유연하게 자동 그룹화
            var groupedData = actualDataSpan switch
            {
                var d when d.TotalHours <= 2 => summaryLogs
                    .GroupBy(x => new DateTime(x.Timestamp.Year, x.Timestamp.Month, x.Timestamp.Day, x.Timestamp.Hour, x.Timestamp.Minute, 0))
                    .Select(g => new
                    {
                        Label = g.Key.ToString("HH:mm"),
                        AvgTemp = Math.Round(g.Average(x => x.AvgTemperature), 1),
                        MaxTemp = Math.Round(g.Max(x => x.MaxTemperature), 1)
                    }),

                var d when d.TotalHours <= 24 => summaryLogs
                    .GroupBy(x => new DateTime(x.Timestamp.Year, x.Timestamp.Month, x.Timestamp.Day, x.Timestamp.Hour, (x.Timestamp.Minute / 10) * 10, 0))
                    .Select(g => new
                    {
                        Label = g.Key.ToString("HH:mm"),
                        AvgTemp = Math.Round(g.Average(x => x.AvgTemperature), 1),
                        MaxTemp = Math.Round(g.Max(x => x.MaxTemperature), 1)
                    }),

                var d when d.TotalDays <= 7 => summaryLogs
                    .GroupBy(x => new DateTime(x.Timestamp.Year, x.Timestamp.Month, x.Timestamp.Day, x.Timestamp.Hour, 0, 0))
                    .Select(g => new
                    {
                        Label = g.Key.ToString("MM/dd HH:00"),
                        AvgTemp = Math.Round(g.Average(x => x.AvgTemperature), 1),
                        MaxTemp = Math.Round(g.Max(x => x.MaxTemperature), 1)
                    }),

                var d when d.TotalDays <= 90 => summaryLogs
                    .GroupBy(x => x.Timestamp.Date)
                    .Select(g => new
                    {
                        Label = g.Key.ToString("yyyy-MM-dd"),
                        AvgTemp = Math.Round(g.Average(x => x.AvgTemperature), 1),
                        MaxTemp = Math.Round(g.Max(x => x.MaxTemperature), 1)
                    }),

                _ => summaryLogs
                    .GroupBy(x => new DateTime(x.Timestamp.Year, x.Timestamp.Month, 1))
                    .Select(g => new
                    {
                        Label = g.Key.ToString("yyyy-MM"),
                        AvgTemp = Math.Round(g.Average(x => x.AvgTemperature), 1),
                        MaxTemp = Math.Round(g.Max(x => x.MaxTemperature), 1)
                    })
            };

            var resultList = groupedData.OrderBy(x => x.Label).ToList();

            var avgColor = new SKColor(14, 165, 233); // Sky Blue
            var maxColor = new SKColor(239, 68, 68);  // Coral Red

            TemperatureSeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Name = "평균 온도 (°C)",
                    Values = resultList.Select(x => x.AvgTemp).ToArray(),
                    Stroke = new SolidColorPaint(avgColor, 2.5f),
                    Fill = null,
                    GeometrySize = resultList.Count > 150 ? 0 : 5,
                    GeometryStroke = new SolidColorPaint(avgColor, 2.5f)
                },
                new LineSeries<double>
                {
                    Name = "최고 온도 (°C)",
                    Values = resultList.Select(x => x.MaxTemp).ToArray(),
                    Stroke = new SolidColorPaint(maxColor, 2.5f),
                    Fill = null,
                    GeometrySize = resultList.Count > 150 ? 0 : 5,
                    GeometryStroke = new SolidColorPaint(maxColor, 2.5f)
                }
            };

            XAxes = new Axis[]
            {
                new Axis
                {
                    Name = "측정 일시",
                    NamePaint = namePaint,
                    Labels = resultList.Select(x => x.Label).ToArray(),
                    LabelsPaint = textPaint,
                    TextSize = 11,
                    LabelsRotation = 15,
                    SeparatorsPaint = separatorPaint
                }
            };

            YAxes = new Axis[]
            {
                new Axis
                {
                    Name = "온도 (°C)",
                    NamePaint = namePaint,
                    LabelsPaint = textPaint,
                    Labeler = val => $"{val:N1} °C",
                    TextSize = 11,
                    SeparatorsPaint = separatorPaint
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
            var logs = await _detectorRepository.GetEventLogsAsync(start, end);
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
            var dumpLogs = await _detectorRepository.GetDumpLogsAsync(start, end);
            DumpLogs = new ObservableCollection<DetectorDumpLogModel>(dumpLogs);
        }

        // ==========================================
        // 5. 엑셀 내보내기 커맨드 (IExcelExportService 연동)
        // ==========================================

        [RelayCommand]
        private async Task ExportEventsExcelAsync()
        {
            if (EventLogs == null || EventLogs.Count == 0)
            {
                MessageBox.Show("저장할 이벤트 로그 데이터가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string defaultDir = Directory.Exists(@"D:\gtcsfile")
                ? @"D:\gtcsfile"
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            var dialog = new SaveFileDialog
            {
                Title = "이벤트 로그 엑셀 저장",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx",
                FileName = $"VBMS_이벤트로그_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                InitialDirectory = defaultDir
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                IsLoading = true;
                await _excelExportService.ExportEventLogsAsync(dialog.FileName, EventLogs);
                MessageBox.Show("이벤트 로그 엑셀 저장이 완료되었습니다.", "성공", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"엑셀 저장 중 오류가 발생했습니다: {ex.Message}", "저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task ExportDumpExcelAsync()
        {
            if (DumpLogs == null || DumpLogs.Count == 0)
            {
                MessageBox.Show("저장할 이상징후 덤프 로그 데이터가 없습니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string defaultDir = Directory.Exists(@"D:\gtcsfile")
                ? @"D:\gtcsfile"
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            var dialog = new SaveFileDialog
            {
                Title = "이상징후 정밀 덤프 로그 엑셀 저장",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx",
                FileName = $"VBMS_덤프로그_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                InitialDirectory = defaultDir
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                IsLoading = true;
                await _excelExportService.ExportDumpLogsAsync(dialog.FileName, DumpLogs);
                MessageBox.Show("이상징후 덤프 로그 엑셀 저장이 완료되었습니다.", "성공", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"엑셀 저장 중 오류가 발생했습니다: {ex.Message}", "저장 실패", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _timer.Stop();
                _disposed = true;
            }
        }
    }
}