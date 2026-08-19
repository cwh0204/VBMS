namespace VBMS.Enums
{
    /// <summary>
    /// 센서 상태 및 이상징후/화재 단계 정의
    /// </summary>
    public enum AnomalyStatus : byte
    {
        Normal = 0,               // 정상
        SmokeAlarm = 1,           // 연기 화재 (비상)
        HighTempAlarm = 2,        // 고온 화재 (비상 - 60℃ 이상)
        DeltaTempWarning = 3,     // ⚠️ 평균 대비 온도 편차 과다 (사전 이상징후)
        AbsoluteTempWarning = 4   // ⚠️ 절대 주의 온도 도달 (사전 이상징후 - 예: 45℃)
    }
}