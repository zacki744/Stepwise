import { writable, type Writable } from 'svelte/store';
import type { ClientMessage, ServerMessage } from '../protocol/messages';
import { chunkMessage, needsChunking } from '../protocol/upload';

export type ConnectionStatus = 'disconnected' | 'connecting' | 'connected';

export const connectionStatus: Writable<ConnectionStatus> = writable('disconnected');

// The single source of truth for the current session: everything that
// reads sessionId reacts automatically when a new session is created or
// rehydrated, without any component needing to track it manually.
export const sessionId: Writable<string | null> = writable(null);

// Every ServerMessage ever received, in order — this is the frontend's own
// append-only log, independent of the backend's trace/op-log. It's what
// the change-log UI (and eventually undo/redo) will read from.
export const messageLog: Writable<ServerMessage[]> = writable([]);

// The most recent message alone, for anything that only cares about "what
// just happened" rather than the full history (e.g. canvas draw calls).
export const latestMessage: Writable<ServerMessage | null> = writable(null);

let socket: WebSocket | null = null;

export function connect(url: string): void {
  if (socket) {
    socket.close();
  }

  connectionStatus.set('connecting');
  socket = new WebSocket(url);

  socket.onopen = () => {
    connectionStatus.set('connected');
  };

  socket.onclose = () => {
    connectionStatus.set('disconnected');
    sessionId.set(null);
  };

  socket.onerror = () => {
    // The browser's WebSocket error event carries no useful detail by
    // design (security reasons) — onclose fires right after this with
    // whatever information is actually available, so there's nothing
    // meaningful to surface here beyond letting that handler run.
  };

  socket.onmessage = (event: MessageEvent<string>) => {
    const message = JSON.parse(event.data) as ServerMessage;
    if (message.type === 'error') {
        statusMessage.set({ kind: 'error', text: `${message.code}: ${message.message}` });
    }
    if (message.type === 'operationFailed') {
        statusMessage.set({ kind: 'operation-failed', text: message.reason });
    }
    if (message.type === 'sessionReady') {
        sessionId.set(message.sessionId);
        statusMessage.set(null);
    }
    if (message.type === 'operationComplete') {
        statusMessage.set(null);
    }
    messageLog.update((log) => [...log, message]);
    latestMessage.set(message);

    if (message.type === 'sessionReady') {
      sessionId.set(message.sessionId);
    }
  };
}

export function send(message: ClientMessage): void {
  if (!socket || socket.readyState !== WebSocket.OPEN) {
    throw new Error('Cannot send: socket is not connected');
  }
  socket.send(JSON.stringify(message));
}

/** Like send(), but splits createSession/rehydrateSession messages that are too big for one frame. */
export function sendLarge(message: ClientMessage): void {
  if (!needsChunking(message)) {
    send(message);
    return;
  }
  for (const chunk of chunkMessage(message)) send(chunk);
}

export function disconnect(): void {
  socket?.close();
  socket = null;
}
export interface StatusMessage {
  kind: 'error' | 'operation-failed';
  text: string;
}

export const statusMessage: Writable<StatusMessage | null> = writable(null);