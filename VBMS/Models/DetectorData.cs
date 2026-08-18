namespace VBMS.Models
{
    public class DetectorData
    {
        public int Index { get; set; }          // 감지기 번호

        // 새로 추가된 좌표 속성
        public int Bay { get; set; }            // 연 (열 위치, 1 ~ 16)
        public int Level { get; set; }          // 단 (행 위치, 0 ~ 15)
        public double Temperature { get; set; } // 온도 (ttt / 10.0)
        public int GasDensity { get; set; }     // 가스농도 (hh)
        public int Status { get; set; }         // 상태 (d)

        public string StatusText => Status switch
        {
            0 => "정상",
            1 => "화재(연기)",
            2 => "가스감지",
            3 => "통신오류",
            4 => "연결안됨",
            5 => "리셋중",
            6 => "연기센서오류",
            7 => "가스센서오류",
            8 => "저/고전압오류",
            _ => $"알수없음({Status})"
        };

        public bool IsAlarm => Status == 1 || Status == 2;
    }
}