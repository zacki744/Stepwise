interface TransactionCreatedPayload {
  from: number;
  to: number;
  amount: number;
}

export function layoutNodesInCircle(canvas: HTMLCanvasElement, nodeCount: number): { x: number; y: number }[] {
  const cx = canvas.width / 2;
  const cy = canvas.height / 2;
  const radius = Math.min(cx, cy) - 40;

  return Array.from({ length: nodeCount }, (_, i) => {
    const angle = (i / nodeCount) * 2 * Math.PI - Math.PI / 2;
    return { x: cx + radius * Math.cos(angle), y: cy + radius * Math.sin(angle) };
  });
}

export function drawDebtNodes(ctx: CanvasRenderingContext2D, positions: { x: number; y: number }[]): void {
  positions.forEach((pos, i) => {
    ctx.fillStyle = '#0e639c';
    ctx.beginPath();
    ctx.arc(pos.x, pos.y, 16, 0, 2 * Math.PI);
    ctx.fill();
    ctx.fillStyle = '#fff';
    ctx.font = '12px sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(String(i), pos.x, pos.y);
  });
}

export function drawTransaction(
  ctx: CanvasRenderingContext2D,
  positions: { x: number; y: number }[],
  payload: TransactionCreatedPayload
): void {
  const a = positions[payload.from];
  const b = positions[payload.to];
  if (!a || !b) return;

  ctx.strokeStyle = '#e0a020';
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(a.x, a.y);
  ctx.lineTo(b.x, b.y);
  ctx.stroke();

  ctx.fillStyle = '#e0a020';
  ctx.font = '11px sans-serif';
  ctx.fillText(payload.amount.toFixed(2), (a.x + b.x) / 2, (a.y + b.y) / 2 - 8);
}