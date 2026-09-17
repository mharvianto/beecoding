<script setup>
import { ref, onMounted, onBeforeUnmount, watch } from 'vue';
import { Chart, BarController, BarElement, LinearScale, CategoryScale, Tooltip } from 'chart.js';
import ChartDataLabels from 'chartjs-plugin-datalabels';
import { theme as appTheme } from '../lib/theme';

Chart.register(BarController, BarElement, LinearScale, CategoryScale, ChartDataLabels, Tooltip);

// Ranking / magnitude chart — one sequential hue, values direct-labeled (no hover
// layer needed since nothing is hidden behind it), built on Chart.js.
const props = defineProps({
  items: { type: Array, required: true },   // [{ label, value, rate }] — rate is 0..1
  color: { type: String, default: '#f59e0b' },
});

const canvasEl = ref(null);
let chart = null;

function isDark() {
  return appTheme.value === 'dark'
    || (appTheme.value === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
}
function tickColor() { return isDark() ? '#94a3b8' : '#64748b'; }
// Recessive, one-step-off-surface hairlines — same tokens as the card border utilities.
function gridColor() { return isDark() ? '#1e293b' : '#e2e8f0'; }
function tooltipBg() { return isDark() ? '#1e293b' : '#ffffff'; }
function tooltipInk() { return isDark() ? '#e2e8f0' : '#0f172a'; }

const fmt = (n) => (n ?? 0).toLocaleString();

function build() {
  if (!canvasEl.value || !props.items.length) { chart?.destroy(); chart = null; return; }
  chart?.destroy();
  chart = new Chart(canvasEl.value, {
    type: 'bar',
    data: {
      labels: props.items.map((i) => i.label),
      datasets: [{
        data: props.items.map((i) => i.value),
        backgroundColor: props.color,
        borderRadius: 3,
        barThickness: 14,
        maxBarThickness: 16,
      }],
    },
    options: {
      indexAxis: 'y',
      responsive: true,
      maintainAspectRatio: false,
      animation: false,
      layout: { padding: { right: 56 } },
      scales: {
        x: {
          beginAtZero: true,
          grid: { color: gridColor() },
          border: { display: false },
          ticks: { color: tickColor(), font: { size: 10 }, maxTicksLimit: 4, precision: 0 },
        },
        y: {
          grid: { display: false },
          ticks: { autoSkip: false, color: tickColor(), font: { size: 11 } },
        },
      },
      plugins: {
        legend: { display: false },
        tooltip: {
          enabled: true,
          displayColors: false,
          backgroundColor: tooltipBg(),
          borderColor: gridColor(),
          borderWidth: 1,
          titleColor: tooltipInk(),
          bodyColor: tooltipInk(),
          padding: 8,
          cornerRadius: 8,
          titleFont: { size: 13, weight: 'bold' },
          bodyFont: { size: 11, weight: 'normal' },
          callbacks: {
            title: (items) => items[0]?.label,
            label: (item) => {
              const rate = props.items[item.dataIndex]?.rate ?? 0;
              return `${fmt(item.raw)} attempts · ${Math.round(rate * 100)}% accepted`;
            },
          },
        },
        datalabels: {
          anchor: 'end',
          align: 'end',
          color: tickColor(),
          font: { size: 10, weight: 'normal' },
          formatter: (value, ctx) => {
            const rate = props.items[ctx.dataIndex]?.rate ?? 0;
            return `${fmt(value)}  ${Math.round(rate * 100)}%`;
          },
        },
      },
    },
  });
}

onMounted(build);
watch(() => [props.items, props.color], build, { deep: true });
watch(appTheme, build);
onBeforeUnmount(() => chart?.destroy());
</script>

<template>
  <div>
    <div :style="{ height: Math.max(90, items.length * 26) + 'px' }">
      <canvas ref="canvasEl"></canvas>
    </div>
    <p v-if="!items.length" class="text-slate-400 dark:text-slate-500 text-xs">No data yet.</p>
  </div>
</template>
