// Mirrors Engine.Protocol/Messages.cs. Field names are camelCase to match
// ProtocolJson.Options' CamelCase naming policy on the backend.

// ---------- Client → server ----------

export type ClientMessage =
  | CreateSessionMessage
  | RehydrateSessionMessage
  | ExecuteOperationMessage;

export interface CreateSessionMessage {
  type: 'createSession';
  datasetType: string;
  initialPayload: unknown;
}

export interface LoggedOperationDto {
  operationId: string;
  options: Record<string, unknown>;
}

export interface RehydrateSessionMessage {
  type: 'rehydrateSession';
  datasetType: string;
  initialPayload: unknown;
  opLog: LoggedOperationDto[];
}

export interface ExecuteOperationMessage {
  type: 'executeOperation';
  requestId: string;
  operationId: string;
  options: Record<string, unknown>;
}

// ---------- Server → client ----------

export type ServerMessage =
  | SessionReadyMessage
  | StepEventMessage
  | OperationCompleteMessage
  | OperationFailedMessage
  | ErrorMessage;

export interface SessionReadyMessage {
  type: 'sessionReady';
  sessionId: string;
}

export interface StepEventMessage {
  type: 'stepEvent';
  stepType: string;
  description: string;
  payload: unknown | null;
}

export interface DatasetSnapshot {
  datasetType: string;
  payload: unknown;
}

export interface OperationCompleteMessage {
  type: 'operationComplete';
  requestId: string;
  resultSummary: unknown | null;
  // Present only when the trace was coalesced AND the operation committed a new dataset
  // AND the snapshot fit the size cap. Absent after a TraceCoalesced means "too large to send".
  finalState?: DatasetSnapshot | null;
}

export interface OperationFailedMessage {
  type: 'operationFailed';
  requestId: string;
  reason: string;
}

export interface ErrorMessage {
  type: 'error';
  code: string;
  message: string;
}

// Narrowing helper — avoids repeating `message.type === '...'` checks with
// manual casts scattered across every consumer.
export function isStepEvent(msg: ServerMessage): msg is StepEventMessage {
  return msg.type === 'stepEvent';
}