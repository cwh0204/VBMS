using System.Collections.Generic;

namespace VBMS.Models
{
    public class CrpPacket
    {
        public string Id { get; set; }
        public string MaxLine { get; set; }
        public string MaxStage { get; set; }
        public string Sequence { get; set; }
        public List<DetectorData> Detectors { get; set; } = new List<DetectorData>();
        public double ModuleTemp { get; set; }
        public int FanStatus { get; set; }
        public string RawData { get; set; }

        /// <summary>
        /// 파싱 중 정합성 이상(길이 불일치, 개수 불일치 등)이 감지되면 메시지가 담깁니다.
        /// 정상 파싱된 경우 null입니다.
        /// </summary>
        public string ParseWarning { get; set; }
    }
}