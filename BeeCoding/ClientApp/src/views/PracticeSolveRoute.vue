<script setup>
import PracticeSolve from './PracticeSolve.vue';

// The router reuses a component when only the :slug changes (a "What next?" link, say), but PracticeSolve loads its problem,
// draft, editor and live connection once when it is created. Keying on the slug makes every problem a fresh page.
defineProps({ slug: { type: String, required: true } });
</script>

<template>
  <PracticeSolve :key="slug" :slug="slug" />
</template>
