using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace VBMS.ViewModels
{
    public partial class FireAlarmPopupViewModel : ObservableObject
    {
        private readonly Action? _onClose;

        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private string _message = string.Empty;

        public FireAlarmPopupViewModel() { }

        // ⭐ 닫기 이벤트 콜백(onClose)을 인자로 받는 생성자 추가
        public FireAlarmPopupViewModel(string title, string message, Action? onClose = null)
        {
            Title = title;
            Message = message;
            _onClose = onClose;
        }

        // [RelayCommand]에 의해 CloseCommand가 자동 생성됩니다.
        [RelayCommand]
        private void Close()
        {
            _onClose?.Invoke(); // [확인] 또는 [X] 버튼 클릭 시 전달받은 콜백 실행
        }
    }
}