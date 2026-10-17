// Mirrors Engine.Gateway/OperationDescriptors.cs. GET /operations is the source of truth for what
// the backend can do; this replaces the hand-kept list in catalog.ts once the UI is rebuilt on it.

export type OptionKind =
  | 'int' // plain integer
  | 'number' // any number
  | 'bool'
  | 'choice' // one of `choices`
  | 'row' // a grid row index
  | 'column' // a grid column index
  | 'node' // a graph node index
  | 'nodeList' // array of node indices
  | 'seed' // random seed
  | 'itemList'; // array of {id, width, height}

export interface OptionDescriptor {
  name: string;
  kind: OptionKind;
  label: string;
  required: boolean;
  /** Value the server uses when the option is left out. null = "not set". */
  default: unknown | null;
  /** Only for kind 'choice'. */
  choices: string[] | null;
}

export interface OperationDescriptor {
  id: string;
  datasetType: 'matrix' | 'graph' | 'tree';
  label: string;
  category: string;
  description: string;
  options: OptionDescriptor[];
  /** A complete valid options object, including every optional key. */
  exampleOptions: Record<string, unknown>;
}

export async function fetchOperations(httpBase: string): Promise<OperationDescriptor[]> {
  const response = await fetch(`${httpBase}/operations`);
  if (!response.ok) throw new Error(`GET /operations failed: ${response.status}`);
  return (await response.json()) as OperationDescriptor[];
}
