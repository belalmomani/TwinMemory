using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TwinMemory.Net;

/// <summary>Self-signed TLS certificate for the local server. Phones pin its SHA-256 fingerprint at pairing time.</summary>
public static class CertStore
{
    const X509KeyStorageFlags Flags = X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable;

    public static X509Certificate2 LoadOrCreate(string dir)
    {
        var path = Path.Combine(dir, "server.pfx");
        if (File.Exists(path))
        {
            try { return new X509Certificate2(path, (string?)null, Flags); } catch { /* regenerate */ }
        }
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=TwinMemory", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        using var self = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        var pfx = self.Export(X509ContentType.Pfx);
        File.WriteAllBytes(path, pfx);
        return new X509Certificate2(pfx, (string?)null, Flags);
    }

    public static string Fingerprint(X509Certificate2 c) => Convert.ToHexString(c.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant();
}
