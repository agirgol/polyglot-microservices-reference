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
    runtimeOnly("io.micrometer:micrometer-registry-prometheus")

    testImplementation("org.springframework.boot:spring-boot-starter-test")
    testImplementation("org.springframework.boot:spring-boot-starter-kafka-test")
}

tasks.test {
    useJUnitPlatform()
}
