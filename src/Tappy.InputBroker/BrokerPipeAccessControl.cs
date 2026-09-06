using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Tappy.InputBroker;

internal static class BrokerPipeAccessControl
{
    public static PipeSecurity Create(SecurityIdentifier allowedUser)
    {
        ArgumentNullException.ThrowIfNull(allowedUser);
        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new PipeAccessRule(
            localSystem,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            allowedUser,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        return security;
    }
}
