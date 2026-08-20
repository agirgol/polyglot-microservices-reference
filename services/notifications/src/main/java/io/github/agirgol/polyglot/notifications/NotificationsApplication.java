package io.github.agirgol.polyglot.notifications;

import io.github.agirgol.polyglot.notifications.consume.Notifications;
import java.util.List;
import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RestController;

/**
 * The Java half of the estate.
 *
 * <p>It is here to prove two things that only a second runtime can prove: that
 * the Kafka contract is language-neutral, and that a trace survives crossing
 * from one to the other. Neither is demonstrable with a second .NET service.
 */
@SpringBootApplication
public class NotificationsApplication {

    public static void main(String[] args) {
        SpringApplication.run(NotificationsApplication.class, args);
    }
}

/** A window on what was notified, so the boundary can be checked from outside. */
@RestController
class NotificationsEndpoint {

    private final Notifications notifications;

    NotificationsEndpoint(Notifications notifications) {
        this.notifications = notifications;
    }

    @GetMapping("/notifications")
    List<Notifications.Notification> recent() {
        return notifications.recent();
    }
}
