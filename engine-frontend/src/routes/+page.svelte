<script lang="ts">
  import {
    connect,
    send,
    connectionStatus,
    latestMessage,
    statusMessage,
    messageLog
  } from '../lib/stores/connection';
  import { drawMazeGrid, drawMazePassage, type MazeDimensions } from '../lib/canvas/maze';
  import { layoutNodesInCircle, drawDebtNodes, drawTransaction } from '../lib/canvas/debtSettlement';
  import { isStepEvent } from '../lib/protocol/messages';
  import { operations, exampleInitialPayload, type DatasetType } from '../lib/operations/catalog';

  type Mode = 'maze' | 'debt' | 'explorer';
  let mode: Mode = $state('maze');

  let wsUrl = $state('ws://localhost:5053/session');

  // ---------- Maze state ----------
  let mazeWidth = $state(8);
  let mazeHeight = $state(8);
  let mazeSeed = $state(42);
  let mazeDimensions: MazeDimensions = $state({ width: 8, height: 8 });

  // ---------- Debt settlement state ----------
  let debtNodeCount = $state(4);
  let edgeFrom = $state(0);
  let edgeTo = $state(2);
  let edgeWeight = $state(30);
  let debtEdges = $state<[number, number, number][]>([]);
  let debtPositions: { x: number; y: number }[] = [];

  // ---------- Explorer state (generic, covers every registered operation) ----------
  let explorerDatasetType: DatasetType = $state('graph');
  let explorerOperationId = $state('generate-maze');
  let explorerInitialPayloadText = $state(JSON.stringify(exampleInitialPayload.graph, null, 2));
  let explorerOptionsText = $state(
    JSON.stringify(operations.find((o) => o.id === 'generate-maze')!.exampleOptions, null, 2)
  );
  let explorerJsonError = $state<string | null>(null);

  const explorerOperationsForType = $derived(operations.filter((o) => o.datasetType === explorerDatasetType));

  // Maps operation id -> which existing canvas renderer applies, if any.
  // Extend this as new operations get dedicated visuals; everything not
  // listed here falls back to the "no visual yet" placeholder.
  const visualKindByOperation: Record<string, 'maze' | 'debt'> = {
    'generate-maze': 'maze',
    'settle-debts': 'debt'
  };
  const explorerVisualKind = $derived(visualKindByOperation[explorerOperationId] ?? null);

  function onExplorerDatasetTypeChange(): void {
    explorerInitialPayloadText = JSON.stringify(exampleInitialPayload[explorerDatasetType], null, 2);
    const first = explorerOperationsForType[0];
    if (first) onExplorerOperationChange(first.id);
  }

  function onExplorerOperationChange(id: string): void {
    explorerOperationId = id;
    const def = operations.find((o) => o.id === id);
    explorerOptionsText = JSON.stringify(def?.exampleOptions ?? {}, null, 2);
  }

  function explorerCreateSession(): void {
    try {
      const payload = JSON.parse(explorerInitialPayloadText);
      explorerJsonError = null;
      pendingAction = null; // Explorer is explicit two-step: Create, then Execute — no auto-chaining

      if (ctx && canvasEl) {
        ctx.clearRect(0, 0, canvasEl.width, canvasEl.height);
        if (explorerVisualKind === 'debt') {
          debtPositions = layoutNodesInCircle(canvasEl, payload.nodeCount ?? 0);
          drawDebtNodes(ctx, debtPositions);
        }
      }

      send({ type: 'createSession', datasetType: explorerDatasetType, initialPayload: payload });
    } catch (e) {
      explorerJsonError = `Invalid initial payload JSON: ${(e as Error).message}`;
    }
  }

  function explorerExecute(): void {
    try {
      const options = JSON.parse(explorerOptionsText);
      explorerJsonError = null;

      if (explorerVisualKind === 'maze' && ctx && canvasEl) {
        mazeDimensions = { width: options.width ?? 0, height: options.height ?? 0 };
        ctx.clearRect(0, 0, canvasEl.width, canvasEl.height);
        drawMazeGrid(ctx, mazeDimensions);
      }

      send({ type: 'executeOperation', requestId: crypto.randomUUID(), operationId: explorerOperationId, options });
    } catch (e) {
      explorerJsonError = `Invalid options JSON: ${(e as Error).message}`;
    }
  }

  // ---------- Shared canvas + push-driven reactivity ----------
  let canvasEl: HTMLCanvasElement | undefined = $state();
  let ctx: CanvasRenderingContext2D | null = $state(null);
  let pendingAction: { kind: 'maze' | 'debt' } | null = null;

  $effect(() => {
    ctx = canvasEl?.getContext('2d') ?? null;
  });

  $effect(() => {
    const message = $latestMessage;
    if (!message || !ctx) return;

    if (message.type === 'sessionReady' && pendingAction) {
      if (pendingAction.kind === 'maze') {
        send({
          type: 'executeOperation',
          requestId: crypto.randomUUID(),
          operationId: 'generate-maze',
          options: { width: mazeWidth, height: mazeHeight, seed: mazeSeed }
        });
      } else {
        send({
          type: 'executeOperation',
          requestId: crypto.randomUUID(),
          operationId: 'settle-debts',
          options: {}
        });
      }
      pendingAction = null;
    }

    if (isStepEvent(message)) {
      if (message.stepType === 'PassageCarved') {
        drawMazePassage(ctx, mazeDimensions, message.payload as { from: number; to: number });
      }
      if (message.stepType === 'TransactionCreated') {
        drawTransaction(ctx, debtPositions, message.payload as { from: number; to: number; amount: number });
      }
    }
  });

  function handleConnect(): void {
    connect(wsUrl);
  }

  function handleGenerateMaze(): void {
    mazeDimensions = { width: mazeWidth, height: mazeHeight };
    pendingAction = { kind: 'maze' };

    if (ctx && canvasEl) {
      ctx.clearRect(0, 0, canvasEl.width, canvasEl.height);
      drawMazeGrid(ctx, mazeDimensions);
    }

    send({
      type: 'createSession',
      datasetType: 'graph',
      initialPayload: { nodeCount: mazeWidth * mazeHeight, edges: [] }
    });
  }

  function addDebtEdge(): void {
    debtEdges = [...debtEdges, [edgeFrom, edgeTo, edgeWeight]];
  }

  function removeDebtEdge(index: number): void {
    debtEdges = debtEdges.filter((_, i) => i !== index);
  }

  function handleSettleDebts(): void {
    pendingAction = { kind: 'debt' };

    if (ctx && canvasEl) {
      ctx.clearRect(0, 0, canvasEl.width, canvasEl.height);
      debtPositions = layoutNodesInCircle(canvasEl, debtNodeCount);
      drawDebtNodes(ctx, debtPositions);
    }

    send({
      type: 'createSession',
      datasetType: 'graph',
      initialPayload: { nodeCount: debtNodeCount, edges: debtEdges }
    });
  }

  function formatLogEntry(msg: unknown): string {
    return JSON.stringify(msg);
  }
