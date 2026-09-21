using Tappy.Windows.Input;

namespace Tappy.Windows.Tests;

public sealed class WirelessKeyboardMetadataTests
{
    [Fact]
    public void Unifying_queries_are_receiver_information_reads_only()
    {
        foreach (var sub in Enumerable.Range(0x20, 6).Concat(Enumerable.Range(0x40, 6)))
        { Assert.Equal(new byte[] { 0x10, 0xFF, 0x83, 0xB5, (byte)sub, 0, 0 }, NativeUnifyingNameReader.ReadRequest((byte)sub)); }
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeUnifyingNameReader.ReadRequest(0xB2));
        Assert.Throws<ArgumentOutOfRangeException>(() => NativeUnifyingNameReader.ReadRequest(0x26));
    }

    [Fact]
    public void Receiver_responses_must_match_receiver_register_slot_and_length()
    {
        var name = new byte[20];
        new byte[] { 0x11, 0xFF, 0x83, 0xB5, 0x40, 9 }.CopyTo(name, 0);
        System.Text.Encoding.ASCII.GetBytes("K400 Plus").CopyTo(name, 6);
        Assert.Equal("Logitech K400 Plus", NativeUnifyingNameReader.ReadName(name, 0x40));
        Assert.Null(NativeUnifyingNameReader.ReadName(name, 0x41));
        Assert.Null(NativeUnifyingNameReader.ReadName(name[..12], 0x40));
        name[5] = 15;
        Assert.Null(NativeUnifyingNameReader.ReadName(name, 0x40));
        name[1] = 1;
        Assert.False(NativeUnifyingNameReader.IsReply(name, 0x40));
        Assert.True(NativeUnifyingNameReader.IsError([0x10, 0xFF, 0x8F, 0x83, 0xB5, 2, 0]));
        Assert.False(NativeUnifyingNameReader.IsError([0x10, 1, 0x8F, 0x83, 0xB5, 2, 0]));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(2, false)]
    [InlineData(9, false)]
    public void Only_keyboard_and_number_pad_pairings_label_keyboard_input(byte kind, bool expected)
    {
        var report = new byte[20];
        new byte[] { 0x11, 0xFF, 0x83, 0xB5, 0x20 }.CopyTo(report, 0);
        report[11] = kind;
        Assert.Equal(expected, NativeUnifyingNameReader.IsKeyboard(report, 0x20));
    }

    [Fact]
    public void Paired_names_are_not_claimed_as_live_connections_and_never_expose_raw_paths()
    {
        Assert.Equal("K400 Plus — Unifying (paired)", new WirelessKeyboardMetadata(false, null, ["K400 Plus"]).Apply("fallback"));
        Assert.Equal("Unifying receiver — paired: K400 Plus / K350", new WirelessKeyboardMetadata(false, null, ["K400 Plus", "K350"]).Apply("fallback"));
        Assert.Equal("fallback", new WirelessKeyboardMetadata(false, null, [@"\\?\HID#PRIVATE"]).Apply("fallback"));
        Assert.Equal("Pad — Bluetooth", new WirelessKeyboardMetadata(true, "Pad\u202E\n", []).Apply("fallback"));
    }

    [Fact]
    public void Bluetooth_name_comes_from_exact_container_not_the_radio_or_another_paired_device()
    {
        var container = Guid.NewGuid();
        KeyboardPnpNode[] nodes = [new("HID\\PAD", container, null, null),
            new("BTHENUM\\SERVICE", container, null, "Microsoft Keyboard for Bluetooth"),
            new("USB\\RADIO", Guid.NewGuid(), "Intel Bluetooth", null)];
        KeyboardPnpNode[] paired = [new("BTHENUM\\DEV_A", Guid.NewGuid(), "Other keyboard", null),
            new("BTHENUM\\DEV_B", container, "Microsoft Bluetooth Number Pad", null)];
        var value = WindowsKeyboardMetadata.Resolve(nodes, "HID Keyboard Device", paired);
        Assert.True(value.IsBluetooth);
        Assert.Equal("Microsoft Bluetooth Number Pad — Bluetooth", value.Apply("fallback"));
        Assert.Equal("Microsoft Keyboard for Bluetooth", WindowsKeyboardMetadata.Resolve(nodes, null, paired[..1]).BluetoothName);
    }

    [Fact]
    public void Bluetooth_LE_and_missing_names_have_honest_fallbacks()
    {
        var container = Guid.NewGuid();
        Assert.Equal("Office Pad — Bluetooth", WindowsKeyboardMetadata.Resolve(
            [new("BTHLEDEVICE\\SERVICE", container, null, null)], null,
            [new("BTHLE\\DEV_ONE", container, "Office Pad", null)]).Apply("fallback"));
        Assert.Equal("Keyboard input (model unknown) — Bluetooth", WindowsKeyboardMetadata.Resolve(
            [new("BTHENUM\\SERVICE", container, "Bluetooth Device", null),
                new("USB\\RADIO", null, "Host radio", null)], null).Apply("fallback"));
        Assert.False(WindowsKeyboardMetadata.Resolve([new("USB\\KEYBOARD", container, "USB keyboard", null)], null).IsBluetooth);
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("00000000-0000-0000-FFFF-FFFFFFFFFFFF")]
    public void Shared_or_empty_containers_do_not_borrow_paired_names(string id)
    {
        var container = new Guid(id);
        var value = WindowsKeyboardMetadata.Resolve([new("BTHENUM\\SERVICE", container, null, null)], null,
            [new("BTHENUM\\DEV_A", container, "Unrelated keyboard", null)]);
        Assert.Null(value.BluetoothName);
    }

    [Fact]
    public async Task Name_discovery_is_nonblocking_cached_and_discards_removed_device_results()
    {
        var completion = new TaskCompletionSource<WirelessKeyboardMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var discovery = new WirelessKeyboardMetadataDiscovery(_ => { Interlocked.Increment(ref count); return completion.Task; });
        var candidate = new RawKeyboardDeviceCandidate((nint)1, "path1", 0x046D, 0xC52B, Guid.NewGuid());
        Assert.Equal(WirelessKeyboardMetadata.Empty, discovery.Get(candidate));
        Assert.True(discovery.IsPending);
        discovery.Get(candidate);
        completion.SetResult(new(false, null, ["K400 Plus"]));
        await WaitUntil(() => !discovery.IsPending);
        Assert.Equal(1, count);
        Assert.True(discovery.ConsumeChange());
        Assert.False(discovery.ConsumeChange());
        Assert.Equal("K400 Plus — Unifying (paired)", discovery.Get(candidate).Apply("fallback"));
        Assert.Equal(1, count);

        var late = new TaskCompletionSource<WirelessKeyboardMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        discovery = new WirelessKeyboardMetadataDiscovery(_ => late.Task);
        discovery.Get(candidate);
        discovery.Retain([]);
        late.SetResult(new(false, null, ["Old keyboard"]));
        await Task.Delay(50);
        Assert.False(discovery.IsPending);
        Assert.False(discovery.ConsumeChange());
    }

    [Fact]
    public async Task Failed_discovery_preserves_the_generic_input_source_and_is_not_retried_in_a_loop()
    {
        var calls = 0;
        var discovery = new WirelessKeyboardMetadataDiscovery(_ => { calls++; throw new IOException("Busy"); });
        var candidate = new RawKeyboardDeviceCandidate((nint)1, "path", 0x046D, 0xC52B, Guid.NewGuid());
        discovery.Get(candidate);
        await WaitUntil(() => !discovery.IsPending);
        Assert.Equal("Receiver", discovery.Get(candidate).Apply("Receiver"));
        Assert.False(discovery.ConsumeChange());
        Assert.Equal(1, calls);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) { await Task.Delay(10, timeout.Token); }
    }
}
