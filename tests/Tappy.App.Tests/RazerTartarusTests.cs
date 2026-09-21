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
    [Fact]
    public async Task Tartarus_factory_layout_photo_keymaps_and_persistence_are_isolated()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var root = NewRoot();
            var app = System.Windows.Application.Current;
            var themes = app.Resources.MergedDictionaries.ToArray();
            try
            {
                var descriptor = DeviceDescriptorSanitizer.CreateKeyboard((nint)41,
                    @"\\?\HID#VID_1532&PID_0201&MI_00#TARTARUS#GUID");
                var host = new TestHost();
                var devices = new KeyboardEnumerator(descriptor);
                var store = new AtomicProfileStore(root);
                var output = new RecordingKeyboard();
                var runtime = new DeviceAwareControllerRuntime(new RawInputKeyboardProvider(devices, host,
                    keyboardIsNeutral: () => true), output, store);
                await using var vm = new MainViewModel(runtime, a => a(), (_, _) => { });
                await vm.InitializeAsync();
                void Confirm()
                {
                    Assert.True(runtime.BeginIdentification(runtime.Devices.Single()).Succeeded);
                    host.EmitKey(41, RawKeyboardFlags.Make, 0x0F, 0x09);
                    host.EmitKey(41, RawKeyboardFlags.Break, 0x0F, 0x09);
                    Assert.True(runtime.ConfirmController().Succeeded);
                }
                Assert.False(runtime.SelectTartarusKeymap(1).Succeeded);
                Confirm();
                Assert.Equal(21, vm.Controls.Count);
                Assert.Equal(21, vm.ControllerPhotoHotspots.Count);
                Assert.Equal("01 · Tab", vm.Controls[0].Label);
                Assert.Equal("15 · V", vm.Controls[14].Label);
                Assert.True(vm.HasTartarusKeymaps);
                Assert.False(vm.HasInputBank); // Never show the PAUK10U Num Lock LED on a Razer.
                Assert.Equal(1, vm.SelectedTartarusKeymap);
                Assert.Null(runtime.GetControllerLedColorCapability()); // Original Tartarus is not RGB.
                foreach (var tile in vm.Controls)
                {
                    Assert.DoesNotContain(vm.Controls, other => other != tile && tile.X < other.X + other.TileWidth &&
                        other.X < tile.X + tile.TileWidth && tile.Y < other.Y + other.TileHeight && other.Y < tile.Y + tile.TileHeight);
                    vm.SelectControl(tile);
                    Assert.True(vm.ControllerPhotoHotspots.Single(h => h.Tile == tile).IsSelected);
                }
                var first = RazerTartarusLayout.Key(0x0F);
                Assert.True(runtime.AssignMapping(first, "F24").Succeeded);
                var staleEdit = Assert.IsType<ControllerAssignmentEditContext>(runtime.BeginAssignmentEdit(first));
                await vm.SelectTartarusKeymapAsync(2);
                Assert.Equal("Unassigned", vm.Controls[0].Action);
                Assert.False(runtime.AssignKeyboardMapping(staleEdit.Target, KeyboardMappingAssignment.PressOnce("stale", ["F13"])).Succeeded);
                Assert.True(runtime.AssignMapping(first, "F23").Succeeded);
                await vm.SelectTartarusKeymapAsync(1);
                Assert.Equal("Hold F24 until release", vm.Controls[0].Action);
                Assert.Equal("#FFE45C", vm.TartarusYellowLed);
                Assert.Equal("#30343A", vm.TartarusGreenLed);
                host.EmitKey(41, RawKeyboardFlags.Make, 0x0F, 0x09);
                Assert.True(vm.Controls[0].IsPressed);
                Assert.True(vm.ControllerPhotoHotspots.Single(h => h.Tile == vm.Controls[0]).IsIlluminated);
                Assert.False(runtime.SelectTartarusKeymap(3).Succeeded);
                host.EmitKey(41, RawKeyboardFlags.Break, 0x0F, 0x09);
                // A diagonal lights both component directions without inventing another physical key.
                host.EmitKey(41, RawKeyboardFlags.E0, 0x48, 0x26);
                host.EmitKey(41, RawKeyboardFlags.E0, 0x4B, 0x25);
                Assert.Equal(2, vm.Controls.Count(t => t.IsPressed));
                host.EmitKey(41, RawKeyboardFlags.E0 | RawKeyboardFlags.Break, 0x48, 0x26);
                host.EmitKey(41, RawKeyboardFlags.E0 | RawKeyboardFlags.Break, 0x4B, 0x25);
                Assert.Equal(21, vm.Controls.Count);
                Assert.Equal(0, output.Down);
                Assert.Equal(0, output.Up);
                await vm.SelectTartarusKeymapAsync(8);
                Assert.Equal("#30343A", vm.TartarusYellowLed);
                Assert.Equal("#30343A", vm.TartarusGreenLed);
                Assert.Equal("#30343A", vm.TartarusBlueLed);
                var saved = Assert.Single((await store.LoadAsync(AtomicProfileStore.DefaultProfileId)).Controllers);
                Assert.Equal(8, saved.Layers.Count);
                Assert.Equal(saved.Layers[7].Id, saved.ActiveLayerId);
                Assert.False(runtime.SelectTartarusKeymap(0).Succeeded);
                Assert.False(runtime.SelectTartarusKeymap(9).Succeeded);
                Confirm();
                Assert.Equal(8, vm.SelectedTartarusKeymap);
                await vm.SelectTartarusKeymapAsync(2);
                Assert.Equal("Hold F23 until release", vm.Controls[0].Action);
                var photo = Assert.IsType<CroppedBitmap>(new ControllerPhotoSourceConverter().Convert(vm.ControllerPhotoSource,
                    typeof(ImageSource), null!, CultureInfo.InvariantCulture));
                Assert.Equal(740, photo.PixelWidth); Assert.Equal(880, photo.PixelHeight);
                var window = new MainWindow(vm, new WindowPlacementStore(root), new SessionMarker(root));
                window.UpdateControllerPhotoPane();
                var content = (FrameworkElement)window.Content;
                foreach (var theme in new[] { "Dark", "Light", "HighContrast" })
                {
                    app.Resources.MergedDictionaries.Clear();
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri($"pack://application:,,,/Tappy;component/Themes/{theme}.xaml") });
                    content.Measure(new Size(1920, 1080)); content.Arrange(new Rect(0, 0, 1920, 1080)); content.UpdateLayout();
                    SaveMicrosoftRender(content, 1920, 1080, $"tartarus-{theme}.png");
                }
                foreach (var tile in vm.Controls) { tile.IsSelected = true; }
                content.UpdateLayout();
                SaveMicrosoftRender((FrameworkElement)((ItemsControl)window.FindName("ControllerPhotoHotspots")).Parent,
                    740, 880, "tartarus-all-faces.png");
                devices.Devices = [];
                host.EmitRemoval(41);
                Assert.Empty(vm.Controls);
                Assert.False(vm.HasControllerPhoto);
                Assert.False(vm.HasTartarusKeymaps);
                Assert.False(runtime.SelectTartarusKeymap(2).Succeeded);
            }
            finally
            {
                app.Resources.MergedDictionaries.Clear();
                foreach (var theme in themes) { app.Resources.MergedDictionaries.Add(theme); }
                Directory.Delete(root, recursive: true);
            }
        }).WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Tartarus_migration_preserves_saved_data_but_fixes_legacy_discovery_order()
    {
        var layout = RazerTartarusLayout.Create();
        var first = layout.Rows[0].Controls[0];
        first.X = 123; first.Y = 456; first.Width = 1.7; first.ColorKey = "Green";
        var restored = RazerTartarusLayout.MergeSavedLayout(layout).Rows[0].Controls[0];
        Assert.Equal(123, restored.X); Assert.Equal(456, restored.Y); Assert.Equal(1.7, restored.Width);
        layout.Id = "generated-grid";
        restored = RazerTartarusLayout.MergeSavedLayout(layout).Rows[0].Controls[0];
        Assert.Equal(0, restored.X); Assert.Equal(0, restored.Y); Assert.Equal("Green", restored.ColorKey);
        var masks = Enumerable.Range(1, 8).Select(RazerTartarusLayout.IndicatorMask).ToArray();
        Assert.Equal(8, masks.Distinct().Count());
        Assert.All(masks, mask => Assert.InRange(mask, 0, 7));
        Assert.Null(ControllerPhotoCatalog.Find("raw-input", 0x1532, 0x0208, "Razer Tartarus Chroma"));
        Assert.Null(ControllerPhotoCatalog.Find("raw-input", 0x1532, 0x022B, "Razer Tartarus V2"));
        Assert.Null(ControllerPhotoCatalog.Find("raw-input", 0x046D, 0xC52B, "Razer Tartarus"));
    }
}
