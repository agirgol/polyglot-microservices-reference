#!/usr/bin/env bash
#
# Demonstrates that an order and its event survive the broker being down.
#
# Stops Kafka, places an order, shows the event waiting in the outbox table,
# starts Kafka, and shows the row clear as the message is delivered. The claim
# in ADR 0002 is this script; if it stops passing, the ADR is wrong.
#
# Expects `docker compose up -d`. The orders service is on 8081 there; override
# ORDERS_URL if you are running it from a terminal instead.
set -euo pipefail

ORDERS="${ORDERS_URL:-http://localhost:8081}"
COMPOSE="docker compose"
PSQL="$COMPOSE exec -T postgres psql -U orders -d orders -t -A"

outbox_count() {
    $PSQL -c "select count(*) from wolverine.wolverine_outgoing_envelopes;" | tr -d ' \r'
}

echo "==> stopping Kafka"
$COMPOSE stop kafka >/dev/null

echo "==> placing an order with the broker down"
response=$(curl -sS -X POST "$ORDERS/orders" \
    -H 'Content-Type: application/json' \
    -d '{"customerId":"outbox-proof","currency":"TRY","lines":[{"sku":"PROOF","quantity":1,"unitPrice":42.00}]}')
echo "    $response"

order_id=$(printf '%s' "$response" | sed -E 's/.*"orderId":"([^"]+)".*/\1/')

echo "==> the order is committed"
$PSQL -c "select \"Id\" from orders where \"Id\" = '$order_id';"

echo "==> and its event is waiting in the outbox"
$PSQL -F' | ' -c "select message_type, destination from wolverine.wolverine_outgoing_envelopes;"

if [ "$(outbox_count)" -eq 0 ]; then
    echo "!!  the outbox is empty. The message is in memory and will not survive a"
    echo "!!  restart. Check that UseDurableOutboxOnAllSendingEndpoints() is set."
    exit 1
fi

echo "==> starting Kafka"
$COMPOSE start kafka >/dev/null
until $COMPOSE exec -T kafka /opt/kafka/bin/kafka-broker-api-versions.sh \
    --bootstrap-server localhost:9092 >/dev/null 2>&1; do sleep 2; done

echo "==> waiting for the outbox to drain"
for _ in $(seq 1 20); do
    [ "$(outbox_count)" -eq 0 ] && { echo "    drained"; break; }
    sleep 3
done

echo "==> and the event is on the topic"
$COMPOSE exec -T kafka /opt/kafka/bin/kafka-console-consumer.sh \
    --bootstrap-server localhost:9092 --topic orders.placed \
    --from-beginning --timeout-ms 15000 2>/dev/null \
    | grep -F "$order_id" \
    || { echo "!!  the event never arrived"; exit 1; }
