// Tiny dependency-free confetti — a few bursts popping in turn from different
// spots, fireworks-style. Self-cleaning canvas per burst. Honours
// prefers-reduced-motion and won't stack a whole new sequence if one is
// already on screen.

let running = false;

// Single burst: `originXFrac` (0..1) picks where along the width it pops.
function burst({ count, duration, originXFrac, onDone }) {
  const canvas = document.createElement('canvas');
  canvas.style.cssText = 'position:fixed;inset:0;width:100%;height:100%;pointer-events:none;z-index:9999';
  document.body.appendChild(canvas);
  const ctx = canvas.getContext('2d');
  const dpr = Math.min(2, window.devicePixelRatio || 1);
  const resize = () => {
    canvas.width = window.innerWidth * dpr;
    canvas.height = window.innerHeight * dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  };
  resize();
  window.addEventListener('resize', resize);

  const colors = ['#f59e0b', '#10b981', '#3b82f6', '#ef4444', '#a855f7', '#ec4899'];
  const ox = window.innerWidth * originXFrac;
  const oy = window.innerHeight * (0.28 + Math.random() * 0.14);
  const parts = Array.from({ length: count }, () => {
    const a = Math.random() * Math.PI * 2;
    const sp = 5 + Math.random() * 10;
    return {
      x: ox, y: oy,
      vx: Math.cos(a) * sp,
      vy: Math.sin(a) * sp - 7,
      size: 5 + Math.random() * 6,
      rot: Math.random() * Math.PI,
      vr: (Math.random() - 0.5) * 0.4,
      color: colors[(Math.random() * colors.length) | 0],
      rect: Math.random() < 0.5,
    };
  });

  const start = performance.now();
  const frame = (now) => {
    const t = now - start;
    ctx.clearRect(0, 0, window.innerWidth, window.innerHeight);
    const fade = t > duration - 700 ? Math.max(0, (duration - t) / 700) : 1;
    for (const p of parts) {
      p.vy += 0.3;
      p.vx *= 0.99;
      p.x += p.vx;
      p.y += p.vy;
      p.rot += p.vr;
      ctx.save();
      ctx.globalAlpha = fade;
      ctx.translate(p.x, p.y);
      ctx.rotate(p.rot);
      ctx.fillStyle = p.color;
      if (p.rect) ctx.fillRect(-p.size / 2, -p.size / 2, p.size, p.size * 0.6);
      else { ctx.beginPath(); ctx.arc(0, 0, p.size / 2, 0, Math.PI * 2); ctx.fill(); }
      ctx.restore();
    }
    if (t < duration) requestAnimationFrame(frame);
    else { window.removeEventListener('resize', resize); canvas.remove(); onDone?.(); }
  };
  requestAnimationFrame(frame);
}

// `force` runs even if a sequence is already on screen (e.g. a level-up
// landing on the same solve that already popped a smaller celebration).
// `waves` bursts pop one after another (every `stagger` ms) from different
// spots instead of a single explosion.
export function celebrate({ count = 150, duration = 2800, force = false, waves = 3, stagger = 260 } = {}) {
  if (typeof window === 'undefined') return;
  if (running && !force) return;
  if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return;
  if (!force) running = true;

  const perBurst = Math.max(20, Math.round(count / waves));
  const spots = [0.28, 0.72, 0.5, 0.18, 0.82];
  let pending = waves;
  for (let i = 0; i < waves; i++) {
    setTimeout(() => {
      burst({
        count: perBurst,
        duration,
        originXFrac: spots[i % spots.length],
        onDone: () => { if (--pending === 0 && !force) running = false; },
      });
    }, i * stagger);
  }
}
