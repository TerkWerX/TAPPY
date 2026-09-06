using Tappy.Windows.ExclusiveInput;

namespace Tappy.Windows.Tests;

public sealed class TappyKeyboardFilterBrokerCoordinatorTests
{
    [Fact]
    public void Primary_pass_through_is_acknowledged_before_secondary_suppression()
    {
        var order = new List<string>();
        using var primary = FakeSession.Primary(order, 7);
        using var secondary = FakeSession.Secondary(order, 7, 750);
        using var coordinator = new TappyKeyboardFilterBrokerCoordinator(primary, secondary);

        coordinator.Arm(7, 750);

        Assert.True(coordinator.IsArmed);
        Assert.True(
            order.IndexOf("primary:query") < order.IndexOf("secondary:set"),
            $"Unexpected broker order: {string.Join(", ", order)}");

        coordinator.Stop();
        Assert.False(coordinator.IsArmed);
        Assert.True(
            order.IndexOf("secondary:fail-open") < order.IndexOf("primary:fail-open"),
            $"Unexpected fail-open order: {string.Join(", ", order)}");
    }

    [Fact]
    public void Bad_secondary_acknowledgement_closes_both_handles_and_cannot_remain_armed()
    {
        var order = new List<string>();
        using var primary = FakeSession.Primary(order, 9);
        using var secondary = FakeSession.Secondary(order, 9, 750) with
        {
            Status = Status(
                KeyboardFilterRole.ConfigurableSecondary,
                KeyboardFilterMode.PassThrough,
                9,
                false,
                0)
        };
        using var coordinator = new TappyKeyboardFilterBrokerCoordinator(primary, secondary);

        Assert.Throws<InvalidDataException>(() => coordinator.Arm(9, 750));

        Assert.False(coordinator.IsArmed);
        Assert.True(primary.Disposed);
        Assert.True(secondary.Disposed);
    }

    [Fact]
    public void Stale_generation_in_an_event_faults_and_closes_both_handles()
    {
        var order = new List<string>();
        using var primary = FakeSession.Primary(order, 11);
        using var secondary = FakeSession.Secondary(order, 11, 750) with
        {
            Events =
            [
                new KeyboardFilterEvent(1, 10, 0x1E, KeyboardFilterEventFlags.None, 0)
            ]
        };
        using var coordinator = new TappyKeyboardFilterBrokerCoordinator(primary, secondary);
        coordinator.Arm(11, 750);

        Assert.Throws<InvalidDataException>(() => coordinator.ReadEvents(16));

        Assert.False(coordinator.IsArmed);
        Assert.True(primary.Disposed);
        Assert.True(secondary.Disposed);
    }

    [Fact]
    public void Multi_interface_groups_acknowledge_every_primary_before_any_secondary_is_suppressed()
    {
        var order = new List<string>();
        using var primaryOne = FakeSession.Primary(order, 12, "primary-1");
        using var primaryTwo = FakeSession.Primary(order, 12, "primary-2");
        using var secondaryOne = FakeSession.Secondary(order, 12, 750, "secondary-1");
        using var secondaryTwo = FakeSession.Secondary(order, 12, 750, "secondary-2");
        using var coordinator = new TappyKeyboardFilterBrokerCoordinator(
            [primaryOne, primaryTwo],
            [secondaryOne, secondaryTwo]);

        coordinator.Arm(12, 750);
        coordinator.Heartbeat();
        coordinator.Stop();

        var firstSecondarySet = order.FindIndex(item => item.StartsWith("secondary-1:set", StringComparison.Ordinal));
        Assert.True(order.FindIndex(item => item == "primary-1:query") < firstSecondarySet);
        Assert.True(order.FindIndex(item => item == "primary-2:query") < firstSecondarySet);
        Assert.Contains("secondary-1:heartbeat", order);
        Assert.Contains("secondary-2:heartbeat", order);
        var firstPrimaryFailOpen = order.FindIndex(item => item.StartsWith("primary-1:fail-open", StringComparison.Ordinal));
        Assert.True(order.FindIndex(item => item == "secondary-1:fail-open") < firstPrimaryFailOpen);
        Assert.True(order.FindIndex(item => item == "secondary-2:fail-open") < firstPrimaryFailOpen);
    }

