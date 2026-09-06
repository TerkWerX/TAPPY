using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace Tappy.InputBroker;

internal sealed class BrokerBootstrapConfiguration : IDisposable
{
    private bool _disposed;

    private BrokerBootstrapConfiguration(SecurityIdentifier allowedUser, byte[] authenticationKey)
    {
        AllowedUser = allowedUser;
        AuthenticationKey = authenticationKey;
    }

    public SecurityIdentifier AllowedUser { get; }

    public byte[] AuthenticationKey { get; }

    public static BrokerBootstrapConfiguration Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The broker bootstrap file does not exist.", fullPath);
        }

        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The broker bootstrap file cannot be a symbolic link or reparse point.");
        }

        var info = new FileInfo(fullPath);
        if (info.Length is <= 0 or > 16 * 1024)
        {
            throw new InvalidDataException("The broker bootstrap file has an invalid size.");
        }

        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.SequentialScan);
        var document = JsonSerializer.Deserialize<BrokerBootstrapDocument>(stream)
            ?? throw new InvalidDataException("The broker bootstrap file is empty.");
        return Create(document.AllowedUserSid, document.AuthenticationKeyBase64);
    }

    internal static BrokerBootstrapConfiguration Create(string? allowedUserSid, string? authenticationKeyBase64)
    {
        if (string.IsNullOrWhiteSpace(allowedUserSid))
        {
            throw new InvalidDataException("The broker bootstrap file must name one allowed user SID.");
        }

        SecurityIdentifier user;
        try
        {
            user = new SecurityIdentifier(allowedUserSid.Trim());
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("The broker bootstrap user SID is invalid.", ex);
        }

        if (!user.IsAccountSid() || IsBroadOrPrivilegedIdentity(user))
        {
            throw new InvalidDataException("The broker bootstrap identity must be one specific, non-SYSTEM user account.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(authenticationKeyBase64 ?? string.Empty);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("The broker authentication key is not valid Base64.", ex);
        }

        if (key.Length != BrokerPipeProtocol.MinimumKeySize)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidDataException("The broker authentication key must be exactly 256 bits.");
        }

        return new BrokerBootstrapConfiguration(user, key);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(AuthenticationKey);
        _disposed = true;
    }

    private static bool IsBroadOrPrivilegedIdentity(SecurityIdentifier identity)
    {
        ReadOnlySpan<WellKnownSidType> forbidden =
        [
            WellKnownSidType.WorldSid,
            WellKnownSidType.AuthenticatedUserSid,
            WellKnownSidType.BuiltinUsersSid,
            WellKnownSidType.BuiltinAdministratorsSid,
            WellKnownSidType.LocalSystemSid,
            WellKnownSidType.LocalServiceSid,
            WellKnownSidType.NetworkServiceSid
        ];
        foreach (var sidType in forbidden)
        {
            if (identity.IsWellKnown(sidType))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class BrokerBootstrapDocument
    {
        public string? AllowedUserSid { get; init; }

        public string? AuthenticationKeyBase64 { get; init; }
    }
}
