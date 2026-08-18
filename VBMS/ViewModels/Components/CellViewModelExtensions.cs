using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace VBMS.ViewModels
{
    public static class CellColors
    {
        public const string Normal = "#4CAF50";    // 정상
        public const string Offline = "#9E9E9E";   // 통신오류
        public const string Warning = "#FF9800";   // 연기감지
        public const string Alarm = "#F44336";     // 화재발생
        public const string Disabled = "#607D8B";  // 사용중지

        /// <summary>
        /// 대소문자 구분 없이 Hex 색상 코드가 일치하는지 비교
        /// </summary>
        public static bool IsSameColor(string? color1, string? color2)
        {
            return string.Equals(color1, color2, StringComparison.OrdinalIgnoreCase);
        }

        public static string DisplayName(string hex)
        {
            if (IsSameColor(hex, Alarm)) return "화재발생";
            if (IsSameColor(hex, Warning)) return "연기감지";
            if (IsSameColor(hex, Disabled)) return "사용중지";
            if (IsSameColor(hex, Offline)) return "통신오류";
            if (IsSameColor(hex, Normal)) return "정상";
            return "알수없음";
        }
    }

    public partial class CellViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _isDimmed;

        [ObservableProperty]
        private double _temperature = 25.0;

        public bool IsException => CellColors.IsSameColor(CellColor, CellColors.Warning) || CellColors.IsSameColor(CellColor, CellColors.Alarm);

        public string ExceptionSummary => CellTooltip;

        public void ResetSensor()
        {
            if (IsException)
            {
                CellColor = CellColors.Normal;
            }
        }
    }
}