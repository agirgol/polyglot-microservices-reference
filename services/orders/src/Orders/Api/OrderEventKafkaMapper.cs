using System.Text;
using Confluent.Kafka;
using Wolverine;
using Wolverine.Kafka;

namespace Orders.Api;

/// <summary>
/// Decides what an order event looks like on Kafka.
/// </summary>
/// <remarks>
/// <para>
/// The default mapping is Wolverine talking to Wolverine, and it works: the body
/// is plain JSON and the metadata rides in headers. One of those headers is the
/// problem. Wolverine writes the trace context to <c>parent-id</c> — in W3C
/// <c>traceparent</c> format, under a name that is not <c>traceparent</c>.
/// OpenTelemetry's Java instrumentation reads the standard name and finds
/// nothing, so the consumer starts a new trace and the request appears to end at
/// the topic.
/// </para>
/// <para>
/// The fix belongs on this side. W3C Trace Context exists so that a consumer
/// does not need to know what produced a message; teaching the Java service
/// about a .NET framework's header naming would work once and be wrong for the
/// next consumer. So the producer emits the standard name.
/// </para>
/// <para>
/// Everything on the wire is written here rather than inherited, which is the
/// other half of the point: this is a cross-language contract, and a contract
/// nobody wrote down is whatever the framework happened to do that release.
/// </para>
/// </remarks>
internal sealed class OrderEventKafkaMapper : IKafkaEnvelopeMapper
{
    public void MapEnvelopeToOutgoing(Envelope envelope, Message<string, byte[]> outgoing)
    {
        outgoing.Value = envelope.Data ?? [];
        outgoing.Headers ??= [];

        outgoing.Headers.Add("content-type", Bytes("application/json"));

        // order.placed, not Orders.Domain.OrderPlaced. See MessageIdentity on
        // the event records.
        if (envelope.MessageType is { Length: > 0 } eventType)
        {
            outgoing.Headers.Add("event-type", Bytes(eventType));
        }

        // The whole reason this class exists.
        if (envelope.ParentId is { Length: > 0 } traceParent)
        {
            outgoing.Headers.Add("traceparent", Bytes(traceParent));
        }
    }

    public void MapIncomingToEnvelope(Envelope envelope, Message<string, byte[]> incoming)
    {
        // Nothing in this system consumes these topics from .NET; the Java
        // service does. Implemented because the interface requires both
        // directions, and kept to what would actually be needed.
        envelope.Data = incoming.Value;
        envelope.ContentType = "application/json";

        if (incoming.Headers is null)
        {
            return;
        }

        if (incoming.Headers.TryGetLastBytes("event-type", out var eventType))
        {
            envelope.MessageType = Encoding.UTF8.GetString(eventType);
        }

        if (incoming.Headers.TryGetLastBytes("traceparent", out var traceParent))
        {
            envelope.ParentId = Encoding.UTF8.GetString(traceParent);
        }
    }

    private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);
}
