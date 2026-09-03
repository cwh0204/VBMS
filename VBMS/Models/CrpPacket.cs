using System.Collections.Generic;

namespace VBMS.Models
{
    public class CrpPacket
    {
        public string Id { get; set; } = string.Empty;
        public string MaxLine { get; set; } = string.Empty;
        public string MaxStage { get; set; } = string.Empty;
        public string Sequence { get; set; } = string.Empty;
        public List<DetectorData> Detectors { get; set; } = new List<DetectorData>();
        public double ModuleTemp { get; set; }
        public int FanStatus { get; set; }
        public string RawData { get; set; } = string.Empty;

        /// <summary>
        /// 파싱 중 정합성 이상(길이 불일치, 개수 불일치 등)이 감지되면 메시지가 담깁니다.
        /// 정상 파싱된 경우 null입니다.
        /// </summary>
        public string? ParseWarning { get; set; }
    }
}