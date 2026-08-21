using MaterialDesignThemes.Wpf;
using SkiaSharp;
using System;

namespace VBMS.Services.Theme
{
    public class ThemeService
    {
        public static ThemeService Instance { get; } = new();

        private readonly PaletteHelper _paletteHelper = new();

        public event Action? ThemeChanged;

        private AppTheme _currentTheme = AppTheme.Light;
        public AppTheme CurrentTheme
        {
            get => _currentTheme;
            set
            {
                if (_currentTheme != value)
                {
                    _currentTheme = value;
                    ApplyMaterialTheme(value);
                    ThemeChanged?.Invoke();
                }
            }
        }

        private void ApplyMaterialTheme(AppTheme theme)
        {
            // ⭐ var를 사용하여 네임스페이스(VBMS.Services.Theme) 충돌 회피
            var currentMaterialTheme = _paletteHelper.GetTheme();
            currentMaterialTheme.SetBaseTheme(theme == AppTheme.Dark ? BaseTheme.Dark : BaseTheme.Light);
            _paletteHelper.SetTheme(currentMaterialTheme);
        }

        public void ToggleTheme()
        {
            CurrentTheme = CurrentTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
        }

        public SKColor TextColor => CurrentTheme == AppTheme.Dark ? SKColors.White : SKColors.Black;
        public SKColor SeparatorColor => CurrentTheme == AppTheme.Dark
            ? SKColors.Gray.WithAlpha(80)
            : SKColors.LightGray.WithAlpha(100);
    }
}