"""Stateful doubles for the smoke contract; never contacts Docker or HTTP."""
import json
import os
from pathlib import Path
import subprocess
import sys

root = Path(os.environ["FAKE_PHASE4_STATE"])
scenario = os.environ["FAKE_PHASE4_SCENARIO"]
tool, *args = sys.argv[1:]


def read(name, default="0"):
    path = root / name
    return path.read_text() if path.exists() else default


def write(name, value="1"):
    (root / name).write_text(str(value))


def option(name):
    return args[args.index(name) + 1]


posted = int(read("posted"))
replayed = int(read("replayed"))
correlation = "34000000-0000-0000-0000-000000000001"
if tool == "curl":
    if args[-1].endswith("/health/ready"):
        if scenario == "unavailable-api":
            sys.exit(7)
        print(json.dumps({"status": "Healthy", "checks": [{"name": "kafka", "status": "Healthy"}]}))
    elif "--request" in args:
        body = json.loads(option("--data"))
        write("run-id", body["loyaltyMemberId"])
        write("posted", posted + 1)
        Path(option("--dump-header")).write_text(f"HTTP/1.1 200 OK\r\nX-Correlation-ID: {correlation}\r\n")
    else:
        count = posted - 1 if scenario == "lost-order" else posted
        print(json.dumps([{"id": str(i), "loyaltyMemberId": read("run-id"), "status": "Fulfilled",
                           "lineItems": [{"station": "Barista", "status": "Fulfilled"},
                                         {"station": "Kitchen", "status": "Fulfilled"}]} for i in range(count)]))
elif "ps" in args:
    if scenario == "hung-command":
        # Descendant inherits stdout: killing only its parent would leave the test hung.
        subprocess.run(["sleep", "30"], check=True)
    print("api\nbarista-worker")
    if scenario != "missing-worker":
        print("kitchen-worker")
elif "stop" in args:
    if posted != 1:
        sys.exit("Stop must follow exactly one committed order")
    write("stopped")
elif "start" in args:
    write("restart-attempted")
    if scenario == "restart-failed":
        sys.exit(1)
    write("recovered")
elif any("query-service-database.sh" in arg for arg in args):
    query = option("-c")
    if "phase4:committed" in query:
        print(posted)
    elif "phase4:envelope" in query:
        print(json.dumps({"messageId": correlation, "eventType": "order-placed", "eventVersion": 1,
                          "occurredAtUtc": "2026-09-05T01:02:03.123456+00:00", "correlationId": correlation,
                          "causationId": None, "payload": {"orderId": correlation, "items": []}}))
    else:
        owner = args[args.index("/opt/coffeeshop/query-service-database.sh") + 1]
        baseline = 17 if scenario == "existing-data" else 0
        multiple = 2 if owner == "counter" else 1
        duplicate = int(scenario == "duplicate-effect" and replayed > 0)
        print(json.dumps({"effects": baseline + posted + duplicate, "unique": baseline + posted,
                          "completed": baseline + posted * multiple, "inbox": baseline + posted * multiple,
                          "outbox": baseline + posted, "pending": int(scenario == "pending-outbox" and posted > 0),
                          "rejected": int(scenario == "rejected-outbox" and posted > 0)}))
elif any("kafka-get-offsets.sh" in arg for arg in args):
    if scenario == "fresh-topics" and posted == 0:
        sys.exit(1)
    topic = option("--topic") if "--topic" in args else "coffeeshop.orders.v1.dlt"
    offset = 10 + posted + replayed if topic.endswith(".orders.v1") else 0
    if scenario == "replay-dead-lettered" and replayed and "dlt" in topic:
        offset += 1
    print(f"{topic}:0:{offset}")
elif any("kafka-consumer-groups.sh" in arg for arg in args):
    lag = int(read("stopped") == "1" and read("recovered") == "0" and scenario != "no-backlog")
    if scenario == "duplicate-not-consumed" and replayed:
        lag = 1
    end = 10 + posted + replayed
    print(f"{option('--group')} coffeeshop.orders.v1 0 {end - lag} {end} {lag} member host client")
elif any("kafka-console-producer.sh" in arg for arg in args):
    headers, key, value = sys.stdin.readline().rstrip("\n").split("\t")
    envelope = json.loads(value)
    assert key == envelope["payload"]["orderId"]
    assert "occurred-at:2026-09-05T01:02:03.1234560+00:00" in headers
    write("replayed", replayed + 1)
else:
    sys.exit(f"Unexpected fake command: {tool} {args}")
