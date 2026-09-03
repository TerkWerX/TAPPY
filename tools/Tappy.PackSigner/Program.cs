using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tappy.PackSigner;

internal static class Program
{
    private const string PrivateKeyPathEnvironmentVariable = "TAPPY_PACK_SIGNING_KEY_PATH";
    private const long MaximumPayloadBytes = 80L * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".png", ".csv"
    };

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args.Any(argument => argument is "--help" or "-h" or "/?"))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "sign" when args.Length == 3 => Sign(args[1], args[2]),
                "verify" when args.Length == 3 => Verify(args[1], args[2]),
                _ => UsageError()
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           InvalidDataException or JsonException or CryptographicException or
                                           ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Tappy.PackSigner: {exception.Message}");
            return 1;
        }
    }

    private static int Sign(string manifestArgument, string publisherId)
    {
        var keyArgument = Environment.GetEnvironmentVariable(PrivateKeyPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(keyArgument))
        {
            throw new InvalidDataException(
                $"Set {PrivateKeyPathEnvironmentVariable} to a private PEM path outside the source repository.");
        }

        var manifestPath = RequireFile(manifestArgument, "Manifest");
        var privateKeyPath = RequireFile(keyArgument, "Private key");
        RefuseRepositoryKey(privateKeyPath, manifestPath);

        var manifest = LoadAndValidateManifest(manifestPath, publisherId);
        using var rsa = RSA.Create();
        ImportPem(rsa, privateKeyPath);
        var signature = rsa.SignData(
            BuildSignaturePayload(manifest),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        manifest["publisher_id"] = publisherId;
        manifest["signature_algorithm"] = "RSA-SHA256";
        manifest["signature"] = Convert.ToBase64String(signature);
        WriteManifestAtomically(manifestPath, manifest);

        Console.WriteLine($"Signed {Path.GetFileName(manifestPath)} for publisher '{publisherId}'.");
        Console.WriteLine("The private key was not copied, printed, or written to the manifest.");
        return 0;
    }

    private static int Verify(string manifestArgument, string publicKeyArgument)
    {
        var manifestPath = RequireFile(manifestArgument, "Manifest");
        var publicKeyPath = RequireFile(publicKeyArgument, "Public key");
        var manifest = LoadAndValidateManifest(manifestPath, null);
        var algorithm = RequiredString(manifest, "signature_algorithm");
        if (!algorithm.Equals("RSA-SHA256", StringComparison.OrdinalIgnoreCase))
        {
            throw new CryptographicException($"Unsupported signature algorithm '{algorithm}'.");
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(RequiredString(manifest, "signature"));
        }
        catch (FormatException exception)
        {
            throw new CryptographicException("Manifest signature is not valid Base64.", exception);
        }

        using var rsa = RSA.Create();
        ImportPem(rsa, publicKeyPath);
        if (!rsa.VerifyData(BuildSignaturePayload(manifest), signature, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1))
        {
            throw new CryptographicException("Manifest signature verification failed.");
        }

        Console.WriteLine($"Verified {Path.GetFileName(manifestPath)} and every declared payload hash.");
        return 0;
    }

    private static JsonObject LoadAndValidateManifest(string manifestPath, string? publisherOverride)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
                       ?? throw new InvalidDataException("The manifest is not a JSON object.");
        var packId = RequiredString(manifest, "pack_id");
        var version = RequiredString(manifest, "version");
        var publisherId = publisherOverride ?? RequiredString(manifest, "publisher_id");
        if (!System.Text.RegularExpressions.Regex.IsMatch(packId, "^[a-z0-9][a-z0-9._-]{0,79}$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException("pack_id must be a lowercase, filesystem-safe identifier of at most 80 characters.");
        }
        if (!Version.TryParse(version, out var parsedVersion) || parsedVersion.Major < 0 || version.Count(c => c == '.') != 2)
        {
            throw new InvalidDataException("version must use numeric major.minor.patch form.");
        }
        if (string.IsNullOrWhiteSpace(publisherId) || publisherId.Length > 120)
        {
            throw new InvalidDataException("publisher_id is missing or too long.");
        }

        manifest["publisher_id"] = publisherId;
        var files = manifest["files"]?.AsArray()
                    ?? throw new InvalidDataException("The manifest must contain a files array.");
        if (files.Count == 0 || files.Count > 512)
        {
            throw new InvalidDataException("The manifest must declare between 1 and 512 data files.");
        }

        var root = Path.GetDirectoryName(manifestPath)
                   ?? throw new InvalidDataException("The manifest directory could not be resolved.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var node in files)
        {
            var file = node?.AsObject() ?? throw new InvalidDataException("Every files entry must be an object.");
            var relative = NormalizeRelativePath(RequiredString(file, "path"));
            if (!seen.Add(relative))
            {
                throw new InvalidDataException($"The manifest declares '{relative}' more than once.");
            }
            if (!AllowedExtensions.Contains(Path.GetExtension(relative)))
            {
                throw new InvalidDataException($"Unsupported data file type in '{relative}'.");
            }

            var payloadPath = ResolveUnderRoot(root, relative);
            if (!File.Exists(payloadPath))
            {
                throw new InvalidDataException($"Declared payload file is missing: {relative}");
            }
            if ((File.GetAttributes(payloadPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Symbolic links and reparse points are not permitted: {relative}");
            }
            totalBytes = checked(totalBytes + new FileInfo(payloadPath).Length);
            if (totalBytes > MaximumPayloadBytes)
            {
                throw new InvalidDataException("Declared payload exceeds the 80 MB safety limit.");
            }

            var expectedHash = NormalizeHash(RequiredString(file, "sha256"));
            using var payloadStream = File.OpenRead(payloadPath);
            var actualHash = Convert.ToHexString(SHA256.HashData(payloadStream));
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"SHA-256 does not match for '{relative}'.");
            }
            file["path"] = relative;
            file["sha256"] = actualHash;
        }

        return manifest;
    }

    private static byte[] BuildSignaturePayload(JsonObject manifest)
    {
        var builder = new StringBuilder();
        builder.Append(RequiredString(manifest, "pack_id").Trim()).Append('\n')
            .Append(RequiredString(manifest, "version").Trim()).Append('\n')
            .Append(RequiredString(manifest, "publisher_id").Trim()).Append('\n');

        var files = manifest["files"]!.AsArray()
            .Select(node => node!.AsObject())
            .OrderBy(file => RequiredString(file, "path"), StringComparer.Ordinal);
        foreach (var file in files)
        {
            builder.Append(NormalizeRelativePath(RequiredString(file, "path")))
                .Append(':')
                .Append(NormalizeHash(RequiredString(file, "sha256")))
                .Append('\n');
        }
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void ImportPem(RSA rsa, string keyPath)
    {
        if ((File.GetAttributes(keyPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("PEM key files cannot be symbolic links or reparse points.");
        }
        if (new FileInfo(keyPath).Length > 1024 * 1024)
        {
            throw new InvalidDataException("A PEM key file cannot exceed 1 MB.");
        }
        var bytes = File.ReadAllBytes(keyPath);
        var characters = Encoding.UTF8.GetChars(bytes);
        try
        {
            rsa.ImportFromPem(characters);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            Array.Clear(characters);
        }
    }

    private static void RefuseRepositoryKey(string privateKeyPath, string manifestPath)
    {
        var repositories = new[]
        {
            FindRepositoryRoot(Path.GetDirectoryName(manifestPath)!),
            FindRepositoryRoot(Environment.CurrentDirectory),
            FindRepositoryRoot(AppContext.BaseDirectory)
        }.Where(path => path is not null).Distinct(StringComparer.OrdinalIgnoreCase);
        if (repositories.Any(repository => IsUnder(privateKeyPath, repository!)))
        {
            throw new InvalidDataException("Refusing to read a private signing key stored inside the source repository.");
        }
    }

    private static string? FindRepositoryRoot(string start)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(start)); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }
        }
        return null;
    }

    private static bool IsUnder(string path, string root)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveUnderRoot(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Payload path escapes the manifest directory: {relative}");
        }
        return path;
    }

    private static string NormalizeRelativePath(string value)
    {
        var normalized = value.Replace('\\', '/').Trim();
        if (normalized.Length == 0 || normalized.StartsWith('/') || normalized.Contains(':') ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException($"Unsafe relative payload path: {value}");
        }
        return normalized;
    }

    private static string NormalizeHash(string value)
    {
        var normalized = value.Replace("sha256:", string.Empty, StringComparison.OrdinalIgnoreCase).Trim().ToUpperInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("Every sha256 value must contain exactly 64 hexadecimal characters.");
        }
        return normalized;
    }

    private static string RequiredString(JsonObject node, string property)
    {
        var value = node[property]?.GetValue<string>()?.Trim();
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"Manifest property '{property}' is required.")
            : value;
    }

    private static string RequireFile(string argument, string label)
    {
        var path = Path.GetFullPath(argument);
        return File.Exists(path) ? path : throw new FileNotFoundException($"{label} file was not found.");
    }

    private static void WriteManifestAtomically(string path, JsonObject manifest)
    {
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine,
                new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static int UsageError()
    {
        Console.Error.WriteLine("Invalid command line.");
        PrintHelp();
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Tappy controller-pack manifest signer");
        Console.WriteLine();
        Console.WriteLine("Sign:   Tappy.PackSigner sign <pack-manifest.json> <publisher-id>");
        Console.WriteLine($"        Reads the private PEM path from {PrivateKeyPathEnvironmentVariable}.");
        Console.WriteLine("Verify: Tappy.PackSigner verify <pack-manifest.json> <public-key.pem>");
        Console.WriteLine();
        Console.WriteLine("The private key must remain outside the Git repository. The tool verifies every declared");
        Console.WriteLine("payload hash before signing and never prints, copies, embeds, or persists private-key data.");
    }
}
