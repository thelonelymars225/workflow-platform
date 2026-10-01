#!/usr/bin/env python3
"""Times GET /api/tasks on the seeded enterprise scenario for an admin, a deep manager and a member.

Usage: seed enterprise, start the API (Development, http profile), then
    python3 docs/perf/measure_task_list.py [base_url] [iterations]
IDs are derived exactly like Data/Seeding/SeedIds.cs (UUID v5 over a fixed namespace).
"""
import http.client, json, statistics, sys, time, uuid
from urllib.parse import urlparse

NS = uuid.UUID("3f1b8c52-6a0e-4c1e-9d55-1f0b6c2e7a90")
seed = lambda key: str(uuid.uuid5(NS, key))
base = urlparse(sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5159")
iterations = int(sys.argv[2]) if len(sys.argv) > 2 else 30
org = seed("org/enterprise")

def manager_of(dept_key):
    return seed("user/enterprise/manager/" + uuid.UUID(seed("dept/" + dept_key)).hex)

personas = [
    ("admin", seed("user/enterprise/admin-0")),
    ("division manager (depth 1)", manager_of("enterprise/Technology")),
    ("deep manager (depth 4)", manager_of("enterprise/Technology/Central/Alpha/squad")),
    ("member", seed("user/enterprise/member-7")),
]

conn = http.client.HTTPConnection(base.hostname, base.port)

def get(path, user):
    start = time.perf_counter()
    conn.request("GET", path, headers={"X-Demo-User-Id": user, "X-Organization-Id": org})
    response = conn.getresponse()
    body = response.read()
    elapsed = (time.perf_counter() - start) * 1000
    assert response.status == 200, (response.status, body[:300])
    return elapsed, json.loads(body)

print("| Caller | Request | Total visible | Median ms | p95 ms |")
print("|---|---|---|---|---|")
for name, user in personas:
    _, first = get("/api/tasks?page=1&pageSize=50", user)
    total = first["totalCount"]
    last = max(1, -(-total // 50))
    for label, path in [
        ("page 1", "/api/tasks?page=1&pageSize=50"),
        ("page 2", "/api/tasks?page=2&pageSize=50"),
        ("middle page", f"/api/tasks?page={max(1, last // 2)}&pageSize=50"),
        (f"last page ({last})", f"/api/tasks?page={last}&pageSize=50"),
        ("status=InProgress p1", "/api/tasks?status=InProgress&page=1&pageSize=50"),
        ("pageSize=200 p1", "/api/tasks?page=1&pageSize=200"),
    ]:
        for _ in range(3):
            get(path, user)  # warm-up
        samples = sorted(get(path, user)[0] for _ in range(iterations))
        p95 = samples[min(len(samples) - 1, int(round(0.95 * len(samples))) - 1)]
        print(f"| {name} | {label} | {total} | {statistics.median(samples):.1f} | {p95:.1f} |")
