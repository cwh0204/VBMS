using System;

namespace VBMS.Models
{
    public class EventLogModel
    {
        // Line 프로퍼티 추가 및 기존 Row 호출 호환 유지
        public int Line { get; set; }
        public int Row
        {
            get => Line;
            set => Line = value;
        }

        public int Bay { get; set; }
        public int Level { get; set; }
        public string Content { get; set; } = string.Empty;

        // DB(DuckDB TIMESTAMP) 및 엑셀과 정합성을 맞추기 위해 DateTime 객체로 선언
        public DateTime DateTime { get; set; } = DateTime.Now;
    }
}