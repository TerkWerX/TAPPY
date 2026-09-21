using Tappy.Windows.Input;

namespace Tappy.Windows.Tests;

public sealed class MidiInputCallbackQueueTests
{
    [Fact]
    public async Task Callback_enqueue_returns_while_managed_subscriber_is_busy_and_keeps_order()
    {
        using var queue = new MidiInputCallbackQueue();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new List<uint>();
        queue.MessageReceived += message =>
        {
            received.Add(message.PackedMessage);
            if (message.PackedMessage == 1)
            {
                entered.TrySetResult();
                release.Wait();
            }
            if (message.PackedMessage == 3)
            {
                finished.TrySetResult();
            }
        };
        try
        {
            var generation = queue.BeginSession();
            await Task.Run(() => queue.Enqueue(generation, 1)).WaitAsync(TimeSpan.FromSeconds(5));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queue.Enqueue(generation, 2);
            queue.Enqueue(generation, 3);
            release.Set();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new uint[] { 1, 2, 3 }, received);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task Closed_stream_messages_are_discarded_when_the_same_port_is_reopened()
    {
        using var queue = new MidiInputCallbackQueue();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var received = new List<uint>();
        queue.MessageReceived += message =>
        {
            received.Add(message.PackedMessage);
            if (message.PackedMessage == 1)
            {
                entered.TrySetResult();
                release.Wait();
            }
            if (message.PackedMessage == 3)
            {
                finished.TrySetResult();
            }
        };
        try
        {
            var oldGeneration = queue.BeginSession();
            queue.Enqueue(oldGeneration, 1);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queue.Enqueue(oldGeneration, 2);
            queue.EndSession();
            var newGeneration = queue.BeginSession();
            queue.Enqueue(oldGeneration, 4);
            queue.Enqueue(newGeneration, 3);
            release.Set();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new uint[] { 1, 3 }, received);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task Dispose_does_not_join_a_subscriber_waiting_for_the_callers_lock()
    {
        using var queue = new MidiInputCallbackQueue();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        queue.MessageReceived += _ =>
        {
            count++;
            entered.TrySetResult();
            release.Wait();
        };
        try
        {
            var generation = queue.BeginSession();
            queue.Enqueue(generation, 1);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queue.Enqueue(generation, 2);
            await Task.Run(queue.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
            release.Set();
            await queue.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, count);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task Queue_overflow_reports_one_managed_fault_and_stops_the_affected_stream()
    {
        using var queue = new MidiInputCallbackQueue(capacity: 2);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var faulted = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        queue.MessageReceived += _ =>
        {
            count++;
            entered.TrySetResult();
            release.Wait();
        };
        queue.Faulted += (_, exception) => faulted.TrySetResult(exception);
        try
        {
            var generation = queue.BeginSession();
            queue.Enqueue(generation, 1);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queue.Enqueue(generation, 2);
            queue.Enqueue(generation, 3);
            queue.Enqueue(generation, 4);
            queue.Enqueue(generation, 5);
            release.Set();
            var failure = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("overflow", failure.Message, StringComparison.OrdinalIgnoreCase);
            queue.Dispose();
            await queue.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, count);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task Subscriber_failure_is_reported_instead_of_silently_losing_input()
    {
        using var queue = new MidiInputCallbackQueue();
        var faulted = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new InvalidOperationException("subscriber failure");
        queue.MessageReceived += _ => throw expected;
        queue.Faulted += (_, exception) => faulted.TrySetResult(exception);
        queue.Enqueue(queue.BeginSession(), 1);
        var failure = await faulted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(expected, Assert.IsType<WinMmMidiInputSessionException>(failure).InnerException);
    }

    [Fact]
    public async Task Failure_from_an_old_blocked_subscriber_does_not_disarm_a_reopened_stream()
    {
        using var queue = new MidiInputCallbackQueue();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedNew = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var faults = 0;
        queue.Faulted += (_, _) => faults++;
        queue.MessageReceived += message =>
        {
            if (message.PackedMessage == 1)
            {
                entered.TrySetResult();
                release.Wait();
                throw new InvalidOperationException("The already-closed stream failed.");
            }
            receivedNew.TrySetResult();
        };
        try
        {
            queue.Enqueue(queue.BeginSession(), 1);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            queue.EndSession();
            queue.Enqueue(queue.BeginSession(), 2);
            release.Set();
            await receivedNew.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, faults);
        }
        finally
        {
            release.Set();
        }
    }
}
