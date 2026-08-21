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
using VBMS.Services.OpcUa;
using VBMS.Services.Orchestrators;
using VBMS.Services.Parsers;
using VBMS.Services.Storage;
using VBMS.Services.Theme;
using VBMS.ViewModels;
using VBMS.Views;

namespace VBMS
{
    public partial class App : Application
    {
        private IHost? _host;
        private static Mutex? _mutex; // 중복 실행 방지용 뮤텍스

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
                    // Repositories (DuckDB)
                    services.AddSingleton<IDetectorRepository, DuckDbRepository>();

                    // 1. TelemetryStorageService 싱글톤 + HostedService 동시 등록
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

                    services.AddSingleton<IFireSignalEvaluator>(sp => new FireSignalEvaluator(60.0));
                    services.AddSingleton<IFireVerificationService, FireVerificationService>();

                    // 2. OPC UA Server 및 HostedService 등록
                    services.AddSingleton<FdsOpcServer>();
                    services.AddHostedService<FdsOpcServerHostedService>();

                    // Theme 설정 변경
                    services.AddSingleton<ThemeService>();

                    // UI ViewModels & Windows
                    services.AddSingleton<MainWindowViewModel>();
                    services.AddTransient<RackDetailViewModel>();
                    services.AddTransient<DataInquiryViewModel>();


                    services.AddTransient<MainWindow>();
                    services.AddTransient<RackDetailWindow>();
                    services.AddTransient<DataInquiryWindow>();

                })
                .Build();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 🌟 1. 중복 실행 체크 (DuckDB 파일 락 및 OPC UA 포트 충돌 방지)
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
                // 🌟 2. 백그라운드 서비스(DuckDB, OPC UA 등) 먼저 시동
                await _host!.StartAsync();

                // 🌟 3. 서비스가 정상 시작되면 메인 화면 표시
                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                mainWindow.DataContext = _host.Services.GetRequiredService<MainWindowViewModel>();
                mainWindow.Show();
            }
            catch (Exception ex)
            {
                LogCrash("OnStartup 구동 중 예외 발생", ex);
                MessageBox.Show($"시스템 시작 중 오류가 발생하여 프로그램을 종료합니다.\n\n오류 내용: {ex.Message}", "치명적 오류", MessageBoxButton.OK, MessageBoxImage.Error);

                // 구동 실패 시 프로그램 즉시 종료
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 🌟 4. 교착 상태(Deadlock) 없는 안전한 백그라운드 서비스 정지
            if (_host != null)
            {
                try
                {
                    // UI 스레드를 멈추지 않도록 별도 Task에서 StopAsync 실행 (최대 5초 대기)
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

            // 뮤텍스 해제
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
                // ex.ToString()을 사용하여 스택 트레이스 및 하위 예외(InnerException)까지 상세히 기록
                string content = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{context}]\n{ex}\n----------------------------------------\n";
                File.AppendAllText(logPath, content);
            }
            catch { }
        }
    }
}