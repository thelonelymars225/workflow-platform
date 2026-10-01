# Enterprise seed: task list performance

Date: 2026-10-01. Branch `claude/enterprise-perf-check`.

## Setup

- Data: `--Development:SeedScenario=enterprise --Development:ResetData=true`. That is 1 organization, 53 departments over 5 levels (depth 0-4), 300 people (3 admins, 52 managers, 245 members), 8,000 personal tasks, 200 automation tasks and about 1,900 runs. `ANALYZE` was run after seeding.
- Machine: cloud container with 4 vCPU and 15 GB RAM. PostgreSQL 16.14 runs locally on default settings. .NET 10.0.12, API in Development with the `http` profile, Debug build.
- Client: [`measure_task_list.py`](measure_task_list.py) sends `GET /api/tasks` over one keep-alive connection with the demo headers: 3 warm-up requests, then 40 timed requests per row. Times are end-to-end HTTP from the client: identity, scope resolution, count query, page query and JSON.
- Callers:
  - **admin**: `enterprise/admin-0`, sees all 8,000 tasks.
  - **division manager**: manages `Technology` (depth 1), a 13-department subtree with 1,936 visible tasks.
  - **deep manager**: manages `Technology Central Alpha Squad` (depth 4), with 157 visible tasks.
  - **member**: `member-7`, 26 owned or assigned tasks.

## Results

**The two indexes compared.** Same warm API process. The two indexes are dropped and recreated between runs, with `ANALYZE` each time. Page size is 50 unless stated. This was the second of two rounds; round 1 agreed within about 1 ms.

| Caller | Request | Visible tasks | Without: median ms | Without: p95 ms | With: median ms | With: p95 ms |
|---|---|---|---|---|---|---|
| admin | page 1 | 8000 | 7.6 | 9.4 | 4.9 | 7.4 |
| admin | page 2 | 8000 | 7.7 | 10.6 | 6.2 | 8.0 |
| admin | middle page | 8000 | 8.4 | 10.7 | 7.1 | 9.8 |
| admin | last page (160) | 8000 | 8.3 | 10.8 | 7.8 | 10.4 |
| admin | status=InProgress p1 | 8000 | 7.1 | 9.4 | 5.1 | 7.2 |
| admin | pageSize=200 p1 | 8000 | 8.3 | 11.3 | 6.0 | 7.9 |
| division manager (depth 1) | page 1 | 1936 | 6.6 | 7.7 | 5.9 | 8.0 |
| division manager (depth 1) | page 2 | 1936 | 6.8 | 9.4 | 6.3 | 8.0 |
| division manager (depth 1) | middle page | 1936 | 7.0 | 9.4 | 7.3 | 9.4 |
| division manager (depth 1) | last page (39) | 1936 | 7.3 | 10.0 | 6.7 | 8.7 |
| division manager (depth 1) | status=InProgress p1 | 1936 | 6.6 | 9.1 | 6.5 | 9.9 |
| division manager (depth 1) | pageSize=200 p1 | 1936 | 7.0 | 9.6 | 6.2 | 7.3 |
| deep manager (depth 4) | page 1 | 157 | 5.7 | 11.6 | 5.3 | 6.7 |
| deep manager (depth 4) | page 2 | 157 | 5.4 | 7.0 | 5.9 | 6.7 |
| deep manager (depth 4) | middle page | 157 | 5.7 | 7.6 | 6.2 | 8.1 |
| deep manager (depth 4) | last page (4) | 157 | 5.7 | 7.0 | 6.6 | 9.2 |
| deep manager (depth 4) | status=InProgress p1 | 157 | 5.8 | 7.7 | 5.7 | 7.3 |
| deep manager (depth 4) | pageSize=200 p1 | 157 | 6.0 | 7.6 | 6.9 | 9.0 |
| member | page 1 | 26 | 4.8 | 6.8 | 5.1 | 7.0 |
| member | page 2 | 26 | 4.2 | 5.9 | 4.9 | 6.4 |
| member | middle page | 26 | 5.0 | 6.7 | 5.1 | 8.4 |
| member | last page (1) | 26 | 5.5 | 6.5 | 5.1 | 7.2 |
| member | status=InProgress p1 | 26 | 4.6 | 5.8 | 5.2 | 9.9 |
| member | pageSize=200 p1 | 26 | 5.2 | 6.8 | 5.5 | 8.0 |

