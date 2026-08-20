# Built from this directory, which is its own Gradle build:
#     docker build services/notifications
#
# The wrapper and the dependency declarations come first so that a source change
# does not re-resolve every dependency. `--no-daemon` because a build container
# is discarded the moment it finishes; a daemon has nothing to speed up.

FROM eclipse-temurin:21-jdk-alpine AS build
WORKDIR /src

COPY gradlew settings.gradle.kts build.gradle.kts ./
COPY gradle/ gradle/
RUN ./gradlew dependencies --no-daemon --quiet || true

COPY src/ src/
RUN ./gradlew bootJar --no-daemon --quiet

FROM eclipse-temurin:21-jre-alpine AS runtime
WORKDIR /app

RUN adduser --disabled-password --no-create-home --uid 64198 notifications
USER notifications

COPY --from=build /src/build/libs/*.jar app.jar

EXPOSE 8080
ENTRYPOINT ["java", "-jar", "app.jar"]
