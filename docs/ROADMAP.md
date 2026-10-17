# Roadmap

Standing rules: backend first and the frontend stays rough until the backend is complete; no new datatype until
Matrix, Graph and Tree have exhaustive operations (a new datatype is its own explicit patch); no pattern
without a reason; every change is in [CHANGELOG.md](../CHANGELOG.md); tests first.

**Where we are.** 61 operations, 3 datasets, 700+ tests, CI green (build, tests, container smoke test).
Deployment files exist but **nothing is deployed, and nothing should be until phase 5 is done**. The audit that
drives phases 3 to 5 is [AUDIT-2026-10.md](AUDIT-2026-10.md).

## Phase 3: hardening II (the audit's findings)

| Patch | Fixes | What |
| --- | --- | --- |
| 3c | H2, H3, M2 | Lazy span-based tokenizer with a line-length cap; dataset construction runs under the execution gate with a time limit; upload budget counts parse cost, not just buffered bytes. |
| 3d | H1 | A server-wide dataset budget (sum of cells + edges across sessions); `server-busy` beyond it. A BenchmarkDotNet project that measures real peak memory per operation on maximum-size datasets, so the budget is a measured number. Container memory limit and `DOTNET_GCHeapHardLimit` documented. |
| 3e | M1, M4 | Idle timeout, per-message completion timeout, WebSocket `KeepAliveTimeout`; per-address cap on concurrent operations. |
| 3f | L1 to L5 | Workflow `permissions`, Dependabot, pinned base images, remove the unused OpenAPI package, `nosniff`, drop the stray `UnitTest1.cs`. |

Decision needed (M3): leave the demo open, or add an optional access token. Default proposal: leave open, document.

## Phase 4: structure and option validation (before adding operations)

1. **One definition per operation.** A single explicit registration carries id, label, category, description,
   options, example, result description and factory. Replaces `OperationCatalog` + `OperationExamples` +
   `OperationDescriptors` + the test list. No reflection; it is still a plain list you can read top to bottom.
2. **Options parsed from the declared options.** Unknown keys, wrong types, missing required options and
   out-of-range values are rejected in one place with a clear message (fixes M5 and M6). Handlers receive typed
   values and never see `JsonElement`.
3. `Operations/` split into `Matrix/`, `Graph/`, `Tree/`; one file per operation from here on.
4. `SessionSocketLoop` split into one handler per message type.
5. Golden-JSON tests for every protocol message (the contract the frontend relies on).

## Phase 5: the edge-case matrix

Operations × adversarial datasets (lists in the audit), asserting no unexpected exception type, no input mutation,
determinism and budget compliance. Expect it to find real bugs; each finding becomes a named regression test. Add
property tests against reference implementations where an operation has an obvious one (see phase 7). Also a
long-running soak test: many sessions, random operations, random disconnects, checking memory and the session
count return to baseline.

**Exit criteria for "backend hardened":** phases 3 to 5 done, audit table has no open High or Medium row,
benchmarks recorded in the repo.

## Phase 6: descriptor v2: what the frontend needs to be generic

`GET /operations` already says what an operation *takes*. The frontend also needs what it *gives back* and what it
*shows while running*, so a panel can be built without any per-operation code:

```json
{
  "id": "shortest-path",
  "label": "Shortest path",
  "category": "Paths and flow",
  "description": "Cheapest route between two nodes (Dijkstra).",
  "effect": "readOnly",
  "options": [ { "name": "start", "kind": "node", "required": true, "label": "Start" } ],
  "returns": {
    "kind": "path",
    "fields": [
      { "name": "path", "kind": "nodeList", "description": "Nodes from start to target; empty if unreachable" },
      { "name": "distance", "kind": "number", "description": "Total weight; null if unreachable" }
    ]
  },
  "steps": [
    { "type": "NodeSettled", "visual": "highlightNode",
      "payload": [ { "name": "node", "kind": "node" }, { "name": "distance", "kind": "number" } ] }
  ],
  "example": { "options": { "start": 0, "target": 7 } }
}
```

