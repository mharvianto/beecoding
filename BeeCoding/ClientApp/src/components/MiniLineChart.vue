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

function build() {
  if (!canvasEl.value) return;
  chart?.destroy();
  const last = props.points.length - 1;
  chart = new Chart(canvasEl.value, {
    type: 'line',
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
        x: { display: false, ticks: { display: false }, grid: { display: false } },
        y: { display: false, beginAtZero: true, ticks: { display: false }, grid: { display: false } },
      },
      plugins: {
        legend: { display: false },
        tooltip: { enabled: false },
        // chartjs-plugin-datalabels registers itself globally (TopicBarChart uses
        // it) and would otherwise try to label every point on this chart too.
        datalabels: { display: false },
      },
    },
  });
}

onMounted(build);
watch(() => [props.points, props.color], build, { deep: true });
watch(appTheme, build);
onBeforeUnmount(() => chart?.destroy());

const fmt = (n) => (n ?? 0).toLocaleString();
const lastPoint = () => props.points[props.points.length - 1];
const activePoint = () => (hoverIdx.value === null ? lastPoint() : props.points[hoverIdx.value]);
</script>

<template>
  <div class="border border-slate-200 dark:border-slate-800 rounded-xl p-3">
    <div class="flex items-baseline justify-between mb-1">
      <div class="text-xs text-slate-400 dark:text-slate-500">{{ title }}</div>
      <div class="text-sm font-semibold tabular-nums">{{ fmt(activePoint()?.value) }}</div>
    </div>
    <div class="h-20">
      <canvas ref="canvasEl" @mouseleave="hoverIdx = null"></canvas>
    </div>
    <div class="flex items-center justify-between text-[10px] text-slate-400 dark:text-slate-500 mt-0.5">
      <span>{{ points[0]?.label }}</span>
      <span>{{ activePoint()?.label }}</span>
    </div>
  </div>
</template>
