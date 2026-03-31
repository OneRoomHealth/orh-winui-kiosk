using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace FireflyCapture.Bridge;

/// <summary>
/// Distributes button-press events to all currently connected SSE clients.
/// Each client gets its own unbounded channel; the broadcaster writes to all of them.
/// </summary>
public sealed class ButtonEventBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<ButtonPressEvent>> _subscribers = new();

    /// <summary>
    /// Subscribe to future button-press events.
    /// Returns an async-enumerable that yields events until cancellation.
    /// The subscriber channel is automatically removed on cancellation or disposal.
    /// </summary>
    public async IAsyncEnumerable<ButtonPressEvent> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<ButtonPressEvent>(
            new UnboundedChannelOptions { SingleReader = true });

        _subscribers[id] = channel;

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
                yield return evt;
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Broadcast a button-press event to all currently subscribed SSE clients.
    /// </summary>
    public void Broadcast(ButtonPressEvent evt)
    {
        foreach (var (_, ch) in _subscribers)
            ch.Writer.TryWrite(evt);
    }

    /// <summary>Number of active SSE subscribers.</summary>
    public int SubscriberCount => _subscribers.Count;
}

/// <summary>
/// Payload sent over SSE when the Firefly snap button is pressed.
/// </summary>
/// <param name="Timestamp">UTC time of the press detection.</param>
/// <param name="SequenceNumber">Monotonically increasing counter across the process lifetime.</param>
public record ButtonPressEvent(DateTime Timestamp, long SequenceNumber);
