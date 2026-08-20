using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VBMS.Models;
using VBMS.Services.Parsers;

namespace VBMS.Services.Communications
{
    public class CrpCommunicationService : ICrpCommunicationService
    {
        // 1. IP:Port (Endpoint) 기준 클라이언트 세션 관리
        private readonly ConcurrentDictionary<string, TcpClient> _connectedClients = new();

        // 2. 장비 ID (예: "001", "002") 기준 클라이언트 매핑
        private readonly ConcurrentDictionary<string, TcpClient> _clientsByBoardId = new();

        private TcpListener? _listener;
        private CancellationTokenSource? _cts;

        // 인터페이스 명세에 맞춘 이벤트 이름
        public event Action<CrpPacket>? OnPacketReceived;
        public event Action<bool>? OnConnectionChanged;
        public event Action<string, string>? OnLogMessage;

        private readonly ICrpDataParser _crpDataParser;

        // 접속된 장비가 1대 이상이면 연결된 것으로 간주
        public bool IsConnected => !_connectedClients.IsEmpty;

        // 현재 접속된 장비 개수
        public int ConnectedCount => _connectedClients.Count;

        public CrpCommunicationService(ICrpDataParser crpDataParser)
        {
            _crpDataParser = crpDataParser;
        }

        // [인터페이스 구현] StartServerAsync
        public Task StartServerAsync(int port = 5000)
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();

            Log($"{port}번 포트에서 다중 CRP 장비 접속 대기 중...", "SYS");

            _ = AcceptClientsAsync(_cts.Token);
            return Task.CompletedTask;
        }

        // [인터페이스 구현] Disconnect
        public void Disconnect()
        {
            _cts?.Cancel();
            _listener?.Stop();

            foreach (var kvp in _connectedClients)
            {
                kvp.Value.Close();
                kvp.Value.Dispose();
            }

            _connectedClients.Clear();
            _clientsByBoardId.Clear();

            OnConnectionChanged?.Invoke(false);
            Log("CRP 서버 통신 서비스가 정지되었습니다.", "SYS");
        }

        private async Task AcceptClientsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await _listener!.AcceptTcpClientAsync(token);
                    string clientEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";

                    _connectedClients[clientEndpoint] = client;
                    OnConnectionChanged?.Invoke(IsConnected);

                    Log($"CRP 장비가 성공적으로 연결되었습니다. ({clientEndpoint}) [총 접속: {_connectedClients.Count}대]", "SYS");

