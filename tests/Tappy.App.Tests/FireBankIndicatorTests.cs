using Tappy.App.Runtime;
using Tappy.Core.Models;
using Tappy.Windows.Input;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class DeviceAwareControllerRuntimeTests
{
    [Fact]
    public async Task Confirming_a_new_Fire_creates_four_banks_by_default()
    {
        // The Fire's own physical Modes/Bank indicator has exactly four
        // positions (Channel Rack / Mixer / User 1 / User 2), so Tappy's own
        // bank count now matches it rather than the generic 3-layer default
        // every other MIDI controller still gets.
        var root = NewTemporaryDirectory();
        try
        {
            var midiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            var midiProvider = new WinMmMidiInputProvider(midiBackend);
            await using var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new FakeEnumerator(), new FakeMessageHost(), keyboardIsNeutral: static () => true),
                logitechG13Provider: null,
                new RecordingOutput(),
                new AtomicProfileStore(root),
                actionOutput: new RecordingActionOutput(),
                midiInputProvider: midiProvider);
            await runtime.InitializeAsync();
            var device = Assert.Single(runtime.Devices);
            Assert.True(runtime.BeginIdentification(device).Succeeded);
            midiBackend.Publish(PackMidi(0x90, 60, 127));
            midiBackend.Publish(PackMidi(0x80, 60, 0));
            Assert.True(runtime.ConfirmController().Succeeded);

            var endpoint = runtime.GetBankEndpoint();

            Assert.Equal(4, endpoint!.Banks.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task An_existing_saved_Fire_profile_with_three_banks_gains_a_fourth_on_reconfirm()
    {
        // Mirrors A_stale_saved_Fire_layout_is_reset_to_the_current_shape_not_scrambled_into_it's
        // approach (D-022): simulate a profile saved before the bank count
        // grew from 3 to 4, then confirm the same device again and check the
        // fourth bank appears without disturbing the first three.
        var root = NewTemporaryDirectory();
        try
        {
            var midiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            var midiProvider = new WinMmMidiInputProvider(midiBackend);
            await using (var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new FakeEnumerator(), new FakeMessageHost(), keyboardIsNeutral: static () => true),
                logitechG13Provider: null,
                new RecordingOutput(),
                new AtomicProfileStore(root),
                actionOutput: new RecordingActionOutput(),
                midiInputProvider: midiProvider))
            {
                await runtime.InitializeAsync();
                var device = Assert.Single(runtime.Devices);
                Assert.True(runtime.BeginIdentification(device).Succeeded);
                midiBackend.Publish(PackMidi(0x90, 60, 127));
                midiBackend.Publish(PackMidi(0x80, 60, 0));
                Assert.True(runtime.ConfirmController().Succeeded);
                Assert.True((await runtime.SaveProfileAsync()).Succeeded);
            }
            midiBackend.Dispose();

            // Truncate to 3 banks the way an old, pre-upgrade save would read.
            var store = new AtomicProfileStore(root);
            var profile = (await store.LoadAsync("default")).ToEditableProfile();
            var fire = Assert.Single(profile.Controllers,
                c => c.Identity.ProviderId == WinMmMidiInputProvider.ProviderId);
            Assert.True(fire.Layers.Count >= 3);
            fire.Layers.RemoveRange(3, fire.Layers.Count - 3);
            fire.Layers[1].Name = "My Custom Bank 2"; // A user rename must survive the upgrade.
            await store.SaveAsync(profile.CreateSnapshot());

            var restoredMidiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            await using var restored = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new FakeEnumerator(), new FakeMessageHost(), keyboardIsNeutral: static () => true),
                logitechG13Provider: null,
                new RecordingOutput(),
                new AtomicProfileStore(root),
                actionOutput: new RecordingActionOutput(),
                midiInputProvider: new WinMmMidiInputProvider(restoredMidiBackend));
            await restored.InitializeAsync();
            var restoredDevice = Assert.Single(restored.Devices);
            Assert.True(restored.BeginIdentification(restoredDevice).Succeeded);
            restoredMidiBackend.Publish(PackMidi(0x90, 60, 127));
            restoredMidiBackend.Publish(PackMidi(0x80, 60, 0));
            Assert.True(restored.ConfirmController().Succeeded);

            var endpoint = restored.GetBankEndpoint();

            Assert.Equal(4, endpoint!.Banks.Count);
            Assert.Contains("My Custom Bank 2", endpoint.Banks[1].Label);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ChangeBank_on_a_confirmed_Fire_advances_through_all_four_banks_without_throwing()
    {
        // Also exercises SyncFireBankIndicatorLocked's no-real-output-wired
        // path (no WinMmMidiOutput is wired to this runtime) — a failed
        // physical LED sync must never block the bank change itself.
        var root = NewTemporaryDirectory();
        try
        {
            var midiBackend = new FakeMidiBackend(new WinMmMidiInputDevice(0, "FL STUDIO FIRE", 1, 2, 3));
            var midiProvider = new WinMmMidiInputProvider(midiBackend);
            await using var runtime = new DeviceAwareControllerRuntime(
                new RawInputKeyboardProvider(new FakeEnumerator(), new FakeMessageHost(), keyboardIsNeutral: static () => true),
                logitechG13Provider: null,
                new RecordingOutput(),
                new AtomicProfileStore(root),
                actionOutput: new RecordingActionOutput(),
                midiInputProvider: midiProvider);
            await runtime.InitializeAsync();
            var device = Assert.Single(runtime.Devices);
            Assert.True(runtime.BeginIdentification(device).Succeeded);
            midiBackend.Publish(PackMidi(0x90, 60, 127));
            midiBackend.Publish(PackMidi(0x80, 60, 0));
            Assert.True(runtime.ConfirmController().Succeeded);
            var endpoint = runtime.GetBankEndpoint()!;

            var seenLabels = new List<string>();
            for (var i = 0; i < 4; i++)
            {
                var result = runtime.ChangeBank(endpoint, BankControlOperation.Next);
                Assert.True(result.Succeeded, result.Message);
                seenLabels.Add(runtime.GetBankEndpoint()!.ActiveLayerId);
            }

            // Four "next" presses from bank 1 visit banks 2, 3, 4, then wrap
            // back to 1.
            Assert.Equal(endpoint.ActiveLayerId, seenLabels[3]);
            Assert.Equal(4, seenLabels.Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
