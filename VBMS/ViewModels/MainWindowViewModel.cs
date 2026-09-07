using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VBMS.Models;
using VBMS.Repositories;
using VBMS.Services.Communications;
using VBMS.Services.Evaluators;
using VBMS.Services.Orchestrators;
using VBMS.Services.Theme;
using VBMS.Views;

namespace VBMS.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly IFdsDataOrchestrator? _orchestrator;
        private readonly IFdsMappingService? _mappingService;
        private readonly IFireSignalEvaluator? _fireSignalEvaluator;
        private readonly IDetectorRepository? _repository;

        // UI 보드 수신 시각 및 위치 추적용
        private readonly ConcurrentDictionary<string, DateTime> _boardLastSeen = new();
        private readonly Timer _uiWatchdogTimer;

        // 중복 로그 도배 방지를 위한 센서별 이전 상태 저장소
        private readonly ConcurrentDictionary<string, int> _detectorLastStatus = new();
        // 타임아웃 로그 중복 발생 방지 플래그
        private readonly ConcurrentDictionary<string, bool> _boardTimeoutState = new();

        // 실시간 시계 및 드라이브 모니터링 타이머
        private readonly DispatcherTimer _systemMonitoringTimer;
        private int _driveUpdateCounter = 0;

        // ==========================================
        // 시스템 상태 프로퍼티 (시계 및 드라이브)
        // ==========================================
        [ObservableProperty]
        private string _currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        [ObservableProperty]
        private double _cDriveUsagePercent;

        [ObservableProperty]
        private string _cDriveStatusText = "용량 확인 중...";

        [ObservableProperty]
        private double _dDriveUsagePercent;

        [ObservableProperty]
        private string _dDriveStatusText = "용량 확인 중...";

        // ==========================================
        // 테마 및 모니터링 프로퍼티
        // ==========================================
        [ObservableProperty]
        private bool _isDarkMode;

        [ObservableProperty]
        private int _normalCount = 1050;

        [ObservableProperty]
        private int _smokeCount = 3;

        [ObservableProperty]
        private int _fireCount = 1;

        [ObservableProperty]
        private CrpPacket? _latestPacket;

        public ObservableCollection<RackViewModel> RackList { get; } = new();
        public ObservableCollection<EventLogModel> EventLogList { get; } = new();

        // 커스텀 알람 팝업 목록
        public ObservableCollection<FireAlarmPopupViewModel> ActiveAlarms { get; } = new();

        public MainWindowViewModel()
        {
            // 초기 테마 상태 동기화
            IsDarkMode = ThemeService.Instance.CurrentTheme == AppTheme.Dark;

            LoadSampleData();

            // 1. 초기 드라이브 용량 즉각 갱신
            UpdateDriveUsage();

            // 2. 1초 주기 타이머 (시계 1초 갱신, 드라이브 용량 10초 갱신)
            _systemMonitoringTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _systemMonitoringTimer.Tick += (s, e) =>
            {
                CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                if (++_driveUpdateCounter >= 10)
                {
                    _driveUpdateCounter = 0;
                    UpdateDriveUsage();
                }
            };
            _systemMonitoringTimer.Start();

            // 2초마다 UI 수신 상태 점검
            _uiWatchdogTimer = new Timer(CheckUiBoardTimeouts, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }

        public MainWindowViewModel(
            IFdsDataOrchestrator orchestrator,
            IFdsMappingService mappingService,
            IFireSignalEvaluator fireSignalEvaluator,
            IDetectorRepository repository) : this()
        {
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _mappingService = mappingService ?? throw new ArgumentNullException(nameof(mappingService));
            _fireSignalEvaluator = fireSignalEvaluator ?? throw new ArgumentNullException(nameof(fireSignalEvaluator));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));

            _orchestrator.OnPacketProcessed += HandlePacketProcessed;

            _orchestrator.OnLogMessage += (message, level) =>
            {
            };

            _ = Task.Run(async () =>
            {
                try
                {
                    await _orchestrator.StartServerAsync(5000);
                }
                catch (Exception ex)
                {
                    AddEventLog(0, 0, 0, $"[서버 오류] 서버 자동 시작 실패: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// C 및 D 드라이브의 실제 남은 용량 및 사용률을 계산합니다.
        /// </summary>
        private void UpdateDriveUsage()
        {
            UpdateSingleDrive("C", percent => CDriveUsagePercent = percent, text => CDriveStatusText = text);
            UpdateSingleDrive("D", percent => DDriveUsagePercent = percent, text => DDriveStatusText = text);
        }

        private void UpdateSingleDrive(string driveLetter, Action<double> setPercent, Action<string> setText)
        {
            try
            {
                var drive = new DriveInfo(driveLetter);
                if (drive.IsReady)
                {
                    long totalBytes = drive.TotalSize;
                    long freeBytes = drive.AvailableFreeSpace;
                    long usedBytes = totalBytes - freeBytes;

                    double totalGb = Math.Round((double)totalBytes / (1024 * 1024 * 1024), 0);
                    double freeGb = Math.Round((double)freeBytes / (1024 * 1024 * 1024), 0);
                    double usagePercent = totalBytes > 0 ? ((double)usedBytes / totalBytes) * 100.0 : 0.0;

                    setPercent(Math.Round(usagePercent, 1));
                    setText($"{totalGb:N0}GB 중 {freeGb:N0}GB 사용 가능");
                }
                else
                {
                    setPercent(0);
                    setText("드라이브 준비 안 됨");
                }
            }
            catch
            {
                setPercent(0);
                setText("드라이브 미인식");
            }
        }

        [RelayCommand]
        private void ToggleTheme()
        {
            ThemeService.Instance.ToggleTheme();
            IsDarkMode = ThemeService.Instance.CurrentTheme == AppTheme.Dark;
        }

        private void LoadSampleData()
        {
            RackList.Add(new RackViewModel("Line 1", 70, 13, 25.4));
            RackList.Add(new RackViewModel("Line 2", 54, 13, 25.5));

            // ⭐ Line 프로퍼티 적용
            EventLogList.Add(new EventLogModel
            {
                Line = 1,
                Bay = 1,
                Level = 1,
                Content = "시스템 수신 대기 중",
                DateTime = DateTime.Now
            });
        }

        private void HandlePacketProcessed(CrpPacket packet)
        {
            if (packet == null || packet.Detectors == null) return;

            if (!string.IsNullOrEmpty(packet.Id))
            {
                _boardLastSeen[packet.Id] = DateTime.Now;
                _boardTimeoutState.TryRemove(packet.Id, out _);
            }

            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                LatestPacket = packet;

                int maxLine = 16;
                if (int.TryParse(packet.MaxLine, out int ml) && ml > 0)
                {
                    maxLine = ml;
                }

                if (_mappingService != null && _mappingService.TryGetBoardMapping(packet.Id, maxLine, out int lane, out int bayOffset, out int targetBay, out int targetLevel))
                {
                    var targetRack = RackList.ElementAtOrDefault(lane - 1);
                    if (targetRack == null) return;

                    targetRack.ResizeIfNeeded(targetBay, targetLevel);
                    targetRack.Temperature = packet.ModuleTemp;

                    bool hasAnyFire = targetRack.UpdateDetectorCells(bayOffset, packet.Detectors, _fireSignalEvaluator);

                    foreach (var detector in packet.Detectors)
                    {
                        int globalBay = detector.Bay + bayOffset;
                        int globalLevel = detector.Level;

                        byte fireStatus = _fireSignalEvaluator?.Evaluate(detector.Status, detector.Temperature) ?? 0;

                        ProcessDetectorEventLog(packet.Id, detector, lane, globalBay, globalLevel, fireStatus, targetRack.RackName);
                    }

                    targetRack.SetFireAlarmState(hasAnyFire);
                }
            });
        }

        private void ProcessDetectorEventLog(string boardId, DetectorData detector, int lane, int bay, int level, byte fireStatus, string rackName)
        {
            string detectorKey = $"{boardId}_{detector.Index}";

            int currentStatusCategory = 0;

            if (detector.Status == 3)
            {
                currentStatusCategory = 3;
            }
            else if (fireStatus == 2)
            {
                currentStatusCategory = 2;
            }
            else if (fireStatus == 1)
            {
                currentStatusCategory = 1;
            }

            _detectorLastStatus.TryGetValue(detectorKey, out int previousStatusCategory);

            if (currentStatusCategory != previousStatusCategory)
            {
                Debug.WriteLine($"[STATUS CHANGE] 보드: {boardId}, 센서 #{detector.Index} | 이전: {previousStatusCategory} -> 변경: {currentStatusCategory}");
                _detectorLastStatus[detectorKey] = currentStatusCategory;

                switch (currentStatusCategory)
                {
                    case 2:
                        AddEventLog(lane, bay, level, $"[화재 감지] 고온 경보 발생! (센서 #{detector.Index}, 온도: {detector.Temperature}℃)");
                        // ⭐ '열' -> 'Line'으로 팝업 텍스트 정정
                        ShowFireNotification("🚨 화재 발생", $"[ {rackName} ]\nLine : {lane}   연 : {bay}   단 : {level}\n온도 : {detector.Temperature}℃");
                        break;

                    case 1:
                        AddEventLog(lane, bay, level, $"[연기 감지] 연기 신호 감지 (센서 #{detector.Index})");
                        // ⭐ '열' -> 'Line'으로 팝업 텍스트 정정
                        ShowFireNotification("⚠️ 연기 감지", $"[ {rackName} ]\nLine : {lane}   연 : {bay}   단 : {level}");
                        break;

                    case 3:
                        AddEventLog(lane, bay, level, $"[통신 오류] 감지기 연결 이상 (센서 #{detector.Index})");
                        break;

                    case 0:
                        if (previousStatusCategory != 0)
                        {
                            AddEventLog(lane, bay, level, $"[상태 복구] 감지기 정상 상태 복구 (센서 #{detector.Index})");
                        }
                        break;
                }
            }
        }

        private void ShowFireNotification(string title, string message)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                FireAlarmPopupViewModel? popupVm = null;

                popupVm = new FireAlarmPopupViewModel(title, message, () =>
                {
                    if (popupVm != null)
                    {
                        ActiveAlarms.Remove(popupVm);
                    }
                });

                ActiveAlarms.Add(popupVm);
            });
        }

        private void CheckUiBoardTimeouts(object? state)
        {
            if (_mappingService == null) return;

            DateTime now = DateTime.Now;

            foreach (var kvp in _boardLastSeen)
            {
                string boardId = kvp.Key;
                DateTime lastSeen = kvp.Value;

                if ((now - lastSeen).TotalSeconds > 5)
                {
                    if (_mappingService.TryGetBoardMapping(boardId, 16, out int lane, out int bayOffset, out int targetBay, out int targetLevel))
                    {
                        if (!_boardTimeoutState.ContainsKey(boardId))
                        {
                            _boardTimeoutState[boardId] = true;
                            // ⭐ 보드 전체 통신 두절이므로 단(Level)을 0으로 통일
                            AddEventLog(lane, bayOffset + 1, 0, $"[통신 오류] 보드 #{boardId} 신호 수신 중단 (5초 초과)");
                        }

                        Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            var targetRack = RackList.ElementAtOrDefault(lane - 1);
                            if (targetRack == null) return;

                            int startBay = bayOffset + 1;
                            int endBay = bayOffset + 16;

                            var targetCells = targetRack.CellList.Where(c => c.Bay >= startBay && c.Bay <= endBay);
                            foreach (var cell in targetCells)
                            {
                                cell.CellColor = "#9E9E9E";
                                cell.CellTooltip = $"[ 위치: {cell.Bay}연 {cell.Level}단 ]\n• 상태: 미연결 (신호 없음)";
                            }
                        });
                    }
                }
            }
        }

        [RelayCommand]
        private void OpenDataInquiry()
        {
            var viewModel = App.Services.GetService<DataInquiryViewModel>();

            var dataInquiryWindow = new DataInquiryWindow
            {
                DataContext = viewModel,
                Owner = Application.Current.MainWindow
            };

            dataInquiryWindow.Show();
        }

        // ⭐ 파라미터 이름을 row -> line으로 변경
        private void AddEventLog(int line, int bay, int level, string content)
        {
            DateTime now = DateTime.Now;
            Debug.WriteLine($"\n[LOG CALL] AddEventLog 호출됨 - 내용: {content}");

            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                EventLogList.Insert(0, new EventLogModel
                {
                    Line = line,
                    Bay = bay,
                    Level = level,
                    Content = content,
                    DateTime = now
                });

                if (EventLogList.Count > 200)
                {
                    EventLogList.RemoveAt(EventLogList.Count - 1);
                }
            });

            if (_repository == null)
            {
                Debug.WriteLine("❌ [DB CRITICAL] _repository가 null입니다! DI 주입 설정을 확인하세요.");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    Debug.WriteLine($"[DB TRY] DB 저장 시도 중... ({content})");
                    // ⭐ DB 저장소 메서드에 line 전달
                    await _repository.SaveEventLogAsync(line, bay, level, content, now).ConfigureAwait(false);
                    Debug.WriteLine($"✅ [DB SUCCESS] DB 저장 완료: {content}\n");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"💥 [DB ERROR] 저장 실패 예외 발생: {ex.Message}\n");
                }
            });
        }
    }
}