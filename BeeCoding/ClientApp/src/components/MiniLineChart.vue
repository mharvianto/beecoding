<script setup>
import { ref, onMounted, onBeforeUnmount, watch } from 'vue';
import {
  Chart, LineController, LineElement, PointElement, LinearScale, CategoryScale, Filler, Tooltip,
} from 'chart.js';
import { theme as appTheme } from '../lib/theme';

Chart.register(LineController, LineElement, PointElement, LinearScale, CategoryScale, Filler, Tooltip);

// A small single-series trend chart (area + line), built on Chart.js. Two measures
// of different scale (e.g. active users vs. submissions) render as two of these
// side by side rather than one dual-axis chart — see dataviz guidance: never a
// second y-axis.
const props = defineProps({
  title: { type: String, required: true },
  points: { type: Array, required: true },   // [{ label: string, value: number }]
  color: { type: String, default: '#f59e0b' },   // amber-500, the app's existing accent
});

const canvasEl = ref(null);
const hoverIdx = ref(null);
let chart = null;

function isDark() {
  return appTheme.value === 'dark'
    || (appTheme.value === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
}
// Recessive, one-step-off-surface hairlines — same tokens as the card border utilities
// (border-slate-200 / dark:border-slate-800) so gridlines read as structure, not data.
function gridColor() { return isDark() ? '#1e293b' : '#e2e8f0'; }
function tickColor() { return isDark() ? '#94a3b8' : '#64748b'; }
function tooltipBg() { return isDark() ? '#1e293b' : '#ffffff'; }
function tooltipInk() { return isDark() ? '#e2e8f0' : '#0f172a'; }

// A vertical hairline at the hovered/nearest X — "the crosshair finds the X" (dataviz).
const crosshairPlugin = {
  id: 'crosshair',
  afterDraw(c) {
    const active = c.getActiveElements();
    if (!active.length) return;
    const { ctx, chartArea } = c;
    const x = active[0].element.x;
    ctx.save();
    ctx.beginPath();
    ctx.moveTo(x, chartArea.top);
    ctx.lineTo(x, chartArea.bottom);
    ctx.lineWidth = 1;
    ctx.strokeStyle = gridColor();
    ctx.stroke();
    ctx.restore();
  },
};

const fmt = (n) => (n ?? 0).toLocaleString();

function build() {
  if (!canvasEl.value) return;
  chart?.destroy();
  const last = props.points.length - 1;
  chart = new Chart(canvasEl.value, {
    type: 'line',
    plugins: [crosshairPlugin],
    data: {
      labels: props.points.map((p) => p.label),
      datasets: [{
        data: props.points.map((p) => p.value),
        borderColor: props.color,
        backgroundColor: props.color + '1a',
        fill: true,
        tension: 0.25,
        borderWidth: 2,
        pointRadius: (ctx) => (ctx.dataIndex === last ? 4 : 0),
        pointHoverRadius: 4,
        pointBackgroundColor: props.color,
        pointBorderColor: isDark() ? '#0f172a' : '#fff',
        pointBorderWidth: 2,
        pointHitRadius: 12,
      }],
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      animation: false,
      interaction: { intersect: false, mode: 'index' },
      onHover: (_evt, elements) => { hoverIdx.value = elements.length ? elements[0].index : null; },
      scales: {
        x: {
          grid: { display: false },
          border: { color: gridColor() },
          ticks: { color: tickColor(), font: { size: 10 }, autoSkip: true, maxTicksLimit: 4, maxRotation: 0 },
        },
        y: {
          beginAtZero: true,
          grid: { color: gridColor() },
          border: { display: false },
          ticks: { color: tickColor(), font: { size: 10 }, maxTicksLimit: 3, precision: 0 },
        },
      },
      plugins: {
        legend: { display: false },
        // chartjs-plugin-datalabels registers itself globally (TopicBarChart uses
        // it) and would otherwise try to label every point on this chart too.
        datalabels: { display: false },
        tooltip: {
          enabled: true,
          displayColors: false,   // single series — the title already names it (dataviz)
          backgroundColor: tooltipBg(),
          borderColor: gridColor(),
          borderWidth: 1,
          titleColor: tooltipInk(),
          bodyColor: tooltipInk(),
          padding: 8,
          cornerRadius: 8,
          titleFont: { size: 13, weight: 'bold' },
          bodyFont: { size: 11, weight: 'normal' },
          // Values lead, labels follow: the bold title slot carries the number,
          // the plain body slot carries the date/time it happened.
          callbacks: {
            title: (items) => fmt(items[0]?.raw),
            label: (item) => item.label,
          },
        },
      },
    },
  });
}

onMounted(build);
watch(() => [props.points, props.color], build, { deep: true });
watch(appTheme, build);
onBeforeUnmount(() => chart?.destroy());

const lastPoint = () => props.points[props.points.length - 1];
const activePoint = () => (hoverIdx.value === null ? lastPoint() : props.points[hoverIdx.value]);
</script>

<template>
  <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
    <div class="flex items-baseline justify-between mb-1">
      <div class="text-xs text-slate-400 dark:text-slate-500">{{ title }}</div>
      <div class="text-sm font-semibold tabular-nums">{{ fmt(activePoint()?.value) }}</div>
    </div>
    <div class="h-32">
      <canvas ref="canvasEl" @mouseleave="hoverIdx = null"></canvas>
    </div>
  </div>
</template>