</script>

<main class="min-h-screen bg-neutral-900 text-neutral-100 p-6">
  <h1 class="text-xl font-semibold mb-4">Engine</h1>

  <section class="bg-neutral-800 rounded-lg p-4 mb-4 flex items-center gap-3">
    <input bind:value={wsUrl} class="bg-neutral-700 rounded px-2 py-1 text-sm w-72" />
    <button onclick={handleConnect} class="bg-blue-600 hover:bg-blue-500 rounded px-3 py-1 text-sm">
      Connect
    </button>
    <span
      class="text-xs px-2 py-1 rounded-full"
      class:bg-green-900={$connectionStatus === 'connected'}
      class:text-green-400={$connectionStatus === 'connected'}
      class:bg-red-900={$connectionStatus !== 'connected'}
      class:text-red-400={$connectionStatus !== 'connected'}
    >
      {$connectionStatus}
    </span>
  </section>

  {#if $statusMessage}
    <section
      class="rounded-lg p-3 mb-4 text-sm"
      class:bg-red-950={$statusMessage.kind === 'error'}
      class:text-red-300={$statusMessage.kind === 'error'}
      class:bg-amber-950={$statusMessage.kind === 'operation-failed'}
      class:text-amber-300={$statusMessage.kind === 'operation-failed'}
    >
      {$statusMessage.text}
    </section>
  {/if}

  <section class="bg-neutral-800 rounded-lg p-4 mb-4 flex gap-2">
    <button
      onclick={() => (mode = 'maze')}
      class="px-3 py-1 rounded text-sm"
      class:bg-blue-600={mode === 'maze'}
      class:bg-neutral-700={mode !== 'maze'}
    >
      Maze
    </button>
    <button
      onclick={() => (mode = 'debt')}
      class="px-3 py-1 rounded text-sm"
      class:bg-blue-600={mode === 'debt'}
      class:bg-neutral-700={mode !== 'debt'}
    >
      Debt Settlement
    </button>
    <button
      onclick={() => (mode = 'explorer')}
      class="px-3 py-1 rounded text-sm"
      class:bg-blue-600={mode === 'explorer'}
      class:bg-neutral-700={mode !== 'explorer'}
    >
      Explorer (all operations)
    </button>
  </section>

  <!-- Controls | Visual (canvas or explorer config) | Live log — log stays visible across every mode -->
  <section class="grid grid-cols-[14rem_1fr_24rem] gap-4 items-start">
    <!-- Controls column -->
    <div class="bg-neutral-800 rounded-lg p-4 flex flex-col gap-2">
      {#if mode === 'maze'}
        <label class="text-sm">
          Width <input type="number" bind:value={mazeWidth} class="bg-neutral-700 rounded px-2 py-1 w-20 ml-2" />
        </label>
        <label class="text-sm">
          Height <input type="number" bind:value={mazeHeight} class="bg-neutral-700 rounded px-2 py-1 w-20 ml-2" />
        </label>
        <label class="text-sm">
          Seed <input type="number" bind:value={mazeSeed} class="bg-neutral-700 rounded px-2 py-1 w-20 ml-2" />
        </label>
        <button
          onclick={handleGenerateMaze}
          disabled={$connectionStatus !== 'connected'}
          class="bg-blue-600 hover:bg-blue-500 disabled:bg-neutral-700 disabled:text-neutral-500 rounded px-3 py-1 text-sm mt-2"
        >
          Generate Maze
        </button>
      {:else if mode === 'debt'}
        <label class="text-sm">
          Nodes <input type="number" bind:value={debtNodeCount} class="bg-neutral-700 rounded px-2 py-1 w-20 ml-2" />
        </label>
        <div class="flex gap-1 items-center text-xs">
          <input type="number" bind:value={edgeFrom} class="bg-neutral-700 rounded px-1 py-1 w-10" />
          owes
          <input type="number" bind:value={edgeTo} class="bg-neutral-700 rounded px-1 py-1 w-10" />
          <input type="number" bind:value={edgeWeight} class="bg-neutral-700 rounded px-1 py-1 w-14" />
          <button onclick={addDebtEdge} class="bg-neutral-600 rounded px-2 py-1">+</button>
        </div>
        <ul class="text-xs">
          {#each debtEdges as [from, to, weight], i}
            <li class="flex justify-between">
              {from} owes {to}: {weight}
              <button onclick={() => removeDebtEdge(i)} class="text-red-400">×</button>
            </li>
          {/each}
        </ul>
        <button
          onclick={handleSettleDebts}
          disabled={$connectionStatus !== 'connected'}
          class="bg-blue-600 hover:bg-blue-500 disabled:bg-neutral-700 disabled:text-neutral-500 rounded px-3 py-1 text-sm mt-2"
        >
          Create &amp; Settle
        </button>
      {:else}
        <label class="text-sm">
          Dataset type
          <select
            bind:value={explorerDatasetType}
            onchange={onExplorerDatasetTypeChange}
            class="bg-neutral-700 rounded px-2 py-1 w-full mt-1"
          >
            <option value="matrix">matrix</option>
            <option value="graph">graph</option>
            <option value="tree">tree</option>
          </select>
        </label>

        <button
          onclick={explorerCreateSession}
          disabled={$connectionStatus !== 'connected'}
          class="bg-blue-600 hover:bg-blue-500 disabled:bg-neutral-700 disabled:text-neutral-500 rounded px-3 py-1 text-sm"
        >
          Create Session
        </button>

        <label class="text-sm">
          Initial payload (JSON)
          <textarea
            bind:value={explorerInitialPayloadText}
            rows="4"
            class="bg-neutral-700 rounded px-2 py-1 w-full mt-1 font-mono text-xs"
          ></textarea>
        </label>

        <label class="text-sm">
          Operation
          <select
            value={explorerOperationId}
            onchange={(e) => onExplorerOperationChange((e.target as HTMLSelectElement).value)}
            class="bg-neutral-700 rounded px-2 py-1 w-full mt-1"
          >
            {#each explorerOperationsForType as op}
              <option value={op.id}>{op.label}</option>
            {/each}
          </select>
        </label>

        <label class="text-sm">
          Options (JSON)
          <textarea
            bind:value={explorerOptionsText}
            rows="4"
            class="bg-neutral-700 rounded px-2 py-1 w-full mt-1 font-mono text-xs"
          ></textarea>
        </label>

        <button
          onclick={explorerExecute}
          disabled={$connectionStatus !== 'connected'}
          class="bg-blue-600 hover:bg-blue-500 disabled:bg-neutral-700 disabled:text-neutral-500 rounded px-3 py-1 text-sm"
        >
          Execute Operation
        </button>

        {#if explorerJsonError}
          <p class="text-red-400 text-xs">{explorerJsonError}</p>
        {/if}

        {#if !explorerVisualKind}
          <p class="text-neutral-500 text-xs mt-1">
            No canvas visual exists yet for "{explorerOperationId}" — results still show fully in the log.
          </p>
        {/if}
      {/if}
    </div>

    <!-- Visual column: canvas whenever one applies (Maze/Debt tabs always, Explorer only for
         operations with a known renderer), placeholder otherwise -->
    <div class="bg-neutral-800 rounded-lg p-4 flex items-center justify-center min-h-[26rem]">
      {#if mode === 'maze' || mode === 'debt' || (mode === 'explorer' && explorerVisualKind)}
        <canvas bind:this={canvasEl} width="400" height="400" class="bg-black rounded"></canvas>
      {:else}
        <p class="text-neutral-500 text-sm text-center max-w-xs">
          No canvas visualization for this operation yet — watch the live log on the right for its result and
          step trace. Build a dedicated visual here once you've settled on how it should look.
        </p>
      {/if}
    </div>

    <!-- Live log column: always visible, every message, every mode -->
    <div class="bg-black rounded-lg p-3 h-[26rem] overflow-y-auto font-mono text-xs flex flex-col gap-1">
      {#each $messageLog as msg}
        <div
          class:text-green-400={msg.type === 'stepEvent'}
          class:text-blue-400={msg.type === 'sessionReady'}
          class:text-amber-400={msg.type === 'operationComplete'}
          class:text-red-400={msg.type === 'error' || msg.type === 'operationFailed'}
          class:text-neutral-500={msg.type !== 'stepEvent' &&
            msg.type !== 'sessionReady' &&
            msg.type !== 'operationComplete' &&
            msg.type !== 'error' &&
            msg.type !== 'operationFailed'}
        >
          {formatLogEntry(msg)}
        </div>
      {/each}
      {#if $messageLog.length === 0}
        <p class="text-neutral-600">No messages yet — connect and run an operation.</p>
      {/if}
    </div>
  </section>
</main>