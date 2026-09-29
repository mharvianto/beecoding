// Tiny dependency-free confetti — a few bursts popping in turn from different
// spots, fireworks-style. Self-cleaning canvas per burst. Honours
// prefers-reduced-motion and won't stack a whole new sequence if one is
// already on screen.

let running = false;

const RAINBOW = ['#f59e0b', '#10b981', '#3b82f6', '#ef4444', '#a855f7', '#ec4899'];

// Solve celebrations scale with the problem's level: colder/smaller for Easy, gold stars and
// more, longer waves for Hard. Anything else (and the level-up) keeps the full rainbow.
const LEVEL_STYLE = {
  Easy:   { colors: ['#10b981', '#34d399', '#14b8a6', '#5eead4', '#a7f3d0'], count: 80, waves: 1, duration: 2200, stars: false },
  Medium: { colors: ['#3b82f6', '#60a5fa', '#8b5cf6', '#a78bfa', '#06b6d4'], count: 150, waves: 2, duration: 2800, stars: false },
  Hard:   { colors: ['#f59e0b', '#fbbf24', '#f97316', '#fde047', '#ef4444'], count: 220, waves: 4, duration: 3600, stars: true },
};

// Single burst: `originXFrac` (0..1) picks where along the width it pops.
function burst({ count, duration, originXFrac, colors, stars, onDone }) {
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
      rect: !stars && Math.random() < 0.5,
      star: stars && Math.random() < 0.6,
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
      if (p.star) {
        const r = p.size * 0.8;
        ctx.beginPath();
        for (let k = 0; k < 10; k++) {
          const rad = k % 2 ? r * 0.45 : r;
          const ang = (Math.PI / 5) * k - Math.PI / 2;
          ctx[k ? 'lineTo' : 'moveTo'](Math.cos(ang) * rad, Math.sin(ang) * rad);
        }
        ctx.closePath(); ctx.fill();
      } else if (p.rect) ctx.fillRect(-p.size / 2, -p.size / 2, p.size, p.size * 0.6);
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
// spots instead of a single explosion. `level` (Easy/Medium/Hard) picks a preset palette and
// scale; `bonus` adds extra waves on top (e.g. for a busy solving day), capped at 8.
export function celebrate({ level, bonus = 0, count, duration, force = false, waves, stagger = 260 } = {}) {
  const style = LEVEL_STYLE[level];
  const colors = style?.colors || RAINBOW;
  const stars = !!style?.stars;
  count ??= style?.count ?? 150;
  duration ??= style?.duration ?? 2800;
  waves ??= Math.min(8, (style?.waves ?? 3) + bonus);
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
        colors,
        stars,
        onDone: () => { if (--pending === 0 && !force) running = false; },
      });
    }, i * stagger);
  }
}
