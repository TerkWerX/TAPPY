using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using Tappy.App.Services;
using Tappy.App.ViewModels;

namespace Tappy.App.Tests;

public sealed partial class InputLifecycleRegressionTests
{
    [Fact]
    public async Task Device_tabs_and_targus_photo_render_in_all_themes_without_opening_native_capture()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var rootPath = NewRoot();
            var application = System.Windows.Application.Current;
            var originalThemes = application.Resources.MergedDictionaries.ToArray();
            try
            {
                var descriptor = Tappy.Windows.Input.DeviceDescriptorSanitizer.CreateKeyboard((nint)41,
                    @"\\?\HID#VID_05A4&PID_9862#VISUAL_TEST");
                var host = new TestHost();
                var otherDescriptor = Tappy.Windows.Input.DeviceDescriptorSanitizer.CreateKeyboard((nint)42,
                    @"\\?\HID#VID_05A4&PID_9862#SECOND_VISUAL_TEST");
                // Exercise saved v3 scattered positions, not a clean first-run
                // layout: the previous upgrade preserved these below the keypad.
                var legacyController = Tappy.Core.Models.ControllerProfile.Create(descriptor.ToCoreIdentity());
                legacyController.Layout = TargusKeypadLayout.Create();
                legacyController.Layout.Id = "targus-pauk10u-layout-v3";
                foreach (var key in legacyController.Layout.Rows.SelectMany(row => row.Controls)
                             .Where(key => key.Cluster == "Navigation reports"))
                { key.X = 144; key.Y = 1000; }
                await new Tappy.Windows.Profiles.AtomicProfileStore(Path.Combine(rootPath, "slot-1"))
                    .SaveAsync(new Tappy.Core.Models.TappyProfile { Controllers = [legacyController] }.CreateSnapshot());
                var devices = new KeyboardEnumerator(descriptor, otherDescriptor);
                var workspace = new ControllerWorkspace(slot => new Tappy.App.Runtime.DeviceAwareControllerRuntime(
                    new Tappy.Windows.Input.RawInputKeyboardProvider(devices,
                        new Tappy.App.Runtime.SharedInputHostLease(host), keyboardIsNeutral: () => true),
                    new RecordingKeyboard(), new Tappy.Windows.Profiles.AtomicProfileStore(Path.Combine(rootPath, $"slot-{slot}"))),
                    action => action(), (_, _) => { });
                await workspace.InitializeAsync();
                var vm = workspace.Tabs[0].ViewModel;
                Assert.All(vm.Devices, device => Assert.StartsWith("Targus PAUK10U number pad · ID ", device.DisplayName));
                Assert.Equal(2, vm.Devices.Select(device => device.DisplayName).Distinct().Count());
                var namesBeforeRefresh = vm.Devices.ToDictionary(device => device.PersistentId, device => device.DisplayName);
                vm.RefreshDevices();
                Assert.All(vm.Devices, device => Assert.Equal(namesBeforeRefresh[device.PersistentId], device.DisplayName));
                vm.SelectedDevice = vm.Devices.Single(device => device.SessionId == descriptor.SessionId);
                vm.BeginIdentification();
                host.EmitKey(41, Tappy.Windows.Input.RawKeyboardFlags.Make);
                host.EmitKey(41, Tappy.Windows.Input.RawKeyboardFlags.Break);
                vm.ConfirmController();
                foreach (var first in vm.Controls)
                {
                    Assert.DoesNotContain(vm.Controls, other => other != first &&
                        first.X < other.X + other.TileWidth && other.X < first.X + first.TileWidth &&
                        first.Y < other.Y + other.TileHeight && other.Y < first.Y + first.TileHeight);
                }
                var second = (await workspace.AddAsync()).ViewModel;
                second.SelectedDevice = second.Devices.Single(device => device.SessionId == otherDescriptor.SessionId);
                second.BeginIdentification();
                host.EmitKey(42, Tappy.Windows.Input.RawKeyboardFlags.Make);
                host.EmitKey(42, Tappy.Windows.Input.RawKeyboardFlags.Break);
                second.ConfirmController();
                var converter = new ControllerPhotoSourceConverter();
                var photo = Assert.IsType<CroppedBitmap>(converter.Convert(vm.ControllerPhotoSource,
                    typeof(ImageSource), null!, CultureInfo.InvariantCulture));
                Assert.Equal(670, photo.PixelWidth);
                Assert.Equal(880, photo.PixelHeight);

                var window = new MainWindow(vm, new WindowPlacementStore(rootPath), new SessionMarker(rootPath), workspace);
                window.UpdateControllerPhotoPane();
                var content = (FrameworkElement)window.Content;
                foreach (var theme in new[] { "Dark", "Light", "HighContrast" })
                {
                    application.Resources.MergedDictionaries.Clear();
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri($"pack://application:,,,/Tappy;component/Themes/{theme}.xaml") });
                    content.Measure(new Size(1920, 1080));
                    content.Arrange(new Rect(0, 0, 1920, 1080));
                    content.UpdateLayout();
                    var led = (System.Windows.Shapes.Ellipse)window.FindName("TargusNumLockLed");
                    Assert.Equal(Color.FromRgb(72, 255, 99), ((SolidColorBrush)led.Fill).Color);
                    Assert.Contains("numeric", vm.InputBankLabel);
                    var tabs = (ListBox)window.FindName("DeviceTabs");
                    Assert.Equal(2, tabs.Items.Count);
                    Assert.True(tabs.ActualHeight > 20);
                    var item = (ListBoxItem)tabs.ItemContainerGenerator.ContainerFromIndex(0);
                    Assert.NotEqual(((SolidColorBrush)item.Foreground).Color, ((SolidColorBrush)item.Background).Color);
                    var setup = (FrameworkElement)window.FindName("SetupPanel");
                    Assert.True(setup.TranslatePoint(new Point(0, 0), content).Y > tabs.TranslatePoint(new Point(0, tabs.ActualHeight), content).Y);
                    var bitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    if (Environment.GetEnvironmentVariable("TAPPY_RENDER_DIR") is { Length: > 0 } directory)
                    {
                        Directory.CreateDirectory(directory);
                        using var stream = File.Create(Path.Combine(directory, $"device-tabs-{theme}.png"));
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        encoder.Save(stream);
                    }
                    var typingPanel = (Expander)window.FindName("TypingKeyboardProtectionPanel");
                    typingPanel.IsExpanded = true;
                    content.Measure(new Size(1120, 760));
                    content.Arrange(new Rect(0, 0, 1120, 760));
                    content.UpdateLayout();
                    Assert.True(typingPanel.ActualHeight > 100);
                    Assert.True(setup.Parent is ScrollViewer);
                    var setupScroller = (ScrollViewer)setup.Parent;
                    setupScroller.ScrollToVerticalOffset(typingPanel.TranslatePoint(new Point(0, 0), setup).Y - 12);
                    content.UpdateLayout();
                    if (Environment.GetEnvironmentVariable("TAPPY_RENDER_DIR") is { Length: > 0 } typingDirectory)
                    {
                        var typingBitmap = new RenderTargetBitmap(1120, 760, 96, 96, PixelFormats.Pbgra32);
                        typingBitmap.Render(content);
                        using var typingStream = File.Create(Path.Combine(typingDirectory, $"typing-protection-{theme}.png"));
                        var typingEncoder = new PngBitmapEncoder();
                        typingEncoder.Frames.Add(BitmapFrame.Create(typingBitmap));
                        typingEncoder.Save(typingStream);
                    }
                    typingPanel.IsExpanded = false;
                    setupScroller.ScrollToHome();
                    content.Measure(new Size(1920, 1080));
                    content.Arrange(new Rect(0, 0, 1920, 1080));
                    content.UpdateLayout();
                    host.EmitKey(41, Tappy.Windows.Input.RawKeyboardFlags.E0, 0x47, 0x24);
                    host.EmitKey(41, Tappy.Windows.Input.RawKeyboardFlags.E0 | Tappy.Windows.Input.RawKeyboardFlags.Break, 0x47, 0x24);
                    content.UpdateLayout();
                    Assert.Equal(Color.FromRgb(18, 44, 25), ((SolidColorBrush)led.Fill).Color);
                    Assert.Contains("navigation", vm.InputBankLabel);
                    Assert.Equal(29, vm.Controls.Count);
                    if (Environment.GetEnvironmentVariable("TAPPY_RENDER_DIR") is { Length: > 0 } offDirectory)
                    {
                        var offBitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
                        offBitmap.Render(content);
                        using var offStream = File.Create(Path.Combine(offDirectory, $"targus-bank2-{theme}.png"));
                        var offEncoder = new PngBitmapEncoder();
                        offEncoder.Frames.Add(BitmapFrame.Create(offBitmap));
                        offEncoder.Save(offStream);
                    }
                    host.EmitKey(41, Tappy.Windows.Input.RawKeyboardFlags.Make, 0x47, 0x67);
                    host.EmitKey(41, Tappy.Windows.Input.RawKeyboardFlags.Break, 0x47, 0x67);
                }
                var selector = (ListBox)window.FindName("DeviceTabs");
                if (Environment.GetEnvironmentVariable("TAPPY_RENDER_DIR") is { Length: > 0 } alignmentDirectory)
                {
                    // Illuminate every face together so overlaps and wrong geometry
                    // cannot hide behind testing one selected key at a time.
                    foreach (var tile in vm.Controls) { tile.IsSelected = true; }
                    content.UpdateLayout();
                    var locator = (FrameworkElement)((ItemsControl)window.FindName("ControllerPhotoHotspots")).Parent;
                    var alignment = new RenderTargetBitmap(670, 880, 96, 96, PixelFormats.Pbgra32);
                    alignment.Render(locator);
                    using var alignmentStream = File.Create(Path.Combine(alignmentDirectory, "targus-all-key-faces.png"));
                    var alignmentEncoder = new PngBitmapEncoder();
                    alignmentEncoder.Frames.Add(BitmapFrame.Create(alignment));
                    alignmentEncoder.Save(alignmentStream);
                }
                selector.SelectedIndex = 1;
                Assert.Same(second, window.DataContext);
                Assert.False(selector.IsTextSearchEnabled);
                var selectedTab = (ListBoxItem)selector.ItemContainerGenerator.ContainerFromIndex(1);
                var keySource = new TabTestPresentationSource();
                foreach (var key in new[] { Key.Back, Key.Home, Key.Left, Key.End, Key.Right,
                             Key.Up, Key.Down, Key.PageUp, Key.PageDown, Key.Divide, Key.Multiply,
                             Key.Enter, Key.Space, Key.NumPad4, Key.NumPad7, Key.NumLock })
                {
                    var down = new KeyEventArgs(Keyboard.PrimaryDevice, keySource, 0, key)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    selectedTab.RaiseEvent(down);
                    Assert.True(down.Handled, key.ToString());
                    down.RoutedEvent = Keyboard.KeyDownEvent;
                    selectedTab.RaiseEvent(down);
                    Assert.Equal(1, selector.SelectedIndex);
                    Assert.Same(second, window.DataContext);
                    var up = new KeyEventArgs(Keyboard.PrimaryDevice, keySource, 0, key)
                    { RoutedEvent = Keyboard.PreviewKeyUpEvent };
                    selectedTab.RaiseEvent(up);
                    Assert.True(up.Handled);
                }
                var text = new TextCompositionEventArgs(Keyboard.PrimaryDevice,
                    new TextComposition(InputManager.Current, selectedTab, "Device 1"))
                { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
                selectedTab.RaiseEvent(text);
                Assert.True(text.Handled);
                // Raw Input still reaches the second device; no Num Lock reset is needed.
                foreach (var scan in new ushort[] { 0x47, 0x4B, 0x0E })
                {
                    var flags = scan == 0x0E ? Tappy.Windows.Input.RawKeyboardFlags.Make : Tappy.Windows.Input.RawKeyboardFlags.E0;
                    host.EmitKey(42, flags, scan, 0);
                    var id = TargusKeypadLayout.Key(scan, scan != 0x0E);
                    Assert.True(second.Controls.Single(c => c.ControlId == id).IsPressed);
                    Assert.False(vm.Controls.Single(c => c.ControlId == id).IsPressed);
                    Assert.Equal(1, selector.SelectedIndex);
                    host.EmitKey(42, flags | Tappy.Windows.Input.RawKeyboardFlags.Break, scan, 0);
                    Assert.False(second.Controls.Single(c => c.ControlId == id).IsPressed);
                }
                selector.SelectedIndex = 0;
                Assert.Same(vm, window.DataContext);
                devices.Devices = [otherDescriptor];
                host.EmitRemoval(41);
                Assert.Single(selector.Items);
                Assert.Same(second, window.DataContext);
                devices.Devices = [];
                host.EmitRemoval(42);
                Assert.Empty(selector.Items);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("EmptyWorkspacePanel")).Visibility);
                Assert.False(((FrameworkElement)window.FindName("SetupPanel")).IsEnabled);
                Assert.True(((FrameworkElement)window.FindName("AddDeviceTabButton")).IsEnabled);
                if (Environment.GetEnvironmentVariable("TAPPY_RENDER_DIR") is { Length: > 0 } emptyDirectory)
                {
                    application.Resources.MergedDictionaries.Clear();
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri("pack://application:,,,/Tappy;component/Themes/Dark.xaml") });
                    content.UpdateLayout();
                    var emptyBitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
                    emptyBitmap.Render(content);
                    using var stream = File.Create(Path.Combine(emptyDirectory, "disconnected-tabs-Dark.png"));
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(emptyBitmap));
                    encoder.Save(stream);
                }
                devices.Devices = [otherDescriptor];
                workspace.Poll();
                Assert.Single(selector.Items);
                Assert.Same(second, window.DataContext);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("EmptyWorkspacePanel")).Visibility);
                devices.Devices = [descriptor, otherDescriptor];
                workspace.Poll();
                Assert.Equal(2, selector.Items.Count);
                Assert.Same(second, window.DataContext); // Returning tab must not steal selection.
                Assert.Equal(1, selector.SelectedIndex);
                await workspace.DisposeAsync();
            }
            finally
            {
                application.Resources.MergedDictionaries.Clear();
                foreach (var theme in originalThemes) { application.Resources.MergedDictionaries.Add(theme); }
                Directory.Delete(rootPath, recursive: true);
            }
        }).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class TabTestPresentationSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
