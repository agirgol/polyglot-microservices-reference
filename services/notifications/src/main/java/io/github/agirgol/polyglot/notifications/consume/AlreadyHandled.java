package io.github.agirgol.polyglot.notifications.consume;

import java.time.Duration;
import org.springframework.data.redis.core.StringRedisTemplate;
import org.springframework.stereotype.Component;

/**
 * Remembers what this service has already acted on.
 *
 * <p>Delivery is at-least-once — that is not a defect of the producer, it is
 * what an outbox forwarding to a broker can promise. A message arriving twice
 * is normal traffic, and the consumer is where it stops being a second email.
 *
 * <p>The key is the business fact, not the transport envelope: an order id and
 * what happened to it. Keying on the producer's message id would work only for
 * a redelivery of that exact envelope, and would let a genuinely republished
 * event through as if it were new.
 */
@Component
public class AlreadyHandled {

    /**
     * How long a handled event is remembered.
     *
     * <p>Bounded rather than forever, because this is a cache and not a ledger.
     * The window has to cover the longest plausible gap between a delivery and
     * its retry — a broker outage, a consumer restart, an operator replaying a
     * topic after an incident. A week covers all three with room to spare.
     *
     * <p>A duplicate arriving after the window produces a duplicate
     * notification. That is the accepted cost, and it is acceptable *here*: the
     * consequence is a second email. The same design under a payment would not
     * be, and that difference is the decision, not the TTL.
     */
    private static final Duration REMEMBERED_FOR = Duration.ofDays(7);

    private final StringRedisTemplate redis;

    public AlreadyHandled(StringRedisTemplate redis) {
        this.redis = redis;
    }

    /**
     * Claims an event, returning true only for the caller that got there first.
     *
     * <p>SETNX rather than a read then a write: two consumer instances handed
     * the same message would both find nothing and both proceed.
     */
    public boolean claim(String eventType, Object businessKey) {
        String key = "notifications:handled:%s:%s".formatted(eventType, businessKey);
        Boolean claimed = redis.opsForValue().setIfAbsent(key, "1", REMEMBERED_FOR);
        return Boolean.TRUE.equals(claimed);
    }
}
