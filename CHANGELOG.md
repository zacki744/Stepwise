# Changelog

Patch-note style. Newest first. Dataset types are only added in their own, explicitly named patch.

## Unreleased

### Protocol
- `uploadChunk`: `createSession` and `rehydrateSession` can now be sent in pieces, so datasets up to the
  size limits (500,000 cells) can be uploaded and restored despite the 1 MiB per-frame cap. Limits:
  64 MiB per upload, 4096 chunks, 2 minutes, 256 MiB buffered across all connections. A bad, out-of-order or
  oversized upload is rejected and discarded; a stalled one closes the connection. Frontend helper:
  `sendLarge()`.

### Reliability
- Seeded generators use an in-house `DeterministicRandom` (SplitMix64) instead of `System.Random`,
  whose sequence is not guaranteed across .NET versions. Replaying a saved operation log can no longer
  change because the runtime was upgraded. Golden tests pin the PRNG and the maze/random-fill output.
  **Behaviour change:** the same seed now produces different (but equally valid) output than 0.1.0.
- Logging goes through `ILogger` instead of `Console`. A client that vanishes without a close
  handshake (`IOException`, like `WebSocketException`) is treated as a normal disconnect, not a crash.
- `generate-maze` rejects non-positive `width`/`height` with a client-input error instead of
  throwing `OverflowException`. Found by the catalog-wide hostile-options contract test.

- `find-bridges` and `find-articulation-points` now charge the work budget for their graph pass. Before,
  a graph with no bridges never touched the budget, so the operation could not be time-limited.
  Surfaced by the contract test once the PRNG change produced a different sample graph.
- Same fix for `find-connected-components`, `add-connection`, `remove-connection`, `isolate-node` and
  `tree-stats`: they walk or copy the whole dataset while recording few trace events, so the trace alone
  charged almost nothing. `SparseTraceBudgetTests` pins this on inputs where nothing is found.

### Tests
- Property-based tests (CsCheck) compare shortest path, Bellman–Ford, minimum spanning tree, max-flow
  (and its min-cut) and strongly connected components against simple independent references on
  thousands of random graphs.

### Project
- GitHub Actions CI (build + test on every push and pull request).
- `.gitattributes` for consistent line endings, `LICENSE` (MIT), this changelog.
- README corrected: Tree storage and the test strategy are described as they actually are.

## 0.1.0 — initial commit

- 3 dataset types: Matrix (flat `int[]`), Graph (CSR, weighted), Tree (binary search tree).
- 60 operations across generation, modification, query, conversion and destruction.
- Live WebSocket sessions: `createSession`, `rehydrateSession`, `executeOperation`; streamed step
  traces; transactional state (an aborted or failed operation changes nothing).
- Execution limits (concurrency gate, time limit, work budget), session capacity and idle sweep,
  1 MiB message cap.
- 667 test cases, including a contract harness that runs every registered operation.
