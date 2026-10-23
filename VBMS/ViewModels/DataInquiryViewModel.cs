using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.DependencyInjection;
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
using VBMS.Services.Localization;
using VBMS.Services.Settings;
using VBMS.Services.Theme;

namespace VBMS.ViewModels
{
    public partial class DataInquiryViewModel : ObservableObject, IDisposable
    {
        private readonly IDetectorRepository _detectorRepository;
        private readonly IExcelExportService _excelExportService;
        private readonly ISettingsService _settingsService;
        private readonly ILocalizationService? _localizationService;
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
            IExcelExportService excelExportService,
            ISettingsService settingsService,
            ILocalizationService? localizationService = null)
        {
            _detectorRepository = detectorRepository ?? throw new ArgumentNullException(nameof(detectorRepository));
            _excelExportService = excelExportService ?? throw new ArgumentNullException(nameof(excelExportService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _localizationService = localizationService ?? App.Services.GetService<ILocalizationService>();

            if (_localizationService != null)
            {
                _localizationService.OnLanguageChanged += HandleLanguageChanged;
            }

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (s, e) => CurrentTimeString = DateTime.Now.ToString("HH:mm:ss");
            _timer.Start();

            _ = LoadAllDataFromDatabaseAsync();
        }

        private void HandleLanguageChanged(string lang)
        {
            // 언어 변경 시 차트 범례/축 라벨 갱신
            if (TemperatureSeries != null && TemperatureSeries.Length >= 2)
            {
                TemperatureSeries[0].Name = _localizationService?.GetString("Str_ChartAvgTemp") ?? "평균 온도 (°C)";
                TemperatureSeries[1].Name = _localizationService?.GetString("Str_ChartMaxTemp") ?? "최고 온도 (°C)";
            }

            if (XAxes != null && XAxes.Length > 0)
            {
                XAxes[0].Name = _localizationService?.GetString("Str_ChartTimeAxis") ?? "측정 일시";
            }

            if (YAxes != null && YAxes.Length > 0)
            {
                YAxes[0].Name = _localizationService?.GetString("Str_ChartTempAxis") ?? "온도 (°C)";
            }
        }

        private static DateTime GetStartOfDay(DateTime date) => date.Date;
        private static DateTime GetEndOfDay(DateTime date) => date.Date.AddDays(1).AddSeconds(-1);

        private (SolidColorPaint textPaint, SolidColorPaint namePaint, SolidColorPaint separatorPaint) GetThemePaints()
        {
            bool isDark = ThemeService.Instance.CurrentTheme == AppTheme.Dark;

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
                MessageBox.Show($"데이터 조회 중 오류가 발생했습니다: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

            string timeAxisName = _localizationService?.GetString("Str_ChartTimeAxis") ?? "측정 일시";
            string tempAxisName = _localizationService?.GetString("Str_ChartTempAxis") ?? "온도 (°C)";
            string avgTempName = _localizationService?.GetString("Str_ChartAvgTemp") ?? "평균 온도 (°C)";
            string maxTempName = _localizationService?.GetString("Str_ChartMaxTemp") ?? "최고 온도 (°C)";

            if (summaryLogs.Count == 0)
            {
                TemperatureSeries = Array.Empty<ISeries>();
                XAxes = new Axis[]
                {
                    new Axis
                    {
                        Name = timeAxisName,
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
                        Name = tempAxisName,
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

            var avgColor = new SKColor(14, 165, 233);
            var maxColor = new SKColor(239, 68, 68);

            TemperatureSeries = new ISeries[]
            {
                new LineSeries<double>
                {
                    Name = avgTempName,
                    Values = resultList.Select(x => x.AvgTemp).ToArray(),
                    Stroke = new SolidColorPaint(avgColor, 2.5f),
                    Fill = null,
                    GeometrySize = resultList.Count > 150 ? 0 : 5,
                    GeometryStroke = new SolidColorPaint(avgColor, 2.5f)
                },
                new LineSeries<double>
                {
                    Name = maxTempName,
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
                    Name = timeAxisName,
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
                    Name = tempAxisName,
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
        // 5. 엑셀 내보내기 커맨드 (설정 서비스 연동)
        // ==========================================

        private string GetExportDirectory()
        {
            string dir = _settingsService.CurrentSettings.ExportDirectory;
            return Directory.Exists(dir) ? dir : Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        [RelayCommand]
        private async Task ExportEventsExcelAsync()
        {
            if (EventLogs == null || EventLogs.Count == 0)
            {
                string noDataMsg = _localizationService?.GetString("Str_NoDataToExport") ?? "저장할 이벤트 로그 데이터가 없습니다.";
                MessageBox.Show(noDataMsg, "VBMS", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = _localizationService?.GetString("Str_BtnExportExcel") ?? "이벤트 로그 엑셀 저장",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx",
                FileName = $"VBMS_EventLogs_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                InitialDirectory = GetExportDirectory()
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                IsLoading = true;
                await _excelExportService.ExportEventLogsAsync(dialog.FileName, EventLogs);
                string okMsg = _localizationService?.GetString("Str_ExportSuccess") ?? "이벤트 로그 엑셀 저장이 완료되었습니다.";
                MessageBox.Show(okMsg, "VBMS", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                string failMsg = _localizationService?.GetString("Str_ExportFail") ?? "엑셀 저장 중 오류가 발생했습니다: ";
                MessageBox.Show($"{failMsg}{ex.Message}", "VBMS", MessageBoxButton.OK, MessageBoxImage.Error);
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
                string noDataMsg = _localizationService?.GetString("Str_NoDataToExport") ?? "저장할 이상징후 덤프 로그 데이터가 없습니다.";
                MessageBox.Show(noDataMsg, "VBMS", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = _localizationService?.GetString("Str_BtnExportExcel") ?? "이상징후 정밀 덤프 로그 엑셀 저장",
                Filter = "Excel 파일 (*.xlsx)|*.xlsx",
                FileName = $"VBMS_DumpLogs_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                InitialDirectory = GetExportDirectory()
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                IsLoading = true;
                await _excelExportService.ExportDumpLogsAsync(dialog.FileName, DumpLogs);
                string okMsg = _localizationService?.GetString("Str_ExportSuccess") ?? "이상징후 덤프 로그 엑셀 저장이 완료되었습니다.";
                MessageBox.Show(okMsg, "VBMS", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                string failMsg = _localizationService?.GetString("Str_ExportFail") ?? "엑셀 저장 중 오류가 발생했습니다: ";
                MessageBox.Show($"{failMsg}{ex.Message}", "VBMS", MessageBoxButton.OK, MessageBoxImage.Error);
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
                if (_localizationService != null)
                {
                    _localizationService.OnLanguageChanged -= HandleLanguageChanged;
                }
                _timer.Stop();
                _disposed = true;
            }
        }
    }
}