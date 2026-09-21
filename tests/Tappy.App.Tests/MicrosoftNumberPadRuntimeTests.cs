using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tappy.App.Runtime;
using Tappy.App.Services;
using Tappy.App.ViewModels;
using Tappy.Windows.Input;
using Tappy.Windows.Profiles;

namespace Tappy.App.Tests;

public sealed partial class InputLifecycleRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Microsoft_pad_is_complete_on_confirmation_and_migrates_saved_assignments(bool legacyProfile)
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var root = NewRoot();
            var app = System.Windows.Application.Current;
            var originalThemes = app.Resources.MergedDictionaries.ToArray();
            try
            {
                var descriptor = DeviceDescriptorSanitizer.CreateKeyboard((nint)41,
                    @"\\?\HID#{00001124-0000-1000-8000-00805F9B34FB}_VID&0002045E_PID&0764&COL01#TEST#GUID")
                    with
                { DisplayName = "Microsoft Bluetooth Number Pad — Bluetooth" };
                var host = new TestHost();
                var output = new RecordingKeyboard();
                var store = new AtomicProfileStore(root);
                var devices = new KeyboardEnumerator(descriptor);
                void Confirm(DeviceAwareControllerRuntime r)
                {
                    Assert.True(r.BeginIdentification(r.Devices.Single()).Succeeded);
                    host.EmitKey(41, RawKeyboardFlags.Make);
                    host.EmitKey(41, RawKeyboardFlags.Break);
                    Assert.True(r.ConfirmController().Succeeded);
                }
                if (legacyProfile)
                {
                    await using var oldRuntime = new DeviceAwareControllerRuntime(new RawInputKeyboardProvider(
                        new KeyboardEnumerator(descriptor with { PresentationModelId = null }), host, keyboardIsNeutral: () => true), output, store);
                    await oldRuntime.InitializeAsync();
                    Confirm(oldRuntime);
                    host.EmitKey(41, RawKeyboardFlags.Make); host.EmitKey(41, RawKeyboardFlags.Break);
                    Assert.True(oldRuntime.AssignMapping(MicrosoftNumberPadLayout.Key(0x4F), "F24").Succeeded);
                    Assert.True((await oldRuntime.SaveProfileAsync()).Succeeded);
                }

                var runtime = new DeviceAwareControllerRuntime(new RawInputKeyboardProvider(devices, host,
                    keyboardIsNeutral: () => true), output, store);
                await using var vm = new MainViewModel(runtime, action => action(), (_, _) => { });
                await vm.InitializeAsync();
                Confirm(runtime);
                Assert.Equal(17, vm.Controls.Count); // No need to discover all keys by pressing them.
                Assert.Equal(4, vm.LayoutColumns); Assert.Equal(5, vm.LayoutRows);
                Assert.Equal("Num Lock", vm.Controls.First().Label);
                Assert.True(vm.HasControllerPhoto);
                Assert.Equal(17, vm.ControllerPhotoHotspots.Count);
                if (legacyProfile)
                {
                    Assert.Equal("Hold F24 until release", vm.Controls.Single(k => k.Label == "1").Action);
                    Assert.NotNull(runtime.GetControllerAction(MicrosoftNumberPadLayout.Key(0x4F)));
                }
                foreach (var tile in vm.Controls)
                {
                    Assert.DoesNotContain(vm.Controls, other => other != tile && tile.X < other.X + other.TileWidth &&
                        other.X < tile.X + tile.TileWidth && tile.Y < other.Y + other.TileHeight && other.Y < tile.Y + tile.TileHeight);
                    vm.SelectControl(tile);
                    Assert.True(vm.ControllerPhotoHotspots.Single(h => h.Tile == tile).IsSelected);
                }
                foreach (var (scan, extended, vk) in new (ushort, bool, ushort)[]
                         { (0x45, false, 0x90), (0x4C, false, 0x65), (0x1C, true, 0x0D), (0x35, true, 0x6F) })
                {
                    var id = MicrosoftNumberPadLayout.Key(scan, extended);
                    var flags = extended ? RawKeyboardFlags.E0 : RawKeyboardFlags.Make;
                    host.EmitKey(41, flags, scan, vk);
                    Assert.True(vm.Controls.Single(k => k.ControlId == id).IsPressed);
                    Assert.True(vm.ControllerPhotoHotspots.Single(h => h.Tile.ControlId == id).IsIlluminated);
                    host.EmitKey(41, flags | RawKeyboardFlags.Break, scan, vk);
                    Assert.False(vm.Controls.Single(k => k.ControlId == id).IsPressed);
                }
                Assert.Equal(0, output.Down);
                Assert.Equal(0, output.Up);
                Assert.True((await runtime.SaveProfileAsync()).Succeeded);
                var saved = await store.LoadAsync(AtomicProfileStore.DefaultProfileId);
                Assert.Equal(MicrosoftNumberPadLayout.LayoutId, Assert.Single(saved.Controllers).Layout.Id);
                Confirm(runtime);
                Assert.Equal(17, vm.Controls.Count);

                var converter = new ControllerPhotoSourceConverter();
                var photo = Assert.IsType<CroppedBitmap>(converter.Convert(vm.ControllerPhotoSource,
                    typeof(ImageSource), null!, CultureInfo.InvariantCulture));
                Assert.Equal(720, photo.PixelWidth); Assert.Equal(880, photo.PixelHeight);
                var window = new MainWindow(vm, new WindowPlacementStore(root), new SessionMarker(root));
                window.UpdateControllerPhotoPane();
                var content = (FrameworkElement)window.Content;
                foreach (var theme in new[] { "Dark", "Light", "HighContrast" })
                {
                    app.Resources.MergedDictionaries.Clear();
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri($"pack://application:,,,/Tappy;component/Themes/{theme}.xaml") });
                    content.Measure(new Size(1920, 1080)); content.Arrange(new Rect(0, 0, 1920, 1080)); content.UpdateLayout();
                    SaveMicrosoftRender(content, 1920, 1080, $"microsoft-keypad-{theme}.png");
                }
                foreach (var tile in vm.Controls) { tile.IsSelected = true; }
                content.UpdateLayout();
                var locator = (FrameworkElement)((ItemsControl)window.FindName("ControllerPhotoHotspots")).Parent;
                SaveMicrosoftRender(locator, 720, 880, "microsoft-keypad-all-faces.png");

                // An alternate report remains independently assignable but lights the same physical face.
                host.EmitKey(41, RawKeyboardFlags.E0, 0x47, 0x24);
                Assert.Equal(18, vm.Controls.Count);
                Assert.Equal(17, vm.ControllerPhotoHotspots.Count);
                Assert.True(vm.ControllerPhotoHotspots.Single(h => h.Tile.Label == "7").IsIlluminated);
                Assert.True(vm.Controls.Single(k => k.ControlId == MicrosoftNumberPadLayout.Key(0x47, true)).X >= 520);
                host.EmitKey(41, RawKeyboardFlags.E0 | RawKeyboardFlags.Break, 0x47, 0x24);
                devices.Devices = [];
                host.EmitRemoval(41);
                Assert.Empty(vm.Controls);
                Assert.False(vm.HasControllerPhoto);
            }
            finally
            {
                app.Resources.MergedDictionaries.Clear();
                foreach (var theme in originalThemes) { app.Resources.MergedDictionaries.Add(theme); }
                Directory.Delete(root, recursive: true);
            }
        }).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static void SaveMicrosoftRender(Visual content, int width, int height, string filename)
    {
        if (Environment.GetEnvironmentVariable("TAPPY_RENDER_DIR") is not { Length: > 0 } directory) { return; }
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        using var stream = File.Create(Path.Combine(directory, filename));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }
}
