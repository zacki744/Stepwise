import type { ClientMessage, UploadChunkMessage } from './messages';

/** Frames above this size are rejected by the server (1 MiB). Leave room for JSON escaping of the pieces. */
export const MAX_FRAME_BYTES = 1024 * 1024;

/** 200k characters stays well inside one frame even if every character needed escaping twice over. */
export const DEFAULT_CHUNK_CHARS = 200_000;

/**
 * True when the message would not fit in a single frame and must be sent with chunkMessage().
 * Measured in UTF-8 bytes, the way the server measures it.
 */
export function needsChunking(message: ClientMessage): boolean {
  return new TextEncoder().encode(JSON.stringify(message)).length > MAX_FRAME_BYTES;
}

/**
 * Splits a createSession/rehydrateSession message into ordered uploadChunk messages. The pieces are cut
 * from the serialised JSON text, never between the two halves of a surrogate pair, so each piece is valid
 * text on its own. Send them in order over the same socket; the server replies once, after the last one,
 * exactly as it would for the unsplit message.
 */
export function chunkMessage(message: ClientMessage, chunkChars = DEFAULT_CHUNK_CHARS): UploadChunkMessage[] {
  if (message.type !== 'createSession' && message.type !== 'rehydrateSession') {
    throw new Error('Only createSession and rehydrateSession messages can be uploaded in chunks');
  }

  const text = JSON.stringify(message);
  const uploadId = crypto.randomUUID();
  const pieces: string[] = [];

  for (let start = 0; start < text.length; ) {
    let end = Math.min(start + chunkChars, text.length);
    const last = text.charCodeAt(end - 1);
    // Don't end a piece on the first half of a surrogate pair.
    if (end < text.length && last >= 0xd800 && last <= 0xdbff) end -= 1;
    pieces.push(text.slice(start, end));
    start = end;
  }

  return pieces.map((data, index) => ({
    type: 'uploadChunk',
    uploadId,
    index,
    total: pieces.length,
    data
  }));
}
