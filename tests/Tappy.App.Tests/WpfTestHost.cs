using System.Windows;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Xml.Linq;

namespace Tappy.App.Tests;

// WPF allows only one Application per process, even after Shutdown. All UI tests
// share one serialized STA dispatcher, without running Tappy's native startup.
internal static class WpfTestHost
{
    private static readonly Lazy<Dispatcher> UiDispatcher = new(CreateDispatcher);

    internal static Task RunAsync(Action action) => UiDispatcher.Value.InvokeAsync(action).Task;
    internal static Task RunAsync(Func<Task> action) => UiDispatcher.Value.InvokeAsync(action).Task.Unwrap();

    private static Dispatcher CreateDispatcher()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                // Load the real resource definitions, not App's native startup code.
                XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
                var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AppResources.Source.xaml"));
                var resources = new XElement(source.Root!.Element(presentation + "Application.Resources")!.Elements().Single());
                resources.SetAttributeValue(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
                foreach (var dictionary in resources.Descendants(presentation + "ResourceDictionary"))
                {
                    if (dictionary.Attribute("Source") is { } attribute)
                    { attribute.Value = "pack://application:,,,/Tappy;component/" + attribute.Value; }
                }
                application.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception exception) { ready.TrySetException(exception); }
        })
        { IsBackground = true, Name = "Tappy headless UI tests" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }
}
