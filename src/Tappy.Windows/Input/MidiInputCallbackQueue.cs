using System.Threading.Channels;

namespace Tappy.Windows.Input;

/// <summary>
/// The native WinMM callback may only enqueue. All subscribers run serially on
/// this managed consumer, with an open-session token so closed streams cannot
/// deliver their buffered input to a newly selected device.
/// </summary>
internal sealed class MidiInputCallbackQueue : IDisposable
{
    private readonly Channel<WinMmMidiInputMessage> _messages;
    private readonly Task _consumer;
    private long _generation;
    private int _disposed;
    private QueuedFault? _pendingFault;
    private long _failedGeneration;

    internal MidiInputCallbackQueue(int capacity = 2048)
    {
        _messages = Channel.CreateBounded<WinMmMidiInputMessage>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
        _consumer = Task.Run(ConsumeAsync);
    }

    internal event Action<WinMmMidiInputMessage>? MessageReceived;
    internal event EventHandler<Exception>? Faulted;
    internal long CurrentGeneration => Volatile.Read(ref _generation);
    internal Task Completion => _consumer;

    internal long BeginSession()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return Interlocked.Increment(ref _generation);
    }

    internal void EndSession() => Interlocked.Increment(ref _generation);

    internal void Enqueue(long generation, uint packedMessage)
    {
        if (Volatile.Read(ref _disposed) != 0 || generation != CurrentGeneration ||
            generation == Volatile.Read(ref _failedGeneration))
        {
            return;
        }

        if (!_messages.Writer.TryWrite(new WinMmMidiInputMessage(generation, packedMessage)))
        {
            ReportCallbackFailure(generation,
                new IOException("The MIDI input queue overflowed; capture was stopped to avoid unbalanced input."));
        }
    }

    internal void ReportCallbackFailure(long generation, Exception exception)
    {
        if (Volatile.Read(ref _disposed) != 0 || generation != CurrentGeneration)
        {
            return;
        }

        Volatile.Write(ref _failedGeneration, generation);
        var fault = new QueuedFault(generation, exception);
        while (true)
        {
            var previous = Volatile.Read(ref _pendingFault);
            if (previous?.Generation == generation || generation != CurrentGeneration)
            {
                break;
            }
            if (ReferenceEquals(Interlocked.CompareExchange(ref _pendingFault, fault, previous), previous))
            {
                break;
            }
        }
        // A full channel already has a wake-up pending. For other callback
        // failures, enqueue a wake-up so failure is still delivered off-callback.
        _messages.Writer.TryWrite(new WinMmMidiInputMessage(generation, 0));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            EndSession();
            _messages.Writer.TryComplete();
        }
        // Never join the consumer here: a subscriber can be waiting for the
        // runtime lock held by the thread that closes/disposes the MIDI port.
    }

    private async Task ConsumeAsync()
    {
        await foreach (var message in _messages.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var failure = Interlocked.Exchange(ref _pendingFault, null);
            if (failure is not null && failure.Generation == CurrentGeneration &&
                Volatile.Read(ref _disposed) == 0)
            {
                RaiseFault(failure.Generation, failure.Exception);
            }

            if (Volatile.Read(ref _disposed) != 0 ||
                message.SessionGeneration != CurrentGeneration ||
                message.SessionGeneration == Volatile.Read(ref _failedGeneration))
            {
                continue;
            }

            try
            {
                MessageReceived?.Invoke(message);
            }
            catch (Exception exception)
            {
                if (message.SessionGeneration == CurrentGeneration && Volatile.Read(ref _disposed) == 0)
                {
                    Volatile.Write(ref _failedGeneration, message.SessionGeneration);
                    RaiseFault(message.SessionGeneration, exception);
                }
            }
        }
    }

    private void RaiseFault(long generation, Exception exception)
    {
        try
        {
            Faulted?.Invoke(this, new WinMmMidiInputSessionException(generation, exception));
        }
        catch (Exception notificationFailure)
        {
            System.Diagnostics.Trace.TraceError(
                "Tappy MIDI failure notification failed: {0}; original failure: {1}",
                notificationFailure, exception);
        }
    }

    private sealed record QueuedFault(long Generation, Exception Exception);
}
