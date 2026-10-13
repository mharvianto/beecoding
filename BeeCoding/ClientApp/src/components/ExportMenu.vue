<script setup>
import { downloadFile } from '../lib/download';
import RowMenu from './RowMenu.vue';

// A "⬇" menu offering the staff downloads for one scope of a board: a single problem (`problem` = its slug), a
// group (`group` = its id, 0 for problems without a group) or, with neither, the whole board. Errors are
// reported to the parent through the `error` event.
const props = defineProps({
  slug: { type: String, required: true },
  problem: { type: String, default: '' },
  group: { type: Number, default: null },
  label: { type: String, default: 'Download' },
});
const emit = defineEmits(['error']);

async function get(path, params, fallback) {
  const q = new URLSearchParams(params);
  if (props.problem) q.set('problem', props.problem);
  else if (props.group != null) q.set('group', String(props.group));
  const query = q.toString();
  try { await downloadFile(`/api/boards/${props.slug}/export/${path}${query ? '?' + query : ''}`, fallback); }
  catch (e) { emit('error', e.message); }
}
</script>

<template>
  <span class="inline-flex">
    <RowMenu :label="label" icon="⬇">
      <button class="row-menu-item" @click="get('results.xlsx', {}, 'results.xlsx')">📊 Results (Excel)</button>
      <button class="row-menu-item" @click="get('answers.zip', { pick: 'best' }, 'answers.zip')">📁 Answers (ZIP) — best attempt</button>
      <button class="row-menu-item" @click="get('answers.zip', { pick: 'latest' }, 'answers.zip')">📁 Answers (ZIP) — latest attempt</button>
    </RowMenu>
  </span>
</template>
