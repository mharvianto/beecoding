<script setup>
import { computed } from 'vue';

// Storage taken by statement pictures: totals, then the heaviest uploaders. Shared by Admin (platform-wide) and
// Org admin (that organization's members). `usage` is an ImageUsageDto from /image-usage.
const props = defineProps({ usage: { type: Object, default: null }, title: { type: String, default: 'Statement picture storage' } });

const fmt = (b) => {
  if (b >= 1024 * 1024 * 1024) return (b / 1024 / 1024 / 1024).toFixed(2) + ' GB';
  if (b >= 1024 * 1024) return (b / 1024 / 1024).toFixed(1) + ' MB';
  if (b >= 1024) return Math.round(b / 1024) + ' KB';
  return b + ' B';
};
const widest = computed(() => Math.max(1, ...(props.usage?.top ?? []).map((u) => u.bytes)));
</script>

<template>
  <div v-if="usage" class="mt-4 pt-3 border-t border-slate-200 dark:border-slate-800">
    <h3 class="font-semibold text-xs text-slate-500 dark:text-slate-400 mb-2">
      {{ title }} <span class="font-normal text-slate-400 dark:text-slate-500">— kept in the database</span>
    </h3>

    <div class="grid grid-cols-2 sm:grid-cols-4 gap-2 mb-3">
      <div v-for="[label, value, hint] in [
        ['Pictures', usage.images, ''],
        ['Storage', fmt(usage.bytes), ''],
        ['Unused', fmt(usage.unusedBytes), usage.unused + ' picture' + (usage.unused === 1 ? '' : 's') + ', cleaned automatically'],
        ['Uploaders', usage.users, ''],
      ]" :key="label" class="rounded-lg border border-slate-200 dark:border-slate-800 px-3 py-2">
        <div class="text-[11px] text-slate-400 dark:text-slate-500">{{ label }}</div>
        <div class="text-lg font-semibold leading-tight">{{ value }}</div>
        <div v-if="hint" class="text-[10px] text-slate-400 dark:text-slate-500">{{ hint }}</div>
      </div>
    </div>

    <p v-if="!usage.top.length" class="text-sm text-slate-400 dark:text-slate-500">No pictures uploaded yet.</p>
    <div v-else class="overflow-x-auto">
      <table class="w-full text-sm">
        <thead>
          <tr class="text-left text-[11px] text-slate-400 dark:text-slate-500">
            <th class="font-medium py-1 pr-3">Uploader</th>
            <th class="font-medium py-1 pr-3 text-right">Pictures <span class="font-normal">/ limit {{ usage.limitPerUser }}</span></th>
            <th class="font-medium py-1 pr-3 w-48">Size</th>
            <th class="font-medium py-1 text-right">Unused</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="u in usage.top" :key="u.userId" class="border-t border-slate-100 dark:border-slate-800/70">
            <td class="py-1.5 pr-3 min-w-0">
              <div class="truncate">{{ u.displayName || u.email }}</div>
              <div v-if="u.displayName" class="text-[11px] text-slate-400 dark:text-slate-500 truncate">{{ u.email }}</div>
            </td>
            <td class="py-1.5 pr-3 text-right tabular-nums"
                :class="usage.limitPerUser > 0 && u.images >= usage.limitPerUser * 0.9 ? 'text-amber-600 dark:text-amber-400 font-medium' : ''">{{ u.images }}</td>
            <td class="py-1.5 pr-3">
              <div class="flex items-center gap-2">
                <div class="flex-1 h-1.5 rounded-full bg-slate-100 dark:bg-slate-800 overflow-hidden">
                  <div class="h-full bg-amber-400 dark:bg-amber-500" :style="{ width: Math.max(2, (u.bytes / widest) * 100) + '%' }"></div>
                </div>
                <span class="w-16 text-right tabular-nums text-xs">{{ fmt(u.bytes) }}</span>
              </div>
            </td>
            <td class="py-1.5 text-right tabular-nums text-xs text-slate-400 dark:text-slate-500">{{ u.unused ? fmt(u.unusedBytes) : '—' }}</td>
          </tr>
        </tbody>
      </table>
    </div>
    <p class="text-[11px] text-slate-400 dark:text-slate-500 mt-2">
      Showing the {{ usage.top.length }} heaviest uploader{{ usage.top.length === 1 ? '' : 's' }}. Each picture is at most {{ usage.maxKb }} KB.
      Deleting pictures frees space inside the database file; the file itself only shrinks after a <code>VACUUM</code>.
    </p>
  </div>
</template>
