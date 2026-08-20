package io.github.agirgol.polyglot.notifications.consume;

import java.time.Instant;
import java.util.ArrayDeque;
import java.util.Deque;
import java.util.List;
import org.springframework.stereotype.Component;

/**
 * What this service would have sent, kept where a test or a curl can see it.
 *
 * <p>It sends nothing. There is no mail server in this architecture and adding
 * one would demonstrate nothing about the boundary being tested — the question
 * is whether an order placed in .NET reaches a consumer in Java with its
 * numbers and its trace intact, not whether SMTP works.
 *
 * <p>In memory and bounded, so it cannot grow without limit. It is a window on
 * the last few, not a record: restart the service and it is empty, which is
 * correct for something that is not the system of record.
 */
@Component
public class Notifications {

    private static final int KEPT = 50;

    private final Deque<Notification> recent = new ArrayDeque<>();

    public record Notification(Instant at, String kind, String customerId, String detail) {
    }

    public synchronized void record(String kind, String customerId, String detail) {
        recent.addFirst(new Notification(Instant.now(), kind, customerId, detail));
        while (recent.size() > KEPT) {
            recent.removeLast();
        }
    }

    public synchronized List<Notification> recent() {
        return List.copyOf(recent);
    }
}
