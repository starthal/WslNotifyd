﻿using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using GrpcNotification;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using WslNotifyd.DBus;
using WslNotifyd.GrpcServices;
using WslNotifyd.Services;

internal class Program
{

    private static (X509Certificate2, X509Certificate2) CreateCerts()
    {
        // if (File.Exists("./server.pfx") && File.Exists("./client.pfx"))
        // {
        //     Console.WriteLine("reusing certs");
        //     return (new X509Certificate2("./server.pfx"), new X509Certificate2("./client.pfx"));
        // }
        var now = DateTimeOffset.UtcNow;

        using var rootRsa = RSA.Create(4096);
        using var rsa = RSA.Create(4096);

        var rootReq = new CertificateRequest("CN=WslNotifyd", rootRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        // rootReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, true));
        rootReq.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootReq.PublicKey, false));
        var serverCert = rootReq.CreateSelfSigned(now.AddDays(-2), now.AddDays(365));

        var req = new CertificateRequest("CN=WslNotifydWin", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, false));
        // req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") }, true));
        // req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.8") }, true));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        var rnd = new Random();
        var serial = new byte[4];
        rnd.NextBytes(serial);
        using var clientCertPublic = req.Create(serverCert, now.AddDays(-2), now.AddDays(365), serial);
        var clientCert = clientCertPublic.CopyWithPrivateKey(rsa);

        // File.WriteAllBytes("./server.pfx", serverCert.Export(X509ContentType.Pfx));
        // File.WriteAllBytes("./client.pfx", clientCert.Export(X509ContentType.Pfx));
        return (serverCert, clientCert);
    }

    private static void Main(string[] args)
    {
        var listenAddress = "https://127.0.0.1:0";
        (var serverCert, var clientCert) = CreateCerts();
        var builder = WebApplication.CreateBuilder(args);
#if DEBUG
        var notifydWinPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "../../../../WslNotifydWin/scripts/runner.sh");
        var workingDirectory = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "../../../../WslNotifydWin");
#else
        var notifydWinPath = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "WslNotifydWin/WslNotifydWin.exe");
        var workingDirectory = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "WslNotifydWin");
#endif
        builder.Services.AddSingleton<IHostedService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<WslNotifydWinProcessService>>();
            var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
            var server = serviceProvider.GetRequiredService<IServer>();
            var psi = new ProcessStartInfo(notifydWinPath)
            {
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true,
            };

            var msg = new CertificateMessage()
            {
                ServerCertificate = ByteString.CopyFrom(serverCert.Export(X509ContentType.Cert)),
                ClientCertificate = ByteString.CopyFrom(clientCert.Export(X509ContentType.Pfx)),
            };
            clientCert.Dispose();
            clientCert = null;

            var stdin = msg.ToByteArray();

            return new WslNotifydWinProcessService(logger, lifetime, server, psi, stdin);
        });
        builder.Services.AddGrpc();
        builder.Services.AddSingleton<IHostedService, DBusNotificationService>();
        builder.Services.AddSingleton<Notifications>();
        builder.Services.Configure<KestrelServerOptions>(kestrelOptions =>
        {
            kestrelOptions.ConfigureHttpsDefaults(httpsOptions =>
            {
                var logger = kestrelOptions.ApplicationServices.GetRequiredService<ILogger<Program>>(); // TODO: better category
                httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                httpsOptions.ClientCertificateValidation = (cert, chain, errors) =>
                {
                    if (errors == SslPolicyErrors.None)
                    {
                        return true;
                    }
                    chain ??= new X509Chain();
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(serverCert);
                    var result = chain.Build(cert);
                    logger.LogDebug("server check: {0}", result);
                    return result;
                };
                httpsOptions.ServerCertificate = serverCert;
                httpsOptions.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;
            });
        });

        var app = builder.Build();
        app.MapGrpcService<NotifierService>();
        app.Run(listenAddress);
    }
}
