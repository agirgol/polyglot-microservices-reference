package io.github.agirgol.polyglot.notifications.events;

import java.math.BigDecimal;
import java.time.OffsetDateTime;
import java.util.UUID;

/**
 * The events this service consumes, as they appear on Kafka.
 *
 * <p>These are hand-written from the contract rather than generated from the
 * producer's types, and that is the point of the exercise: the producer is a
 * .NET service, there is no shared library, and the only thing holding the two
 * together is the JSON on the topic. If a field is renamed on the other side,
 * nothing here fails to compile — it fails at runtime, which is why the shape
 * is asserted by a test rather than trusted.
 *
 * <p>{@code total} is a {@link BigDecimal}. The wire carries {@code 59.7000}
 * and a {@code double} would not hold it exactly. Money does not survive binary
 * floating point on either side of a boundary.
 */
public final class OrderEvents {

    private OrderEvents() {
    }

    public record OrderPlaced(
            UUID orderId,
            String customerId,
            String currency,
            BigDecimal total,
            int lineCount,
            OffsetDateTime placedAt) {
    }

    public record OrderConfirmed(
            UUID orderId,
            String customerId,
            OffsetDateTime confirmedAt) {
    }

    public record OrderCancelled(
            UUID orderId,
            String customerId,
            String reason,
            OffsetDateTime cancelledAt) {
    }
}
