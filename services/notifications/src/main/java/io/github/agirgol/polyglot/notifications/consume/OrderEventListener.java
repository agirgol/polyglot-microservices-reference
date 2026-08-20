package io.github.agirgol.polyglot.notifications.consume;

import tools.jackson.databind.ObjectMapper;
import io.github.agirgol.polyglot.notifications.events.OrderEvents;
import io.micrometer.core.instrument.Counter;
import io.micrometer.core.instrument.MeterRegistry;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.kafka.annotation.KafkaListener;
import org.springframework.stereotype.Component;

/**
 * Turns order events into notifications.
 *
 * <p>The payload is taken as a {@link String} and read with Jackson rather than
 * through Spring's typed deserialiser. The producer is not a Spring
 * application: it writes no {@code __TypeId__} header, so the machinery that
 * would normally choose a type has nothing to work from. Naming the type at the
 * point of use is shorter than configuring a container factory per topic, and
 * it puts the contract where a reader looking for it will be.
 */
@Component
public class OrderEventListener {

    private static final Logger log = LoggerFactory.getLogger(OrderEventListener.class);

    private final ObjectMapper json;
    private final AlreadyHandled alreadyHandled;
    private final Notifications notifications;
    private final Counter handled;
    private final Counter suppressed;

    public OrderEventListener(
            ObjectMapper json,
            AlreadyHandled alreadyHandled,
            Notifications notifications,
            MeterRegistry metrics) {
        this.json = json;
        this.alreadyHandled = alreadyHandled;
        this.notifications = notifications;
        this.handled = Counter.builder("notifications.handled")
                .description("Order events that produced a notification")
                .register(metrics);
        this.suppressed = Counter.builder("notifications.suppressed")
                .description("Order events already handled, so not notified again")
                .register(metrics);
    }

    @KafkaListener(topics = "orders.placed", groupId = "notifications")
    public void onOrderPlaced(String payload) {
        var event = json.readValue(payload, OrderEvents.OrderPlaced.class);
        notify("order-placed", event.orderId(), event.customerId(),
                "Order %s placed for %s %s across %d line(s)".formatted(
                        event.orderId(), event.total(), event.currency(), event.lineCount()));
    }

    @KafkaListener(topics = "orders.confirmed", groupId = "notifications")
    public void onOrderConfirmed(String payload) {
        var event = json.readValue(payload, OrderEvents.OrderConfirmed.class);
        notify("order-confirmed", event.orderId(), event.customerId(),
                "Order %s confirmed".formatted(event.orderId()));
    }

    @KafkaListener(topics = "orders.cancelled", groupId = "notifications")
    public void onOrderCancelled(String payload) {
        var event = json.readValue(payload, OrderEvents.OrderCancelled.class);
        notify("order-cancelled", event.orderId(), event.customerId(),
                "Order %s cancelled: %s".formatted(event.orderId(), event.reason()));
    }

    private void notify(String kind, Object businessKey, String customerId, String detail) {
        if (!alreadyHandled.claim(kind, businessKey)) {
            suppressed.increment();
            log.info("Already notified {} for {}; not sending again", kind, businessKey);
            return;
        }

        notifications.record(kind, customerId, detail);
        handled.increment();
        log.info("Notifying {}: {}", customerId, detail);
    }
}
