using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using VBMS.Models;

namespace VBMS.Services.Settings
{
    public class SettingsService : ISettingsService
    {
        private readonly string _userConfigPath;
        private readonly string _defaultConfigPath;
        private SystemSettingsModel _currentSettings = new();

        public SystemSettingsModel CurrentSettings => _currentSettings;

        public event Action<SystemSettingsModel>? OnSettingsChanged;

        public SettingsService()
        {
            string configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
            _userConfigPath = Path.Combine(configDir, "user_settings.json");
            _defaultConfigPath = Path.Combine(configDir, "appsettings.json");

            LoadInitialSettings();
        }

        private void LoadInitialSettings()
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                // 1순위: 사용자가 저장한 독립 설정 파일(user_settings.json) 로드
                if (File.Exists(_userConfigPath))
                {
                    string userJson = File.ReadAllText(_userConfigPath);
                    _currentSettings = JsonSerializer.Deserialize<SystemSettingsModel>(userJson, options)
                                       ?? new SystemSettingsModel();
                    return;
                }

                // 2순위: 사용자 설정이 아직 없으면 기본 appsettings.json에서 로드
                if (File.Exists(_defaultConfigPath))
                {
                    string defaultJson = File.ReadAllText(_defaultConfigPath);
                    var rootNode = JsonNode.Parse(defaultJson);
                    var settingsNode = rootNode?["SystemSettings"];

                    if (settingsNode != null)
                    {
                        _currentSettings = JsonSerializer.Deserialize<SystemSettingsModel>(settingsNode.ToJsonString(), options)
                                           ?? new SystemSettingsModel();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SettingsService] 설정 로드 오류: {ex.Message}");
            }

            // 기본값 유지
            _currentSettings = new SystemSettingsModel();
        }

        public async Task<SystemSettingsModel> LoadSettingsAsync()
        {
            LoadInitialSettings();
            return await Task.FromResult(_currentSettings);
        }

        public async Task SaveSettingsAsync(SystemSettingsModel settings)
        {
            _currentSettings = settings;

            string? dir = Path.GetDirectoryName(_userConfigPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            // 빌드 시 덮어쓰기 되지 않도록 user_settings.json에 저장
            string json = JsonSerializer.Serialize(settings, options);
            await File.WriteAllTextAsync(_userConfigPath, json);

            OnSettingsChanged?.Invoke(_currentSettings);
        }
    }
}