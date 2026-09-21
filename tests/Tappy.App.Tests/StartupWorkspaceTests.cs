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
    public async Task Startup_with_saved_slots_has_no_phantom_tabs_but_setup_and_typing_protection_remain_accessible()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var root = NewRoot();
            var application = System.Windows.Application.Current;
            var oldThemes = application.Resources.MergedDictionaries.ToArray();
            try
            {
                var settings = Path.Combine(root, "tabs.json");
                await File.WriteAllTextAsync(settings, "2");
                var device = DeviceDescriptorSanitizer.CreateKeyboard((nint)41, @"\\?\HID#VID_046D&PID_C52B#STARTUP");
                var host = new TestHost();
                await using var workspace = new ControllerWorkspace(slot => new DeviceAwareControllerRuntime(
                    new RawInputKeyboardProvider(new KeyboardEnumerator(device), new SharedInputHostLease(host), keyboardIsNeutral: () => true),
                    new RecordingKeyboard(), new AtomicProfileStore(Path.Combine(root, $"slot-{slot}"))),
                    action => action(), (_, _) => { }, settings);
                await workspace.InitializeAsync();
                Assert.Equal(2, workspace.Tabs.Count);
                Assert.Empty(workspace.VisibleTabs);
                application.Resources.MergedDictionaries.Clear();
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri("pack://application:,,,/Tappy;component/Themes/Dark.xaml") });
                var vm = workspace.Tabs[0].ViewModel;
                var window = new MainWindow(vm, new WindowPlacementStore(root), new SessionMarker(root), workspace);
                var tabs = (ListBox)window.FindName("DeviceTabs");
                Assert.Empty(tabs.Items);
                Assert.Equal(-1, tabs.SelectedIndex);
                Assert.True(((FrameworkElement)window.FindName("SetupPanel")).IsEnabled);
                Assert.True(((FrameworkElement)window.FindName("AddDeviceTabButton")).IsEnabled);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("EmptyWorkspacePanel")).Visibility);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1120, 760));
                content.Arrange(new Rect(0, 0, 1120, 760));
                content.UpdateLayout();
                if (Environment.GetEnvironmentVariable("TAPPY_RENDER_DIR") is { Length: > 0 } directory)
                {
                    Directory.CreateDirectory(directory);
                    var bitmap = new RenderTargetBitmap(1120, 760, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(content);
                    using var stream = File.Create(Path.Combine(directory, "startup-no-tabs-Dark.png"));
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    encoder.Save(stream);
                }
                // Identification from the untabbed setup view opens only the requested slot.
                vm.SelectedDevice = vm.Devices.Single();
                vm.BeginIdentification();
                Assert.Single(tabs.Items);
                host.EmitKey(41, RawKeyboardFlags.Make);
                host.EmitKey(41, RawKeyboardFlags.Break);
                vm.ConfirmController();
                Assert.Single(tabs.Items);
                Assert.Same(vm, window.DataContext);
                Assert.Equal(2, workspace.Tabs.Count);
            }
            finally
            {
                application.Resources.MergedDictionaries.Clear();
                foreach (var dictionary in oldThemes) { application.Resources.MergedDictionaries.Add(dictionary); }
                Directory.Delete(root, recursive: true);
            }
        });
    }
}
