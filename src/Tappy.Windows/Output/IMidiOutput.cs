using Tappy.Core.Output;

namespace Tappy.Windows.Output;

public interface IMidiOutput : IDisposable
{
    void Send(string? preferredDeviceName, MidiShortMessage message);
    void Reset();
}
