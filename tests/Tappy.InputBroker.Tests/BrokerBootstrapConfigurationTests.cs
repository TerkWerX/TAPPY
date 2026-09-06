using System.Security.Principal;

namespace Tappy.InputBroker.Tests;

public sealed class BrokerBootstrapConfigurationTests
{
    [Fact]
    public void Specific_user_and_256_bit_key_are_accepted_and_key_is_zeroed_on_dispose()
    {
        var key = Convert.ToBase64String(Enumerable.Repeat((byte)0x5A, 32).ToArray());
        var configuration = BrokerBootstrapConfiguration.Create("S-1-5-21-1-2-3-1001", key);
        var retainedKey = configuration.AuthenticationKey;

        configuration.Dispose();

        Assert.All(retainedKey, value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData("S-1-1-0")]
    [InlineData("S-1-5-11")]
    [InlineData("S-1-5-18")]
    [InlineData("S-1-5-32-545")]
    [InlineData("S-1-5-32-544")]
    public void Broad_or_privileged_identity_is_rejected(string sid)
    {
        var key = Convert.ToBase64String(new byte[32]);

        Assert.Throws<InvalidDataException>(() => BrokerBootstrapConfiguration.Create(sid, key));
    }

    [Fact]
    public void Short_key_is_rejected()
    {
        var key = Convert.ToBase64String(new byte[31]);

        Assert.Throws<InvalidDataException>(() =>
            BrokerBootstrapConfiguration.Create("S-1-5-21-1-2-3-1001", key));
    }

    [Fact]
    public void Pipe_acl_contains_only_system_and_the_configured_user()
    {
        var allowedUser = new SecurityIdentifier("S-1-5-21-1-2-3-1001");
        var security = BrokerPipeAccessControl.Create(allowedUser);

        var rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            typeof(SecurityIdentifier))
            .Cast<System.IO.Pipes.PipeAccessRule>()
            .ToArray();

        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(2, rules.Length);
        Assert.Contains(rules, rule => rule.IdentityReference.Equals(allowedUser));
        Assert.Contains(rules, rule =>
            rule.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)));
    }
}