    [Fact]
    public void Duplicate_session_across_physical_groups_is_rejected()
    {
        var order = new List<string>();
        using var session = FakeSession.Primary(order, 13);

        Assert.Throws<ArgumentException>(() =>
            new TappyKeyboardFilterBrokerCoordinator([session], [session]));
    }

    [Fact]
    public void Multi_interface_reads_are_bounded_across_the_whole_secondary_group()
    {
        var order = new List<string>();
        using var primary = FakeSession.Primary(order, 14);
        using var secondaryOne = FakeSession.Secondary(order, 14, 750, "secondary-1") with
        {
            Events =
            [
                new KeyboardFilterEvent(1, 14, 0x1E, KeyboardFilterEventFlags.None, 0)
            ]
        };
        using var secondaryTwo = FakeSession.Secondary(order, 14, 750, "secondary-2") with
        {
            Events =
            [
                new KeyboardFilterEvent(1, 14, 0x30, KeyboardFilterEventFlags.None, 0)
            ]
        };
        using var coordinator = new TappyKeyboardFilterBrokerCoordinator(
            [primary],
            [secondaryOne, secondaryTwo]);
        coordinator.Arm(14, 750);

        var events = coordinator.ReadEvents(1);

        Assert.Single(events);
        Assert.Contains("secondary-1:read:1", order);
        Assert.DoesNotContain(order, item => item.StartsWith("secondary-2:read", StringComparison.Ordinal));
    }

    private static KeyboardFilterInstanceStatus Status(
        KeyboardFilterRole role,
        KeyboardFilterMode mode,
        ulong generation,
        bool watchdogArmed,
        uint watchdogTimeout) =>
        new(
            TappyKeyboardFilterProtocol.Version,
            mode,
            role,
            generation,
            watchdogArmed,
            watchdogTimeout,
            0,
            0);

    private sealed record FakeSession(string Name, List<string> Order) : ITappyKeyboardFilterDeviceSession
    {
        public required KeyboardFilterInstanceStatus Status { get; init; }
        public IReadOnlyList<KeyboardFilterEvent> Events { get; init; } = [];
        public bool Disposed { get; private set; }

        public static FakeSession Primary(List<string> order, ulong generation, string name = "primary") =>
            new(name, order)
            {
                Status = TappyKeyboardFilterBrokerCoordinatorTests.Status(
                    KeyboardFilterRole.ProtectedPrimary,
                    KeyboardFilterMode.PassThrough,
                    generation,
                    false,
                    0)
            };

        public static FakeSession Secondary(
            List<string> order,
            ulong generation,
            uint watchdogTimeout,
            string name = "secondary") =>
            new(name, order)
            {
                Status = TappyKeyboardFilterBrokerCoordinatorTests.Status(
                    KeyboardFilterRole.ConfigurableSecondary,
                    KeyboardFilterMode.CaptureAndSuppress,
                    generation,
                    true,
                    watchdogTimeout)
            };

        public void Begin() => Order.Add($"{Name}:begin");

        public KeyboardFilterInstanceStatus QueryStatus()
        {
            Order.Add($"{Name}:query");
            return Status;
        }

        public void SetPolicy(
            ulong policyGeneration,
            KeyboardFilterRole role,
            KeyboardFilterMode mode,
            uint watchdogTimeoutMilliseconds) =>
            Order.Add($"{Name}:set");

        public void Heartbeat(ulong policyGeneration) => Order.Add($"{Name}:heartbeat");

        public IReadOnlyList<KeyboardFilterEvent> ReadEvents(int maximumEvents)
        {
            Order.Add($"{Name}:read:{maximumEvents}");
            return Events.Take(maximumEvents).ToArray();
        }

        public void ForceFailOpen() => Order.Add($"{Name}:fail-open");

        public void Dispose()
        {
            Disposed = true;
            Order.Add($"{Name}:dispose");
        }
    }
}
