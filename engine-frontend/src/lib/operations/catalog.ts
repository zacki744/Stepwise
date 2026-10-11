export type DatasetType = 'matrix' | 'graph' | 'tree';

export interface OperationDef {
  id: string;
  label: string;
  datasetType: DatasetType;
  exampleOptions: Record<string, unknown>;
}

export const operations: OperationDef[] = [
  // Matrix
  { id: 'fill-even-odd', label: 'Fill even/odd pattern', datasetType: 'matrix', exampleOptions: {} },
  { id: 'read-cell', label: 'Read cell', datasetType: 'matrix', exampleOptions: { row: 0, column: 0 } },
  { id: 'place-items', label: 'Place items (bin packing)', datasetType: 'matrix',
    exampleOptions: { items: [{ id: 1, width: 2, height: 2 }] } },
  { id: 'flood-fill', label: 'Flood fill', datasetType: 'matrix',
    exampleOptions: { row: 0, column: 0, newValue: 5 } },

  // Graph
  { id: 'reachability-count', label: 'Reachability count', datasetType: 'graph', exampleOptions: { startNode: 0 } },
  { id: 'generate-maze', label: 'Generate maze', datasetType: 'graph',
    exampleOptions: { width: 8, height: 8, seed: 42 } },
  { id: 'settle-debts', label: 'Settle debts', datasetType: 'graph', exampleOptions: {} },
  { id: 'minimum-spanning-tree', label: 'Minimum spanning tree', datasetType: 'graph', exampleOptions: {} },
  { id: 'shortest-path', label: 'Shortest path', datasetType: 'graph', exampleOptions: { start: 0, target: 1 } },
  { id: 'add-connection', label: 'Add connection', datasetType: 'graph',
    exampleOptions: { from: 0, to: 1, weight: 1.0 } },
  { id: 'remove-connection', label: 'Remove connection', datasetType: 'graph', exampleOptions: { from: 0, to: 1 } },
  { id: 'find-articulation-points', label: 'Find articulation points', datasetType: 'graph', exampleOptions: {} },
  { id: 'find-bridges', label: 'Find bridges', datasetType: 'graph', exampleOptions: {} },
  { id: 'find-connected-components', label: 'Find connected components', datasetType: 'graph', exampleOptions: {} },
  { id: 'isolate-node', label: 'Isolate node', datasetType: 'graph', exampleOptions: { nodeIndex: 0 } },

  // Tree
  { id: 'insert-node', label: 'Insert node', datasetType: 'tree', exampleOptions: { value: 50 } },
  { id: 'remove-node', label: 'Remove node', datasetType: 'tree', exampleOptions: { value: 50 } }
];

export const exampleInitialPayload: Record<DatasetType, Record<string, unknown>> = {
  matrix: { rows: 5, columns: 5 },
  graph: { nodeCount: 6, edges: [] },
  tree: {}
};