- `effect` comes from the existing `StateEffect` (read-only, replaces state, changes dataset type).
- `returns.kind` and `steps[].visual` come from small fixed vocabularies (`path`, `nodeList`, `table`, `scalar`,
  `map`, `components`... and `highlightNode`, `highlightEdge`, `highlightCell`, `highlightRange`, `none`).
- A contract test runs every operation on the sample datasets and checks that the real result and the real step
  payloads match what the descriptor declares, so the descriptor cannot lie.
- Unknown kinds are allowed: the frontend falls back to a JSON viewer, so a new operation never breaks the UI.

## Phase 7: more operations (61 → about 115)

Batches of about 10, each: tests first, a property test against a simple reference where possible, descriptor v2
entry, CHANGELOG line. Candidates, roughly in priority order (the first group in each list is the most valuable for
visualisation and for a portfolio):

**Graph (+25)**: Prim (to contrast with Kruskal), Kosaraju (a second SCC), Dijkstra to *all* nodes, BFS levels,
DFS discovery/finish times with edge classification, return an actual cycle, Eulerian path/circuit (Hierholzer),
Hungarian assignment (weighted bipartite), minimum-cost flow, minimum cut (Stoer-Wagner), PageRank, diameter /
radius / eccentricity / center, clustering coefficient and triangle count, k-core decomposition, biconnected
components, condensation DAG, transitive closure, longest path in a DAG, critical path scheduling, clique
(Bron-Kerbosch), girth (shortest cycle), contract edge, merge nodes, complement, relabel, degree-sequence check.

**Matrix (+12)**: A* and Dijkstra on a weighted grid, spiral order, largest rectangle of ones, maximum sub-rectangle
sum, region sum via prefix sums, connected-component labelling, distance transform, replace value / threshold /
invert, roll/shift, sort rows or columns, pad / tile, diagonal operations.

**Tree (+14)**: floor/ceiling, predecessor/successor, range count and range list, rank (order statistic), level
order (BFS), is-balanced, is-valid-BST, diameter, path to a node, depth of a node, rotate left/right at a node (to
teach rebalancing by hand), bulk insert, delete range, subtree sum, Morris traversal.

Not before this is done and reviewed: new datatypes (AVL tree, heap, trie, linked list, array/string), each as its own
patch.

## Phase 8: the frontend, built from the descriptors

Backend-planned, so the contract stays the priority: the UI knows nothing about individual operations.

- **Operation browser**: groups by `category`, searchable, shows `description`. Choosing one opens a side panel.
- **Form builder**: one input widget per option `kind`. `node`, `row` and `column` kinds can also be filled by
  clicking on the canvas. Defaults from `default`, validation from `required`/`choices`; the panel shows the
  `returns` description before you run it.
- **Result renderer registry**: maps `returns.kind` to a component (path highlights on the canvas, table, key/value,
  components coloured). Unregistered kinds fall back to a JSON viewer.
- **Step player**: maps `steps[].visual` to canvas highlights; play/pause/step; speed; for coalesced traces shows
  the summary and jumps to the final state.
- **Workflow**: a visible step log, undo by replaying the log from the saved initial state, save/load, compare two
  algorithms on the same dataset (run both, show stats side by side using `stats`).
- **Editing without JSON**: hover actions on nodes, drag to move, drop near a node to auto-connect, add/remove
  connections (uses the `directed` option), delete a node (`remove-graph-node` with the returned mapping), and a text
  import box for big pastes (text import already exists on the backend).
- Quality gates for the frontend: `svelte-check`, ESLint, a CI job, and a handful of Playwright flows.

## Phase 9: deployment

Only after phase 5. Files already exist (`Engine/Dockerfile`, `docker-compose.yml`, CI image check). Remaining:
choose a host, set `Security__AllowedOrigins__0`, enable forwarded headers behind the proxy, one instance only,
`wss://`, uptime check on `/health`, and a short operations note (limits, logs, how to restart).