                    _ = Task.Run(() => HandleClientAsync(client, clientEndpoint, token), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"클라이언트 접속 수락 중 오류: {ex.Message}", "ERR");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, string clientEndpoint, CancellationToken token)
        {
            string? registeredBoardId = null;

            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                byte[] buffer = new byte[8192];
                StringBuilder rxBuffer = new StringBuilder();

                try
                {
                    while (!token.IsCancellationRequested && client.Connected)
                    {
                        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token);

                        if (bytesRead == 0)
                        {
                            Log($"[{clientEndpoint}] CRP 장비 연결이 종료되었습니다. (0 Bytes Read)", "SYS");
                            break;
                        }

                        string receivedChunk = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                        rxBuffer.Append(receivedChunk);

                        // 안전한 링버퍼 형태의 패킷 추출 루프
                        while (true)
                        {
                            string currentData = rxBuffer.ToString();
                            int startIndex = currentData.IndexOf('(');

                            // 버퍼 내에 시작 문자가 없으면 대기 (잘못된 찌꺼기 데이터는 버퍼 폭증 방지)
                            if (startIndex == -1)
                            {
                                if (rxBuffer.Length > 8192) rxBuffer.Clear();
                                break;
                            }

                            // '(' 앞의 불필요한 찌꺼기 데이터 즉시 제거
                            if (startIndex > 0)
                            {
                                rxBuffer.Remove(0, startIndex);
                                currentData = rxBuffer.ToString();
                                startIndex = 0;
                            }

                            // '(' 이후의 종료 문자 ')' 위치 검색
                            int endIndex = currentData.IndexOf(')', startIndex);

                            // 아직 짝이 맞는 ')'가 들어오지 않았으면 다음 ReadAsync 대기
                            if (endIndex == -1) break;

                            // 완전한 패킷 추출
                            string fullPacket = currentData.Substring(startIndex, endIndex - startIndex + 1);

                            // 처리할 패킷 구간을 버퍼에서 즉시 제거
                            rxBuffer.Remove(0, endIndex + 1);

                            // 패킷 해석 처리
                            CrpPacket? packet = _crpDataParser.Parse(fullPacket);
                            Log($"[{clientEndpoint}] {fullPacket}", "RX");

                            if (packet != null)
                            {
                                registeredBoardId = packet.Id;
                                _clientsByBoardId[registeredBoardId] = client;

                                OnPacketReceived?.Invoke(packet);

                                var alarmList = packet.Detectors.Where(d => d.IsAlarm).ToList();
                                string alarmSummary = alarmList.Any()
                                    ? $"경보발생[{string.Join(", ", alarmList.Select(a => $"#{a.Index}:{a.StatusText}"))}]"
                                    : "정상";

                                string parsedText = $"[{clientEndpoint} | ID:{packet.Id}] 순번:{packet.Sequence} | 감지기수:{packet.Detectors.Count}개 | 모듈온도:{packet.ModuleTemp}℃ | 상태:{alarmSummary}";
                                Log(parsedText, alarmList.Any() ? "ERR" : "SYS");
                            }
                            else if (fullPacket.Contains("RSR") || fullPacket.Contains("VER") || fullPacket.Contains("RST") || fullPacket.Contains("FAN"))
                            {
                                Log($"[{clientEndpoint}] 명령어 처리 응답 성공: {fullPacket}", "SYS");
                            }
                            else
                            {
                                Log($"[{clientEndpoint}] 규격에 맞지 않는 패킷 구조입니다.", "ERR");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[{clientEndpoint}] 통신 중 오류 발생: {ex.Message}", "ERR");
                }
                finally
                {
                    _connectedClients.TryRemove(clientEndpoint, out _);

                    if (!string.IsNullOrEmpty(registeredBoardId))
                    {
                        _clientsByBoardId.TryRemove(registeredBoardId, out _);
                    }

                    OnConnectionChanged?.Invoke(IsConnected);
                    Log($"[{clientEndpoint}] 세션 정리가 완료되었습니다. [남은 접속: {_connectedClients.Count}대]", "SYS");
                }
            }
        }

        public async Task SendCommandAsync(string command)
        {
            if (_connectedClients.IsEmpty)
            {
                Log("연결된 CRP 장비가 없습니다. 명령 전송 취소.", "ERR");
                return;
            }

            string? targetBoardId = ExtractBoardIdFromCommand(command);

            if (!string.IsNullOrEmpty(targetBoardId) && _clientsByBoardId.TryGetValue(targetBoardId, out var targetClient))
            {
                bool success = await SendToClientAsync(targetClient, command, $"ID:{targetBoardId}");
                if (success) return;
            }

            Log($"[{targetBoardId ?? "전체"}] 타겟 세션을 찾을 수 없어 전체 연결된 장비({_connectedClients.Count}대)에 명령을 전송합니다: {command}", "SYS");

            byte[] body = Encoding.ASCII.GetBytes(command);

            foreach (var kvp in _connectedClients)
            {
                try
                {
                    NetworkStream stream = kvp.Value.GetStream();
                    await stream.WriteAsync(body, 0, body.Length);
                    Log($"[{kvp.Key}] 명령 전송 완료: {command}", "TX");
                }
                catch (Exception ex)
                {
                    Log($"[{kvp.Key}] 명령 전송 실패: {ex.Message}", "ERR");
                }
            }
        }

        private async Task<bool> SendToClientAsync(TcpClient client, string command, string targetLabel)
        {
            try
            {
                if (!client.Connected) return false;

                byte[] body = Encoding.ASCII.GetBytes(command);
                NetworkStream stream = client.GetStream();
                await stream.WriteAsync(body, 0, body.Length);
                Log($"[{targetLabel}] 타겟 개별 명령 전송 성공: {command}", "TX");
                return true;
            }
            catch (Exception ex)
            {
                Log($"[{targetLabel}] 타겟 개별 명령 전송 실패: {ex.Message}", "ERR");
                return false;
            }
        }

        private string? ExtractBoardIdFromCommand(string command)
        {
            if (string.IsNullOrEmpty(command)) return null;

            string trimmed = command.Trim('[', ']', ' ', '\r', '\n');
            if (trimmed.Length >= 3 && char.IsDigit(trimmed[0]) && char.IsDigit(trimmed[1]) && char.IsDigit(trimmed[2]))
            {
                return trimmed.Substring(0, 3);
            }

            return null;
        }

        private void Log(string message, string type)
        {
            string formatted = $"[{DateTime.Now:HH:mm:ss.fff}] [{type}] {message}";

            // [인터페이스 이벤트 이름 적용] OnLogMessage
            OnLogMessage?.Invoke(message, type);
        }
    }
}