using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Configuration;
using System;
using System.Collections.Specialized;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace VBMS.Services.OpcUa
{
    public class FdsOpcServerHostedService : IHostedService
    {
        private readonly FdsOpcServer _opcServer;
        private readonly ILogger<FdsOpcServerHostedService> _logger;
        private ApplicationInstance? _opcApplication;

        public FdsOpcServerHostedService(FdsOpcServer opcServer, ILogger<FdsOpcServerHostedService> logger)
        {
            _opcServer = opcServer;
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                string hostName = System.Net.Dns.GetHostName();
                var baseAddresses = new Opc.Ua.StringCollection { "opc.tcp://0.0.0.0:4840/VBMS/FDS" };

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

                _opcApplication = new ApplicationInstance((ITelemetryContext)null!)
                {
                    ApplicationName = "VBMS_FDS_Server",
                    ApplicationType = ApplicationType.Server,
                    ApplicationConfiguration = config
                };

                await _opcApplication.StartAsync(_opcServer);
                _logger.LogInformation("OPC UA 서버가 성공적으로 시작되었습니다. (Port: 4840)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OPC UA 서버 구동 중 오류 발생");
                throw;
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_opcApplication != null)
            {
                _logger.LogInformation("OPC UA 서버 중지 중...");
                await _opcApplication.StopAsync();
                _logger.LogInformation("OPC UA 서버 중지 완료.");
            }
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
    }
}