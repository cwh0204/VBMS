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

        // ⭐ 외부 라이브러리 대신 화면에 띄울 커스텀 알람 팝업 목록
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
            IFireSignalEvaluator fireSignalEvaluator) : this() // ⭐ INotificationManager 제거
        {
            _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
            _mappingService = mappingService ?? throw new ArgumentNullException(nameof(mappingService));
            _fireSignalEvaluator = fireSignalEvaluator ?? throw new ArgumentNullException(nameof(fireSignalEvaluator));

            // 패킷 처리 완료 이벤트 구독 (위치 기반 이벤트 로그 및 UI 업데이트 처리)
            _orchestrator.OnPacketProcessed += HandlePacketProcessed;

            _orchestrator.OnLogMessage += (message, level) =>
            {
                Debug.WriteLine($"[{level}] {message}");
            };

            _ = Task.Run(async () =>
            {
                try
                {
                    await _orchestrator.StartServerAsync(5000);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ERR] 서버 자동 시작 실패: {ex.Message}");
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

                    bool hasAnyFire = false; // 해당 랙 내 화재/경보 감지 여부 플래그

                    foreach (var detector in packet.Detectors)
                    {
                        int globalBay = detector.Bay + bayOffset;
                        int globalLevel = detector.Level;

                        var cell = targetRack.CellList.FirstOrDefault(c => c.Bay == globalBay && c.Level == globalLevel);
                        if (cell == null) continue;

                        // 1. FireSignalEvaluator를 이용한 상태 평가
                        // 2: 온도감지(고온/화재), 1: 연기감지, 0: 정상
                        byte fireStatus = _fireSignalEvaluator?.Evaluate(detector.Status, detector.Temperature) ?? 0;

                        // 랙 단위 화재 플래그 검사 (온도 또는 연기 화재 발생 시)
                        if (fireStatus == 1 || fireStatus == 2)
                        {
                            hasAnyFire = true;
                        }

                        // 2. 셀 색상 업데이트
                        if (detector.Status == 3)
                        {
                            cell.CellColor = "#FFC107"; // 통신/연결 오류 (노랑)
                        }
                        else if (fireStatus == 2)
                        {
                            cell.CellColor = "#F44336"; // 온도 화재 (빨강)
                        }
                        else if (fireStatus == 1)
                        {
                            cell.CellColor = "#FF9800"; // 연기 감지 (주황)
                        }
                        else if (detector.Status == 0 && fireStatus == 0)
                        {
                            cell.CellColor = "#4CAF50"; // 정상 (초록)
                        }
                        else
                        {
                            cell.CellColor = "#9E9E9E"; // 미연결 (회색)
                        }

                        // 3. 툴팁 텍스트 세분화
                        string statusDisplayText = detector.Status switch
                        {
                            3 => "통신오류",
                            0 => fireStatus switch
                            {
                                2 => "화재(온도)",
                                1 => "화재(연기)",
                                _ => "정상"
                            },
                            _ => fireStatus switch
                            {
                                2 => "화재(온도)",
                                1 => "화재(연기)",
                                _ => "미연결"
                            }
                        };

                        cell.CellTooltip = $"[ 위치: {cell.Bay}연 {cell.Level}단 ]\n• 센서 ID: #{detector.Index}\n• 상태: {statusDisplayText}\n• 온도: {detector.Temperature}℃";

                        // 4. 평가 결과를 전달하여 이벤트 로그 및 직접 작성한 팝업 알람 발생 처리
                        ProcessDetectorEventLog(packet.Id, detector, lane, globalBay, globalLevel, fireStatus, targetRack.RackName);
                    }

                    // 5. 랙 카드 테두리 색상/두께 업데이트 (화재 유무 반영)
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
                _detectorLastStatus[detectorKey] = currentStatusCategory;

                switch (currentStatusCategory)
                {
                    case 2: // 온도 화재
                        AddEventLog(lane, bay, level, $"[화재 감지] 고온 경보 발생! (센서 #{detector.Index}, 온도: {detector.Temperature}℃)");

                        // ⭐ 직접 제작한 팝업 알람 출력
                        ShowFireNotification("🚨 화재 발생", $"[ {rackName} ]\n열 : {bay}  연 : {bay}  단 : {level}\n온도 : {detector.Temperature}℃");
                        break;

                    case 1: // 연기 화재
                        AddEventLog(lane, bay, level, $"[연기 감지] 연기 신호 감지 (센서 #{detector.Index})");

                        // ⭐ 직접 제작한 팝업 알람 출력
                        ShowFireNotification("⚠️ 연기 감지", $"[ {rackName} ]\n열 : {bay}  연 : {bay}  단 : {level}");
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
        /// (사용자가 [확인] 또는 [X] 버튼을 직접 누를 때까지 화면에 계속 남아있습니다)
        /// </summary>
        private void ShowFireNotification(string title, string message)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                FireAlarmPopupViewModel? popupVm = null;

                // [확인] 또는 [X] 버튼을 누를 때만 목록에서 제거
                popupVm = new FireAlarmPopupViewModel(title, message, () =>
                {
                    if (popupVm != null)
                    {
                        ActiveAlarms.Remove(popupVm);
                    }
                });

                // 화면 오버레이 목록에 추가 (자동 삭제 타이머 제거)
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

                // 5초 이상 신호가 없으면 수신 중단으로 판단
                if ((now - lastSeen).TotalSeconds > 5)
                {
                    if (_mappingService.TryGetBoardMapping(boardId, 16, out int lane, out int bayOffset, out int targetBay, out int targetLevel))
                    {
                        // 최초 타임아웃 발생 시 1회 통신오류 이벤트 로그 추가
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

                            // 해당 보드가 위치했던 16개 Bay 영역을 회색(미연결)으로 복구
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

        /// <summary>
        /// 통계 및 이력 조회 팝업 창 열기
        /// </summary>
        [RelayCommand]
        private void OpenDataInquiry()
        {
            // 뷰와 뷰모델 생성 및 연결
            var dataInquiryWindow = new DataInquiryWindow
            {
                DataContext = new DataInquiryViewModel(),
                // 메인 창의 자식 팝업으로 지정 (메인 창 중앙 위치 및 최소화/닫기 종속)
                Owner = Application.Current.MainWindow
            };

            // 팝업 띄우기 (독립적으로 다른 창 작업도 허용하려면 Show(), 모달로 막으려면 ShowDialog())
            dataInquiryWindow.Show();
        }

        /// <summary>
        /// ROW, BAY, LEVEL 정보를 포함하여 이벤트 로그를 추가하는 헬퍼 메서드
        /// </summary>
        private void AddEventLog(int row, int bay, int level, string content)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                EventLogList.Insert(0, new EventLogModel
                {
                    Row = row,
                    Bay = bay,
                    Level = level,
                    Content = content,
                    DateTime = DateTime.Now.ToString("HH:mm:ss")
                });

                // 로그 목록이 너무 길어지지 않도록 상위 200개만 유지
                if (EventLogList.Count > 200)
                {
                    EventLogList.RemoveAt(EventLogList.Count - 1);
                }
            });
        }
    }
}