<script setup>
import { ref, watch, onMounted } from 'vue';
import QRCode from 'qrcode';

const props = defineProps({
  text: { type: String, required: true },
  size: { type: Number, default: 220 },
});

const canvas = ref(null);
const error = ref('');

async function render() {
  if (!canvas.value || !props.text) return;
  error.value = '';
  try {
    await QRCode.toCanvas(canvas.value, props.text, { width: props.size, margin: 1 });
  } catch (e) { error.value = e.message; }
}

onMounted(render);
watch(() => props.text, render);
</script>

<template>
  <div class="inline-block bg-white p-2 rounded-lg">
    <canvas ref="canvas" :width="size" :height="size"></canvas>
    <p v-if="error" class="text-xs text-red-600">{{ error }}</p>
  </div>
</template>
