namespace VBMS.Models
{
    public class SystemSettingsModel
    {
        // --------------------------------------------------
        // 1. 일반 설정 (General)
        // --------------------------------------------------
        /// <summary>
        /// 시스템 언어 ("ko-KR" 또는 "en-US")
        /// </summary>
        public string Language { get; set; } = "ko-KR";

        /// <summary>
        /// 기본 화면 테마 ("Dark" 또는 "Light")
        /// </summary>
        public string Theme { get; set; } = "Dark";

        // --------------------------------------------------
        // 2. 안전 및 화재 판정 임계치 (Safety / Thresholds)
        // --------------------------------------------------
        /// <summary>
        /// 고온 화재 경보 온도 (기본: 60.0℃)
        /// </summary>
        public double HighTempThreshold { get; set; } = 60.0;

        /// <summary>
        /// 주의 경보 온도 (기본: 45.0℃)
        /// </summary>
        public double WarningTempThreshold { get; set; } = 45.0;

        /// <summary>
        /// 랙 평균 대비 국소 편차 경보 임계치 (기본: 10.0℃)
        /// </summary>
        public double DeltaTempThreshold { get; set; } = 10.0;

        // --------------------------------------------------
        // 3. 통신 및 네트워크 (Network)
        // --------------------------------------------------
        /// <summary>
        /// CRP 패킷 수신 TCP 서버 포트 (기본: 5000)
        /// </summary>
        public int ServerPort { get; set; } = 5000;

        /// <summary>
        /// FDS 보드 신호 수신 두절 판정 타임아웃 (초 단위, 기본: 5)
        /// </summary>
        public int BoardTimeoutSeconds { get; set; } = 5;

        // --------------------------------------------------
        // 4. 저장소 및 데이터 관리 (Storage)
        // --------------------------------------------------
        /// <summary>
        /// 엑셀 및 데이터 덤프 기본 저장 폴더 경로
        /// </summary>
        public string ExportDirectory { get; set; } = @"D:\gtcsfile";

        /// <summary>
        /// UI 실시간 이벤트 로그 최대 보관 행 수 (기본: 200)
        /// </summary>
        public int MaxEventLogCount { get; set; } = 200;
    }
}