using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VBMS.Models;
using VBMS.Repositories; // Repository 네임스페이스
using VBMS.Services.Communications;
using VBMS.Services.Evaluators;
using VBMS.Services.Orchestrators;
using VBMS.Views;

namespace VBMS.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly IFdsDataOrchestrator? _orchestrator;
        private readonly IFdsMappingService? _mappingService;
        private readonly IFireSignalEvaluator? _fireSignalEvaluator;
        private readonly IDetectorRepository? _repository; // DB 저장용 Repository

        // UI 보드 수신 시각 및 위치 추적용
        private readonly ConcurrentDictionary<string, DateTime> _boardLastSeen = new();
        private readonly Timer _uiWatchdogTimer;

        // 중복 로그 도배 방지를 위한 센서별 이전 상태 저장소 (Key: BoardId_Index, Value: StatusCategory)
        private readonly ConcurrentDictionary<string, int> _detectorLastStatus = new();
        // 타임아웃 로그 중복 발생 방지 플래그
        private readonly ConcurrentDictionary<string, bool> _boardTimeoutState = new();

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
            LoadSampleData();

            // 2초마다 UI 수신 상태 점검 (5초 이상 패킷 미수신 시 회색으로 초기화 및 통신오류 로그 생성)
            _uiWatchdogTimer = new Timer(CheckUiBoardTimeouts, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }

        public MainWindowViewModel(
            IFdsDataOrchestrator orchestrator,
            IFdsMappingService mappingService,
            IFireSignalEvaluator fireSignalEvaluator,
            IDetectorRepository repository) : this() // Repository DI 주입
        {
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _mappingService = mappingService ?? throw new ArgumentNullException(nameof(mappingService));
            _fireSignalEvaluator = fireSignalEvaluator ?? throw new ArgumentNullException(nameof(fireSignalEvaluator));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));

            // 패킷 처리 완료 이벤트 구독
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

        private void LoadSampleData()
        {
            RackList.Add(new RackViewModel("상온 #1 (#1)", 70, 13, 25.4));
            RackList.Add(new RackViewModel("상온 #2 (#1)", 54, 13, 25.5));

            EventLogList.Add(new EventLogModel
            {
                Row = 1,
                Bay = 1,
                Level = 1,
                Content = "시스템 수신 대기 중",
                DateTime = DateTime.Now.ToString("HH:mm:ss")
            });
        }

        private void HandlePacketProcessed(CrpPacket packet)
        {
            if (packet == null || packet.Detectors == null) return;

            // 해당 보드의 최근 수신 시각 기록 및 타임아웃 해제
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

                    // 1. RackViewModel로 셀 UI 색상/상태 업데이트 위임
                    bool hasAnyFire = targetRack.UpdateDetectorCells(bayOffset, packet.Detectors, _fireSignalEvaluator);

                    // 2. 이벤트 로그 기록 및 알람 팝업 발생 처리
                    foreach (var detector in packet.Detectors)
                    {
                        int globalBay = detector.Bay + bayOffset;
                        int globalLevel = detector.Level;

                        byte fireStatus = _fireSignalEvaluator?.Evaluate(detector.Status, detector.Temperature) ?? 0;

                        ProcessDetectorEventLog(packet.Id, detector, lane, globalBay, globalLevel, fireStatus, targetRack.RackName);
                    }

                    // 3. 랙 카드 테두리 경보 색상 반영
                    targetRack.SetFireAlarmState(hasAnyFire);
                }
            });
        }

        /// <summary>
        /// FireSignalEvaluator의 평가 결과(fireStatus)를 받아 상태 변화 시에만 로그 및 알람 팝업을 발생시킵니다.
        /// </summary>
        private void ProcessDetectorEventLog(string boardId, DetectorData detector, int lane, int bay, int level, byte fireStatus, string rackName)
        {
            string detectorKey = $"{boardId}_{detector.Index}";

            // 카테고리 정의 -> 2: 온도화재, 1: 연기화재, 3: 통신오류, 0: 정상
            int currentStatusCategory = 0;

            if (detector.Status == 3)
            {
                currentStatusCategory = 3;
            }
            else if (fireStatus == 2)
            {
                currentStatusCategory = 2; // 온도/고온 화재
            }
            else if (fireStatus == 1)
            {
                currentStatusCategory = 1; // 연기 화재
            }

            _detectorLastStatus.TryGetValue(detectorKey, out int previousStatusCategory);

            // 상태가 변경되었을 때만 이벤트 로그 및 알람 발생 (도배 방지)
            if (currentStatusCategory != previousStatusCategory)
            {
                Debug.WriteLine($"[STATUS CHANGE] 보드: {boardId}, 센서 #{detector.Index} | 이전: {previousStatusCategory} -> 변경: {currentStatusCategory}");
                _detectorLastStatus[detectorKey] = currentStatusCategory;

                switch (currentStatusCategory)
                {
                    case 2: // 온도 화재
                        AddEventLog(lane, bay, level, $"[화재 감지] 고온 경보 발생! (센서 #{detector.Index}, 온도: {detector.Temperature}℃)");
                        ShowFireNotification("🚨 화재 발생", $"[ {rackName} ]\n열 : {lane}  연 : {bay}  단 : {level}\n온도 : {detector.Temperature}℃");
                        break;

                    case 1: // 연기 화재
                        AddEventLog(lane, bay, level, $"[연기 감지] 연기 신호 감지 (센서 #{detector.Index})");
                        ShowFireNotification("⚠️ 연기 감지", $"[ {rackName} ]\n열 : {lane}  연 : {bay}  단 : {level}");
                        break;

                    case 3: // 통신 오류
                        AddEventLog(lane, bay, level, $"[통신 오류] 감지기 연결 이상 (센서 #{detector.Index})");
                        break;

                    case 0: // 정상 복구
                        if (previousStatusCategory != 0)
                        {
                            AddEventLog(lane, bay, level, $"[상태 복구] 감지기 정상 상태 복구 (센서 #{detector.Index})");
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// 순수 WPF 기반 화재 알람 팝업 출력 메서드
        /// </summary>
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

        /// <summary>
        /// 5초 이상 수신이 끊긴 보드의 셀 구역을 회색(미연결)으로 초기화하고 통신 오류 이벤트를 발생시킵니다.
        /// </summary>
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
                            AddEventLog(lane, bayOffset + 1, 1, $"[통신 오류] 보드 #{boardId} 신호 수신 중단 (5초 초과)");
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
                                cell.CellColor = "#9E9E9E"; // 미연결 (회색)
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
            var dataInquiryWindow = new DataInquiryWindow
            {
                DataContext = new DataInquiryViewModel(),
                Owner = Application.Current.MainWindow
            };

            dataInquiryWindow.Show();
        }

        /// <summary>
        /// UI 목록에 로그를 추가하고 DB에 비동기로 저장합니다. (디버그 로그 포함)
        /// </summary>
        private void AddEventLog(int row, int bay, int level, string content)
        {
            DateTime now = DateTime.Now;
            Debug.WriteLine($"\n[LOG CALL] AddEventLog 호출됨 - 내용: {content}");

            // [1] UI 화면 갱신 (WPF UI 스레드)
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                EventLogList.Insert(0, new EventLogModel
                {
                    Row = row,
                    Bay = bay,
                    Level = level,
                    Content = content,
                    DateTime = now.ToString("HH:mm:ss")
                });

                if (EventLogList.Count > 200)
                {
                    EventLogList.RemoveAt(EventLogList.Count - 1);
                }
            });

            // [2] DB 비동기 저장 (백그라운드 스레드)
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
                    await _repository.SaveEventLogAsync(row, bay, level, content, now).ConfigureAwait(false);
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