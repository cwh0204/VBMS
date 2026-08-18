using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Notification.Wpf;
using Opc.Ua;
using Opc.Ua.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using VBMS.Models;
using VBMS.Services.Communications;
using VBMS.Services.Communications.OpcUa;
using VBMS.Services.Evaluators;
using VBMS.Services.Orchestrators;
using VBMS.Services.Parsers;
using VBMS.ViewModels;
using VBMS.Views;

namespace VBMS
{
    public partial class App : Application
    {
        private readonly IHost _host;
        private ApplicationInstance? _opcApplication;

        public App()
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.SetBasePath(AppDomain.CurrentDomain.BaseDirectory);
                    config.AddJsonFile("Config/appsettings.json", optional: false, reloadOnChange: true);
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<INotificationManager, NotificationManager>();
                    services.Configure<FdsOptions>(context.Configuration.GetSection("FdsConfig"));
                    services.AddSingleton<IFdsMappingService, FdsMappingService>();
                    services.AddSingleton<ICrpCommunicationService, CrpCommunicationService>();
                    services.AddSingleton<ICrpDataParser, CrpDataParser>();
                    services.AddSingleton<IFdsDataOrchestrator, FdsDataOrchestrator>();
                    services.AddSingleton<FdsOpcServer>();
                    services.AddSingleton<MainWindowViewModel>();
                    services.AddTransient<MainWindow>();
                    services.AddSingleton<IFireSignalEvaluator>(new FireSignalEvaluator(60.0));
                    services.AddSingleton<IFireVerificationService, FireVerificationService>();

                })
                .Build();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            await _host.StartAsync();

            try
            {
                var opcServer = _host.Services.GetRequiredService<FdsOpcServer>();
                string hostName = System.Net.Dns.GetHostName();

                var baseAddresses = new StringCollection
                {
                    "opc.tcp://0.0.0.0:4840/VBMS/FDS"
                };

                var config = new ApplicationConfiguration()
                {
                    ApplicationName = "VBMS_FDS_Server",
                    ApplicationUri = $"urn:{hostName}:VBMS:VBMS_FDS_Server",
                    ApplicationType = ApplicationType.Server,
                    SecurityConfiguration = new SecurityConfiguration
                    {
                        ApplicationCertificate = new CertificateIdentifier
                        {
                            StoreType = "Directory",
                            StorePath = "%CommonApplicationData%\\OPC Foundation\\CertificateStores\\MachineDefault",
                            SubjectName = $"CN=VBMS FDS Server, DC={hostName}"
                        },
                        TrustedPeerCertificates = new CertificateTrustList
                        {
                            StoreType = "Directory",
                            StorePath = "%CommonApplicationData%\\OPC Foundation\\CertificateStores\\UA Applications"
                        },
                        TrustedIssuerCertificates = new CertificateTrustList
                        {
                            StoreType = "Directory",
                            StorePath = "%CommonApplicationData%\\OPC Foundation\\CertificateStores\\UA Certificate Authorities"
                        },
                        RejectedCertificateStore = new CertificateTrustList
                        {
                            StoreType = "Directory",
                            StorePath = "%CommonApplicationData%\\OPC Foundation\\CertificateStores\\RejectedCertificates"
                        },
                        AutoAcceptUntrustedCertificates = true
                    },
                    TransportQuotas = new TransportQuotas
                    {
                        OperationTimeout = 60000,
                        MaxStringLength = 1048576,
                        MaxByteStringLength = 1048576,
                        MaxArrayLength = 65535,
                        MaxMessageSize = 4194304,
                        MaxBufferSize = 65535,
                        ChannelLifetime = 300000,
                        SecurityTokenLifetime = 3600000
                    },
                    ServerConfiguration = new ServerConfiguration
                    {
                        BaseAddresses = baseAddresses,
                        SecurityPolicies = new ServerSecurityPolicyCollection
                        {
                            new ServerSecurityPolicy
                            {
                                SecurityPolicyUri = SecurityPolicies.None,
                                SecurityMode = MessageSecurityMode.None
                            }
                        },
                        DiagnosticsEnabled = false,
                        MaxQueuedRequestCount = 2000
                    }
                };

                await config.ValidateAsync(ApplicationType.Server);

                // 2. 인증서 검색 및 생성
                X509Certificate2 cert = await config.SecurityConfiguration.ApplicationCertificate.FindAsync(true);
                if (cert == null)
                {
                    cert = CreateSelfSignedOpcCertificate(
                        config.SecurityConfiguration.ApplicationCertificate.SubjectName,
                        config.ApplicationUri,
                        hostName
                    );
                }
                config.SecurityConfiguration.ApplicationCertificate.Certificate = cert;

                // ★ 3. Task.Run 제거 후 직관적인 서버 실행
                _opcApplication = new ApplicationInstance((ITelemetryContext)null!)
                {
                    ApplicationName = "VBMS_FDS_Server",
                    ApplicationType = ApplicationType.Server,
                    ApplicationConfiguration = config
                };

                await _opcApplication.StartAsync(opcServer);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"OPC UA 서버 시작 실패: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.DataContext = _host.Services.GetRequiredService<MainWindowViewModel>();
            mainWindow.Show();

            base.OnStartup(e);
        }

        private static X509Certificate2 CreateSelfSignedOpcCertificate(string subjectName, string applicationUri, string dnsName)
        {
            using (RSA rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest(
                    new X500DistinguishedName(subjectName),
                    rsa,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1);

                request.CertificateExtensions.Add(
                    new X509KeyUsageExtension(
                        X509KeyUsageFlags.DigitalSignature |
                        X509KeyUsageFlags.KeyEncipherment |
                        X509KeyUsageFlags.DataEncipherment,
                        true));

                var sanBuilder = new SubjectAlternativeNameBuilder();
                sanBuilder.AddUri(new Uri(applicationUri));
                sanBuilder.AddDnsName(dnsName);
                sanBuilder.AddDnsName("localhost");
                request.CertificateExtensions.Add(sanBuilder.Build());

                var cert = request.CreateSelfSigned(
                    DateTimeOffset.UtcNow.AddDays(-1),
                    DateTimeOffset.UtcNow.AddYears(2));

                return new X509Certificate2(
                    cert.Export(X509ContentType.Pfx),
                    "",
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            try
            {
                if (_opcApplication != null)
                {
                    await _opcApplication.StopAsync();
                }
            }
            catch { }

            using (_host)
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
            }

            base.OnExit(e);
        }
    }
}