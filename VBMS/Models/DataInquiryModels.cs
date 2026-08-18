using System;

namespace VBMS.Models
{
    // 화재보고 데이터 모델
    public class FireReportModel
    {
        public DateTime Time { get; set; }
        public string TimeString => Time.ToString("yyyy/MM/dd HH:mm:ss");
        public string RackName { get; set; } = string.Empty; // 열
        public int Bay { get; set; }                          // 연
        public int Level { get; set; }                        // 단
        public string Content { get; set; } = string.Empty;  // 내용
    }
}