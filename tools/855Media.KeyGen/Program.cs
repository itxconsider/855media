using System;
using System.IO;
using System.Text.Json;
using _855Media.Core.Licensing;

namespace _855Media.KeyGen;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("=== 855Media Commercial License Generator ===");

        if (args.Length == 0 || args[0] == "help")
        {
            ShowHelp();
            return;
        }

        var cmd = args[0].ToLowerInvariant();

        switch (cmd)
        {
            case "gen-keys":
                GenerateKeys();
                break;

            case "gen-serial":
                GenerateSerials(args);
                break;

            case "create-license":
                CreateLicense(args);
                break;

            case "verify":
                VerifyLicense(args);
                break;

            default:
                Console.WriteLine($"Unknown command: {cmd}");
                ShowHelp();
                break;
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine(@"
Usage:
  dotnet run -- gen-keys
      Generates a new RSA 2048-bit Public and Private keypair.

  dotnet run -- create-license <customerName> <customerEmail> [machineId] [daysValid] [privateKeyFile]
      Creates a cryptographically signed license token.
      - If machineId is 'any' or omitted, license is valid on any machine.
      - If daysValid is '0' or omitted, license is lifetime (never expires).

  dotnet run -- verify <tokenFileOrString> [publicKeyFile]
      Verifies and prints the contents of a license token.

  dotnet run -- gen-serial [count]
      Generates offline short serial keys (e.g. 855M-XXXX-XXXX-XXXX-XXXX).
");
    }

    private static void GenerateSerials(string[] args)
    {
        var count = args.Length > 1 && int.TryParse(args[1], out var c) ? Math.Clamp(c, 1, 100) : 5;
        Console.WriteLine($"\n--- Generated {count} Lifetime Serial Keys ---");
        for (var i = 0; i < count; i++)
        {
            var key = LicenseCrypto.GenerateShortSerialKey();
            Console.WriteLine(key);
        }
        Console.WriteLine("\nBuyers can paste any of these keys directly into the app to activate offline!\n");
    }

    private static void GenerateKeys()
    {
        var (pub, priv) = LicenseCrypto.GenerateKeyPair();
        File.WriteAllText("855media_public.key", pub);
        File.WriteAllText("855media_private.key", priv);

        Console.WriteLine("Keys generated successfully!");
        Console.WriteLine("Public key saved to: 855media_public.key");
        Console.WriteLine("Private key saved to: 855media_private.key (KEEP THIS SECRET!)");
        Console.WriteLine("\nPublic Key Base64:\n" + pub);
    }

    private static void CreateLicense(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: dotnet run -- create-license <customerName> <customerEmail> [machineId] [daysValid] [privateKeyFile]");
            return;
        }

        var name = args[1];
        var email = args[2];
        var machineId = args.Length > 3 && args[3] != "any" ? args[3] : string.Empty;
        var days = args.Length > 4 && int.TryParse(args[4], out var d) ? d : 0;
        var privKeyFile = args.Length > 5 ? args[5] : "855media_private.key";

        if (!File.Exists(privKeyFile))
        {
            Console.WriteLine($"Private key file not found at: {privKeyFile}");
            Console.WriteLine("Run 'dotnet run -- gen-keys' first to generate your keys.");
            return;
        }

        var privKey = File.ReadAllText(privKeyFile).Trim();

        var payload = new LicensePayload
        {
            LicenseKey = $"855M-{Guid.NewGuid():N}"[..19].ToUpperInvariant(),
            CustomerName = name,
            CustomerEmail = email,
            MachineFingerprint = machineId,
            Type = days > 0 ? LicenseType.Annual : LicenseType.Lifetime,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = days > 0 ? DateTime.UtcNow.AddDays(days) : null,
            AllowedFeatures = ["All"],
        };

        var token = LicenseCrypto.CreateSignedToken(payload, privKey);
        var filename = $"license_{email.Replace('@', '_').Replace('.', '_')}.lic";
        File.WriteAllText(filename, token);

        Console.WriteLine("\n--- License Generated Successfully ---");
        Console.WriteLine($"License Key:  {payload.LicenseKey}");
        Console.WriteLine($"Customer:     {payload.CustomerName} ({payload.CustomerEmail})");
        Console.WriteLine($"Machine Lock: {(string.IsNullOrWhiteSpace(machineId) ? "Any Machine" : machineId)}");
        Console.WriteLine($"Type:         {payload.Type} (Expires: {payload.ExpiresAt?.ToString("yyyy-MM-dd") ?? "Never"})");
        Console.WriteLine($"Saved to:     {filename}");
        Console.WriteLine($"\nToken String:\n{token}\n");
    }

    private static void VerifyLicense(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: dotnet run -- verify <tokenFileOrString> [publicKeyFile]");
            return;
        }

        var input = args[1];
        var token = File.Exists(input) ? File.ReadAllText(input).Trim() : input.Trim();
        var pubKey = args.Length > 2 && File.Exists(args[2])
            ? File.ReadAllText(args[2]).Trim()
            : null;

        var (isValid, payload, error) = LicenseCrypto.VerifyToken(token, pubKey);

        if (!isValid || payload is null)
        {
            Console.WriteLine($"License verification FAILED: {error}");
            return;
        }

        Console.WriteLine("License verification SUCCESSFUL!");
        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }
}
