using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Notification.Wpf;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using VBMS.Models;
using VBMS.Repositories;
using VBMS.Services.Communications;
using VBMS.Services.Evaluators;
using VBMS.Services.Export;
using VBMS.Services.Localization;
using VBMS.Services.OpcUa;
using VBMS.Services.Orchestrators;
using VBMS.Services.Parsers;
using VBMS.Services.Settings;
using VBMS.Services.Storage;
using VBMS.Services.Theme;
using VBMS.ViewModels;
using VBMS.Views;

namespace VBMS
{
    public partial class App : Application
    {
        private IHost? _host;
        private static Mutex? _mutex;

        public new static App Current => (App)Application.Current;
        public static IServiceProvider Services => Current._host!.Services;

        public App()
        {
            RegisterGlobalExceptionHandlers();

            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(AppDomain.CurrentDomain.BaseDirectory);
                    config.AddJsonFile("Config/appsettings.json", optional: false, reloadOnChange: true);
                })
                .ConfigureServices((context, services) =>
                {
                    // 환경 설정 및 다국어 서비스
                    services.AddSingleton<ILocalizationService, LocalizationService>();
                    services.AddSingleton<ISettingsService, SettingsService>();
                    services.AddSingleton<ThemeService>();

                    // Repositories (DuckDB)
                    services.AddSingleton<IDetectorRepository, DuckDbRepository>();

                    // TelemetryStorageService
                    services.AddSingleton<TelemetryStorageService>();
                    services.AddSingleton<ITelemetryStorageService>(sp => sp.GetRequiredService<TelemetryStorageService>());
                    services.AddHostedService(sp => sp.GetRequiredService<TelemetryStorageService>());

                    // Options & Notification
                    services.AddSingleton<INotificationManager, NotificationManager>();
                    services.Configure<FdsOptions>(context.Configuration.GetSection("FdsConfig"));

                    // Domain Services
                    services.AddSingleton<IFdsMappingService, FdsMappingService>();
                    services.AddSingleton<ICrpCommunicationService, CrpCommunicationService>();
                    services.AddSingleton<ICrpDataParser, CrpDataParser>();
                    services.AddSingleton<IFdsDataOrchestrator, FdsDataOrchestrator>();

                    services.AddSingleton<IFireSignalEvaluator>(sp =>
                    {
                        var settings = sp.GetRequiredService<ISettingsService>().CurrentSettings;
                        return new FireSignalEvaluator(
                            settings.HighTempThreshold,
                            settings.WarningTempThreshold,
                            settings.DeltaTempThreshold);
                    });

                    services.AddSingleton<IFireVerificationService, FireVerificationService>();

                    // OPC UA Server
                    services.AddSingleton<FdsOpcServer>();
                    services.AddHostedService<FdsOpcServerHostedService>();

                    // 엑셀 내보내기
                    services.AddSingleton<IExcelExportService, ExcelExportService>();

                    // ViewModels & Windows
                    services.AddSingleton<MainWindowViewModel>();
                    services.AddTransient<RackDetailViewModel>();
                    services.AddTransient<DataInquiryViewModel>();
                    services.AddTransient<SettingsViewModel>();

                    services.AddTransient<MainWindow>();
                    services.AddTransient<RackDetailWindow>();
                    services.AddTransient<DataInquiryWindow>();
                    services.AddTransient<SettingsWindow>();
                })
                .Build();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            const string mutexName = "Global\\VBMS_FireMonitoringSystem_SingleInstance_Mutex";
            _mutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show("VBMS 화재 감지 모니터링 프로그램이 이미 실행 중입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                Shutdown();
                return;
            }

            try
            {
                await _host!.StartAsync();

                // 언어 및 테마 복원
                var settingsService = _host.Services.GetRequiredService<ISettingsService>();
                var locService = _host.Services.GetRequiredService<ILocalizationService>();
                var themeService = _host.Services.GetRequiredService<ThemeService>();

                locService.SetLanguage(settingsService.CurrentSettings.Language);

                bool isDark = settingsService.CurrentSettings.Theme.Equals("Dark", StringComparison.OrdinalIgnoreCase);
                if ((isDark && themeService.CurrentTheme != AppTheme.Dark) ||
                    (!isDark && themeService.CurrentTheme != AppTheme.Light))
                {
                    themeService.ToggleTheme();
                }

                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                mainWindow.DataContext = _host.Services.GetRequiredService<MainWindowViewModel>();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                LogCrash("OnStartup 구동 중 예외 발생", ex);
                MessageBox.Show($"시스템 시작 중 오류가 발생하여 프로그램을 종료합니다.\n\n오류 내용: {ex.Message}", "치명적 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_host != null)
            {
                try
                {
                    Task.Run(async () =>
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await _host.StopAsync(cts.Token);
                    }).Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    LogCrash("App OnExit 자원 정리 중 오류 발생", ex);
                }
                finally
                {
                    _host.Dispose();
                    _host = null;
                }
            }

            if (_mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                    _mutex.Dispose();
                }
                catch { }
            }

            base.OnExit(e);
        }

        private void RegisterGlobalExceptionHandlers()
        {
            this.DispatcherUnhandledException += (s, e) =>
            {
                LogCrash("UI Thread Exception", e.Exception);
                e.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    LogCrash("AppDomain Unhandled Exception", ex);
                }
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                LogCrash("TaskScheduler Unobserved Exception", e.Exception);
                e.SetObserved();
            };
        }

        private static void LogCrash(string context, Exception ex)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                string content = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{context}]\n{ex}\n----------------------------------------\n";
                File.AppendAllText(logPath, content);
            }
            catch { }
        }
    }
}