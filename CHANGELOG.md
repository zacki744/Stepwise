# Changelog

Patch-note style. Newest first. Dataset types are only added in their own, explicitly named patch.

## Unreleased

### Reliability
- Logging goes through `ILogger` instead of `Console`. A client that vanishes without a close
  handshake (`IOException`, like `WebSocketException`) is treated as a normal disconnect, not a crash.
- `generate-maze` rejects non-positive `width`/`height` with a client-input error instead of
  throwing `OverflowException`. Found by the catalog-wide hostile-options contract test.

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
