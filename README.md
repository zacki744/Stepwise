# Dataset Operation Engine

A stateful, in-memory backend for building and transforming **matrices, graphs and trees** over a live WebSocket session. A client creates a dataset, applies operations one at a time, and receives a streamed step trace and the result for each. The client owns undo and local persistence; the server holds the working state only while the session is alive.

The project is a showcase of backend systems engineering in .NET: concurrency control, resource limits, algorithm implementations, clean architecture and a test-first approach.

> **Status:** the backend is the focus and is feature-rich (60 operations). The frontend is a rough proof-of-concept until the backend is complete.

## What it does

- **3 dataset types** with flat, array-backed storage (no per-element objects): `Matrix` (flat `int[]`), `Graph` (CSR with weights), `Tree` (binary search tree via parallel arrays).
- **60 operations** across generation, modification, query, conversion and destruction — for example flood fill, Game of Life, grid pathfinding, shortest path, MST, Tarjan bridges/articulation points, strongly connected components, Dinic max-flow, bipartite matching, betweenness centrality, Floyd–Warshall, graph colouring, BST insert/remove/LCA/k-th smallest, and maze generation.
- **Streamed traces**: each operation emits step events as it works, so a client can animate and log them. Very long traces are coalesced into one message plus the final state.
- **Transactional semantics**: operations work on a copy; the session commits only after a successful `Execute`. A failure or abort changes nothing.
- **Deterministic**: all randomness is seeded, which is what makes rehydration by replay safe.

## Architecture

Ports & Adapters, with deliberately few patterns:

```
Engine.Domain     datasets, operations, tracing, limits   (no ASP.NET, no I/O)
Engine.Session    session registry, per-session locking, idle eviction
Engine.Protocol   wire messages (System.Text.Json, polymorphic)
Engine.Gateway    ASP.NET Core host, WebSocket loop, operation catalog
```

- **Strategy**: every operation implements `IOperation<TDataset>` and is selected by id from a registry.
- **Capability interfaces** (`ITraversable`, `IWeighted`, `IOrderable`) instead of a dataset inheritance tree.
- **Live sessions** with sticky-session scaling in mind; externalised state (e.g. Redis) is a deliberate non-goal for now.
- Algorithms are **iterative**, so deep graphs cannot overflow the stack.

## Reliability

- Per-session lock; cross-session parallelism.
- Execution policy: concurrency gate, queue timeout, per-operation time limit and work-unit budget (operations abort cleanly with a reason).
- Limits on dataset size (cells, edges), returned list size, WebSocket message size (1 MiB) and session count; idle sessions are swept.
- Every `executeOperation` produces exactly one terminal message (`operationComplete` or `operationFailed`). A per-message catch-all keeps the connection alive.
- A catalog-wide **contract test** runs every registered operation: valid example succeeds without mutating its input, same input gives identical output, hostile options fail only with client-input errors and leave the session untouched, and the work budget is consulted.

## Wire protocol (short version)

Client → server: `createSession`, `rehydrateSession`, `executeOperation`
Server → client: `sessionReady`, `stepEvent`, `operationComplete`, `operationFailed`, `error`

`GET /operations` lists every operation with an example options payload.

## Getting started

Requirements: .NET 9 SDK, Node.js LTS (for the frontend).

```bash
# backend
cd Engine
dotnet build
dotnet test
dotnet run --project src/Engine.Gateway     # WebSocket at ws://localhost:5053/session (check the console for the actual port)

# frontend (proof of concept)
cd engine-frontend
npm install
npm run dev
```

## Testing

xUnit throughout. Domain tests need no server; gateway tests use `WebApplicationFactory` and talk to the real WebSocket endpoint. Algorithms are cross-checked against simple reference implementations where practical.

## Repository layout

```
Engine/             .NET solution (src/ and tests/)
engine-frontend/    SvelteKit + Svelte 5 + TypeScript + Tailwind client
```

## Roadmap

- Frontend UX: drag/drop nodes, hover actions, per-operation side panels, step log, algorithm comparison.
- Backend: operation descriptors served by `/operations`, state snapshots for trees, directed edge edits, real graph node removal, execution stats, text import.
- Later, as separate patches: more algorithm variants, self-balancing trees, and new dataset types.
