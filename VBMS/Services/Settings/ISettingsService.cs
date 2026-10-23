using System;
using System.Threading.Tasks;
using VBMS.Models;

namespace VBMS.Services.Settings
{
    public interface ISettingsService
    {
        /// <summary>
        /// 현재 메모리에 로드된 최신 설정 객체
        /// </summary>
        SystemSettingsModel CurrentSettings { get; }

        /// <summary>
        /// 설정값이 저장/변경되었을 때 구독 컴포넌트들에게 알리는 이벤트
        /// </summary>
        event Action<SystemSettingsModel>? OnSettingsChanged;

        /// <summary>
        /// 파일로부터 설정을 비동기로 다시 읽어옵니다.
        /// </summary>
        Task<SystemSettingsModel> LoadSettingsAsync();

        /// <summary>
        /// 변경된 설정을 Config/appsettings.json에 비동기 저장합니다.
        /// </summary>
        Task SaveSettingsAsync(SystemSettingsModel settings);
    }
}