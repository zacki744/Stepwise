interface PassageCarvedPayload {
  from: number;
  to: number;
}

export interface MazeDimensions {
  width: number;
  height: number;
}

function cellCenter(node: number, width: number, cellSize: number): { x: number; y: number } {
  const row = Math.floor(node / width);
  const col = node % width;
  return { x: col * cellSize + cellSize / 2, y: row * cellSize + cellSize / 2 };
}

export function drawMazeGrid(ctx: CanvasRenderingContext2D, { width, height }: MazeDimensions): void {
  const cellSize = Math.min(ctx.canvas.width / width, ctx.canvas.height / height);
  ctx.strokeStyle = '#333';
  for (let i = 0; i <= width; i++) {
    ctx.beginPath();
    ctx.moveTo(i * cellSize, 0);
    ctx.lineTo(i * cellSize, height * cellSize);
    ctx.stroke();
  }
  for (let j = 0; j <= height; j++) {
    ctx.beginPath();
    ctx.moveTo(0, j * cellSize);
    ctx.lineTo(width * cellSize, j * cellSize);
    ctx.stroke();
  }
}

export function drawMazePassage(
  ctx: CanvasRenderingContext2D,
  dimensions: MazeDimensions,
  payload: PassageCarvedPayload
): void {
  const cellSize = Math.min(ctx.canvas.width / dimensions.width, ctx.canvas.height / dimensions.height);
  const a = cellCenter(payload.from, dimensions.width, cellSize);
  const b = cellCenter(payload.to, dimensions.width, cellSize);

  ctx.strokeStyle = '#6fcf6f';
  ctx.lineWidth = Math.max(2, cellSize * 0.4);
  ctx.beginPath();
  ctx.moveTo(a.x, a.y);
  ctx.lineTo(b.x, b.y);
  ctx.stroke();
}