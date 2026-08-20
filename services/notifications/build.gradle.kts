plugins {
    java
    alias(libs.plugins.spring.boot)
    alias(libs.plugins.spring.dependency.management)
}

group = "io.github.agirgol.polyglot"
version = "0.1.0-SNAPSHOT"

java {
    toolchain {
        languageVersion = JavaLanguageVersion.of(21)
    }
}

repositories {
    mavenCentral()
}

dependencies {
    implementation("org.springframework.boot:spring-boot-starter-web")
    // The starter, not the bare spring-kafka library. Spring Boot 4 moved each
    // technology's auto-configuration into its own artifact: spring-kafka alone
    // puts the classes on the classpath and nothing registers a listener
    // container, so @KafkaListener is quietly ignored and the service starts
    // clean while consuming nothing.
    implementation("org.springframework.boot:spring-boot-starter-kafka")

    // Redis holds what this service has already acted on. See ADR 0006 — the
    // question is not whether a message arrives twice, it is what happens when
    // it does.
    implementation("org.springframework.boot:spring-boot-starter-data-redis")

    implementation("org.springframework.boot:spring-boot-starter-actuator")

    // Micrometer's tracing bridge rather than the OpenTelemetry Java agent. The
    // agent instruments more for less code, but it is a separate artifact that
    // has to be shipped beside the jar and attached at launch — one more thing
    // to get right in a container, and silent when it is wrong.
    //
    // The starter, not io.micrometer:micrometer-tracing-bridge-otel plus
    // io.opentelemetry:opentelemetry-exporter-otlp. Those resolve, and Boot 4
    // keeps their auto-configuration in a separate artifact, so the tracer is
    // on the classpath and nothing configures an exporter: the service traces
    // itself into a void and reports no error. Same shape as spring-kafka
    // without spring-boot-starter-kafka.
    implementation("org.springframework.boot:spring-boot-starter-opentelemetry")

    testImplementation("org.springframework.boot:spring-boot-starter-test")
    testImplementation("org.springframework.boot:spring-boot-starter-kafka-test")
}

tasks.test {
    useJUnitPlatform()
}
