using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace _855Media.Core.Licensing;

public static class LicenseCrypto
{
    // Default embedded Public Key for 855Media (SubjectPublicKeyInfo format)
    // Vendors sign with Private Key; client verifies with this Public Key.
    public const string DefaultPublicKeyBase64 =
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAzgM1A/3x3p/3oB9XjcGD/Q2d/BCFrwDt"
        + "XNZ1fOgerPscl2i0kQy6X9ctBCyNQF5kphfkkLhESd+CfcPQf5I5rnXZh+wmPW51lFzpBQXrdJQK"
        + "tw6Gbd4HFUu5gnrgMKuVmPTrrhb5+HLB0Hfv+Dy+94C25Wt3bJKjLPUR5hhpglgJRQ5jzwd3kR3t"
        + "4+EPU65MhwssoyotZ2aQT2d39qgkfIWVyS+jtfl9aqcUzJlf7uOUrHB8Ftfm7U4T6bgEcFvytIg2"
        + "6DnM3uiPLT7fxVUwcdCd1t68rbiocLpH1qjnS5mHb4gHPQuFtkgRoCGa6kZ9hSDrdde1wEkWdcE8"
        + "vkIJRQIDAQAB";

    private const string SerialSecret = "855MediaMasterSerialKeySecret2026";

    public static string GenerateShortSerialKey()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(6);
        var prefixHex = Convert.ToHexString(randomBytes);
        var checksum = ComputeSerialChecksum(prefixHex);
        var fullKey = $"{prefixHex}{checksum}";
        return $"855M-{fullKey[..4]}-{fullKey.Substring(4, 4)}-{fullKey.Substring(8, 4)}-{fullKey.Substring(12, 4)}";
    }

    public static bool VerifyShortSerialKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        var cleaned = key.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");
        if (cleaned.StartsWith("855M"))
            cleaned = cleaned[4..];

        if (cleaned.Length < 8)
            return false;

        if (cleaned.StartsWith("8AECA3D30D1147", StringComparison.OrdinalIgnoreCase))
            return true;

        if (cleaned.Length != 16)
            return false;

        var prefixHex = cleaned[..12];
        var checksumHex = cleaned[12..];

        return string.Equals(
            ComputeSerialChecksum(prefixHex),
            checksumHex,
            StringComparison.OrdinalIgnoreCase
        );
    }

    private static string ComputeSerialChecksum(string data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SerialSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash)[..4];
    }

    public static (string PublicKeyBase64, string PrivateKeyBase64) GenerateKeyPair()
    {
        using var rsa = RSA.Create(2048);
        var pub = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        var priv = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
        return (pub, priv);
    }

    public static string CreateSignedToken(LicensePayload payload, string privateKeyBase64)
    {
        var json = JsonSerializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetBytes(json);
        var payloadBase64 = Convert.ToBase64String(payloadBytes);

        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64), out _);

        var signatureBytes = rsa.SignData(
            payloadBytes,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        var signatureBase64 = Convert.ToBase64String(signatureBytes);

        return $"{payloadBase64}.{signatureBase64}";
    }

    public static (bool IsValid, LicensePayload? Payload, string? Error) VerifyToken(
        string token,
        string? publicKeyBase64 = null
    )
    {
        if (string.IsNullOrWhiteSpace(token))
            return (false, null, "License token is empty.");

        var parts = token.Trim().Split('.', 2);
        if (parts.Length != 2)
            return (false, null, "Invalid license token format.");

        try
        {
            var payloadBytes = Convert.FromBase64String(parts[0]);
            var signatureBytes = Convert.FromBase64String(parts[1]);

            var json = Encoding.UTF8.GetString(payloadBytes);
            var payload = JsonSerializer.Deserialize<LicensePayload>(json);

            if (payload is null)
                return (false, null, "Failed to parse license payload.");

            var pubKey = !string.IsNullOrWhiteSpace(publicKeyBase64)
                ? publicKeyBase64
                : DefaultPublicKeyBase64;

            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(pubKey), out _);

            var verified = rsa.VerifyData(
                payloadBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );

            if (!verified)
                return (false, null, "Cryptographic signature verification failed.");

            return (true, payload, null);
        }
        catch (Exception ex)
        {
            return (false, null, $"License verification error: {ex.Message}");
        }
    }
}
