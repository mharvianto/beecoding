<script setup>
import Icon from './Icon.vue';
// Open/closed arrow for a collapsible header: points down when open, right when closed. Replaces the ▾ ▸ text glyphs, which
// render tiny and differently on every OS. `size` is the font size the icon scales from (the icon is 1.1em).
defineProps({ open: { type: Boolean, default: false }, size: { type: String, default: '1.25rem' } });
</script>

<template>
  <span class="inline-block leading-none transition-transform shrink-0" :class="open ? '' : '-rotate-90'" :style="{ fontSize: size }" aria-hidden="true"><Icon name="chevron" /></span>
</template>
