using System;

namespace VBMS.Models
{
    public class DetectorDumpLogModel
    {
        /// <summary>
        /// 덤프 수신 시간
        /// </summary>
        public DateTime ReceivedAt { get; set; }

        /// <summary>
        /// 랙 식별자 (예: BAY01_LV02)
        /// </summary>
        public string RackId { get; set; } = string.Empty;

        /// <summary>
        /// 보드 ID
        /// </summary>
        public int BoardId { get; set; }

        /// <summary>
        /// 온도
        /// </summary>
        public double Temperature { get; set; }

        /// <summary>
        /// 가스 농도
        /// </summary>
        public double GasLevel { get; set; }

        /// <summary>
        /// 화재 상태 여부
        /// </summary>
        public bool IsFire { get; set; }

        /// <summary>
        /// 센서 에러 여부
        /// </summary>
        public bool IsError { get; set; }
    }
}