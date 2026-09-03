using System;
using VBMS.Models;

namespace VBMS.Services.Parsers
{
    public class CrpDataParser : ICrpDataParser
    {
        // 실측 데이터 기준: 센서 1개 = 온도(3자리) + 상태(1자리) = 4자리
        // ※ 공식 프로토콜 문서(ttt+hh+d=6자리)와 다름 - 실제 장비가 가스(hh) 필드를 보내지 않는 것으로 확인됨.
        //   문서가 최신 펌웨어를 반영 못 하고 있을 수 있으니 벤더 확인 필요. 확인 전까지는 실측값을 기준으로 파싱함.
        private const int SensorBlockWidth = 4;
        private const int FooterWidth = 6; // fnt(3) + s(1) + SS(2, 고정값 "98")

        // 반환 타입을 CrpPacket? 로 변경하여 null 반환 경고(CS8603) 해제
        public CrpPacket? Parse(string? rawData)
        {
            if (string.IsNullOrWhiteSpace(rawData) || !rawData.StartsWith("(") || !rawData.EndsWith(")"))
            {
                return null;
            }

            try
            {
                string content = rawData.Substring(1, rawData.Length - 2);
                int commaIndex = content.IndexOf(',');
                if (commaIndex == -1)
                {
                    return null;
                }

                // 1. 헤더 영역 파싱 (xxxyyddc)
                string header = content.Substring(0, commaIndex);
                if (header.Length < 8)
                {
                    return null;
                }

                var packet = new CrpPacket
                {
                    Id = header.Substring(0, 3),
                    MaxLine = header.Substring(3, 2),
                    MaxStage = header.Substring(5, 2),
                    Sequence = header.Substring(7, 1),
                    RawData = rawData
                };

                // 헤더로부터 연(MaxLine)과 단(MaxStage) 숫자를 정수로 추출 (실패 시 기본값 16)
                int maxLine = 16;
                if (int.TryParse(packet.MaxLine, out int ml)) maxLine = ml;
                int maxStage = 16;
                if (int.TryParse(packet.MaxStage, out int ms)) maxStage = ms;

                // 2. 바디 및 푸터 영역 분리
                string bodyAndFooter = content.Substring(commaIndex + 1);
                if (bodyAndFooter.Length < FooterWidth)
                {
                    return null;
                }

                string body = bodyAndFooter.Substring(0, bodyAndFooter.Length - FooterWidth);
                string footer = bodyAndFooter.Substring(bodyAndFooter.Length - FooterWidth);

                // 정합성 체크: body 길이가 SensorBlockWidth의 배수가 아니면 정렬이 깨진 것 -> 경고 남기고 계속 진행
                if (body.Length % SensorBlockWidth != 0)
                {
                    packet.ParseWarning =
                        $"body 길이({body.Length})가 센서 블록 폭({SensorBlockWidth})의 배수가 아닙니다. " +
                        $"나머지 {body.Length % SensorBlockWidth}자는 파싱에서 제외됩니다. 회선 노이즈나 패킷 분할 오류일 수 있습니다.";
                }

                // 3. 센서 영역 파싱 (SensorBlockWidth 단위 반복: ttt d)
                int detectorIndex = 1;
                int sensorSeq = 0;
                for (int i = 0; i + SensorBlockWidth <= body.Length; i += SensorBlockWidth)
                {
                    string chunk = body.Substring(i, SensorBlockWidth);
                    if (int.TryParse(chunk.Substring(0, 3), out int rawTemp) &&
                        int.TryParse(chunk.Substring(3, 1), out int status))
                    {
                        int bay = (sensorSeq / maxStage) + 1;
                        int level = sensorSeq % maxStage;

                        packet.Detectors.Add(new DetectorData
                        {
                            Index = detectorIndex++,
                            Bay = bay,
                            Level = level,
                            Temperature = rawTemp / 10.0,
                            GasDensity = 0, // 실측 데이터에 가스 필드가 없어 알 수 없음 (문서와 불일치, 확인 필요)
                            Status = status
                        });
                        sensorSeq++;
                    }
                    else
                    {
                        packet.ParseWarning = (string.IsNullOrEmpty(packet.ParseWarning) ? "" : packet.ParseWarning + " | ")
                            + $"인덱스 {i}의 센서 블록 '{chunk}' 파싱 실패, 건너뜀 (이후 좌표가 밀릴 수 있음).";
                    }
                }

                // 기대 개수(연 x 단)와 실제 파싱된 개수가 다르면 경고
                int expectedCount = maxLine * maxStage;
                if (packet.Detectors.Count != expectedCount)
                {
                    packet.ParseWarning = (string.IsNullOrEmpty(packet.ParseWarning) ? "" : packet.ParseWarning + " | ")
                        + $"기대 감지기 수({maxLine}x{maxStage}={expectedCount})와 실제 파싱된 수({packet.Detectors.Count})가 다릅니다.";
                }

                // 4. 푸터 영역 파싱
                if (int.TryParse(footer.Substring(0, 3), out int rawModuleTemp))
                {
                    packet.ModuleTemp = rawModuleTemp / 10.0;
                }
                if (int.TryParse(footer.Substring(3, 1), out int fanStatus))
                {
                    packet.FanStatus = fanStatus;
                }

                return packet;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}