A first pass on a freshly started process was 2-6 ms slower across the board, for example admin page 1 at 13.0 ms median. That came from warm-up (JIT, connection pool), not from the database.

**SQL alone** (`EXPLAIN (ANALYZE, BUFFERS)`, the exact statements EF generates, enterprise organization):

| Query | Before | After | Plan after |
|---|---|---|---|
| Admin page 1 (`ORDER BY CreatedAt DESC, Id DESC LIMIT 50`) | 3.1 ms, seq scan + top-N sort of 8,000 rows | 0.20 ms | Index scan on `IX_PersonalTasks_Org_CreatedAt_Id`, stops after 50 rows |
| Admin last page (`OFFSET 7950`) | 6.4 ms, seq scan + 2.2 MB in-memory sort | 4.2 ms | Index scan reading 8,000 entries; the OFFSET cost remains |
| Admin `status=InProgress` page 1 | 1.4 ms, seq scan + sort | 0.07 ms | Index scan on `IX_PersonalTasks_Org_Status_CreatedAt_Id` |
| Admin count | 1.0 ms | 1.0 ms | Index-only scan |
| Division manager page 1 (`owner OR assignee OR DepartmentId = ANY(13 ids)`) | 1.0 ms, BitmapOr over 3 indexes + top-N sort | 0.12 ms | Ordered index scan with filter (137 rows skipped) |
| Division manager count | 0.46 ms | 0.56 ms | BitmapOr |
| Deep manager page 1 | 0.16 ms | 0.24 ms | BitmapOr + top-N sort (planner keeps the bitmap for narrow scopes) |
| Member page 1 / count | 0.06 ms | 0.08 ms | BitmapOr on owner and assignee indexes |
| Scope: membership, departments, grants, delegations | each below 0.03 ms | same | Unique/secondary index; departments seq scan over 53 rows |
| Automation runs page | 0.06 ms | same | `IX_AutomationRuns_AutomationTaskId_QueuedAt` |

## Change made

Migration `PersonalTaskListIndexes` adds two indexes that follow the list order:

- `IX_PersonalTasks_Org_CreatedAt_Id` on `(OrganizationId, CreatedAt DESC, Id DESC)`: whole-organization pages read the index instead of sorting every task.
- `IX_PersonalTasks_Org_Status_CreatedAt_Id` on `(OrganizationId, Status, CreatedAt DESC, Id DESC)`: the same for the status filter.

Admin page 1 drops 3.1 → 0.2 ms in SQL and 7.6 → 4.9 ms end to end. The gap grows linearly with the number of tasks. Without the index, every admin page sorts the whole organization; with it, early pages stay constant. Manager and member requests are unchanged within noise. Their scoped queries were already sub-millisecond through the existing `(OrganizationId, OwnerUserId)`, `(OrganizationId, AssigneeUserId)` and `(OrganizationId, DepartmentId)` indexes. Automation tasks (200 rows) do not need an extra index yet.

## Findings and follow-ups (not done here)

- **Most of the time is outside SQL.** A 5-7 ms request spends below 1.5 ms in SQL. Each list request makes 6 round trips: membership, departments, access exceptions, delegations, count, page. Caching the per-org department list, or loading the four scope queries in one batch, would save more than further indexes would.
- **Deep OFFSET pages still read every preceding row**: 4.2 ms for page 160 of 8,000. If organizations grow past roughly 100k tasks, switch to keyset pagination with a `(CreatedAt, Id)` cursor; the new index already supports it.
- **`count(*)` is linear in the visible rows**: 1 ms for 8,000. Fine at this size; consider a capped or estimated count for very large organizations.
- **`DepartmentId = ANY(@ids)` grows with the size of the subtree.** It is trivial at 53 departments. With thousands of departments, a join on the materialized `Path` prefix (already indexed with `text_pattern_ops`) would be better than a large array parameter.
- **Statistics after seeding**: run `ANALYZE` after a large seed (autovacuum gets there eventually) so the planner sees the real row counts.

## Reproduce

```sh
dotnet run --project services/Workflow.Api --launch-profile http -- --Development:SeedScenario=enterprise --Development:ResetData=true --Development:ExitAfterSeed=true
psql -d workflow_dev -c 'ANALYZE;'
dotnet run --project services/Workflow.Api --launch-profile http &
python3 docs/perf/measure_task_list.py http://localhost:5159 40
```
