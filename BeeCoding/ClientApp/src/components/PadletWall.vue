<script setup>
import { ref, watch, computed, onMounted } from 'vue';
import { api } from '../lib/api';
import VerdictBadge from './VerdictBadge.vue';
import SubmissionView from './SubmissionView.vue';

const props = defineProps({
  boardSlug: { type: String, required: true },
  currentUserId: Number,
  refreshSignal: { type: Number, default: 0 },
  drafts: { type: Object, default: () => ({}) },   // "problemId:userId" -> { code, updatedAt, authorName }
});
const wall = ref({ problems: [], posts: [], examMode: false, viewerIsStaff: false });
const activeProblem = ref(null);
const noteDraft = ref({});          // postId -> string while editing
const commentDraft = ref({});       // postId -> string
const openComments = ref({});       // postId -> bool
const error = ref('');
const viewSubmissionId = ref(null); // set to open the full-code viewer modal

const EMOJIS = ['👍', '⭐', '🎉', '🔥', '👀'];

async function load() {
  try {
    wall.value = await api.get(`/api/boards/${props.boardSlug}/wall`);
    if (!activeProblem.value && wall.value.problems.length)
      activeProblem.value = wall.value.problems[0].id;
  } catch (e) { error.value = e.message; }
}
onMounted(load);
watch(() => props.refreshSignal, load);

const visiblePosts = computed(() => {
  const posts = wall.value.posts.filter((p) => p.problemId === activeProblem.value);
  const seen = new Set(posts.map((p) => `${p.problemId}:${p.userId}`));
  // Staff may receive live drafts for students who have no post row yet.
  const draftOnly = Object.entries(props.drafts)
    .filter(([k]) => k.startsWith(`${activeProblem.value}:`) && !seen.has(k))
    .map(([k, d]) => {
      const [problemId, userId] = k.split(':').map(Number);
      return {
        postId: null, problemId, userId, authorName: d.authorName, note: '', mine: userId === props.currentUserId,
        redacted: false, verdict: 'None', score: 0, attempts: 0, runtimeMs: 0, memoryKb: 0,
        language: 'cpp', codePreview: null, updatedAt: d.updatedAt, reactions: [], comments: [],
      };
    });
  return [...posts, ...draftOnly];
});

function draftFor(post) {
  const d = props.drafts[`${post.problemId}:${post.userId}`];
  if (!d || post.mine || post.redacted) return null;
  return d;
}
function truncate(code, n = 14) {
  const lines = (code || '').split('\n');
  return lines.length > n ? lines.slice(0, n).join('\n') + '\n…' : lines.join('\n');
}
function previewCode(post) {
  const d = draftFor(post);
  return d ? truncate(d.code) : post.codePreview;
}

const pastels = [
  'bg-rose-50 dark:bg-rose-500/10',
  'bg-amber-50 dark:bg-amber-500/10',
  'bg-lime-50 dark:bg-lime-500/10',
  'bg-sky-50 dark:bg-sky-500/10',
  'bg-violet-50 dark:bg-violet-500/10',
  'bg-teal-50 dark:bg-teal-500/10',
  'bg-orange-50 dark:bg-orange-500/10',
];
const avatarColors = ['bg-rose-400', 'bg-amber-400', 'bg-lime-500', 'bg-sky-400', 'bg-violet-400', 'bg-teal-400', 'bg-orange-400'];
function hash(n) { return ((n * 2654435761) >>> 0) % pastels.length; }
function initials(name) {
  return (name || '?').split(/\s+/).slice(0, 2).map((w) => w[0]?.toUpperCase() || '').join('');
}
function ago(iso) {
  if (!iso) return '';
  const t = Date.parse(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : iso + 'Z');
  if (!t) return '';
  const s = Math.max(1, Math.floor((Date.now() - t) / 1000));
  if (s < 60) return 'just now';
  if (s < 3600) return Math.floor(s / 60) + 'm';
  if (s < 86400) return Math.floor(s / 3600) + 'h';
  return Math.floor(s / 86400) + 'd';
}

async function toggleReaction(post, emoji) {
  try {
    post.reactions = await api.post(`/api/posts/${post.postId}/reactions`, { emoji });
  } catch (e) { error.value = e.message; }
}
function reactionCount(post, emoji) {
  return post.reactions.find((r) => r.emoji === emoji);
}

function startNote(post) { noteDraft.value[post.postId] = post.note || ''; }
async function saveNote(post) {
  try {
    await api.put(`/api/posts/${post.postId}/note`, { note: noteDraft.value[post.postId] });
    post.note = noteDraft.value[post.postId].trim();
    delete noteDraft.value[post.postId];
  } catch (e) { error.value = e.message; }
}

async function addComment(post) {
  const body = (commentDraft.value[post.postId] || '').trim();
  if (!body) return;
  try {
    const c = await api.post(`/api/posts/${post.postId}/comments`, { body });
    post.comments.push(c);
    commentDraft.value[post.postId] = '';
  } catch (e) { error.value = e.message; }
}
async function delComment(post, c) {
  try {
    await api.del(`/api/posts/${post.postId}/comments/${c.id}`);
    post.comments = post.comments.filter((x) => x.id !== c.id);
  } catch (e) { error.value = e.message; }
}

function openPost(post) {
  if (post.latestSubmissionId) { viewSubmissionId.value = post.latestSubmissionId; return; }
  error.value = 'No submission yet for this problem.';
}

async function toggleHiddenByStudent(post) {
  try {
    await api.patch(`/api/posts/${post.postId}/visibility`, { hiddenByStudent: !post.hiddenByStudent });
    post.hiddenByStudent = !post.hiddenByStudent;
  } catch (e) { error.value = e.message; }
}
</script>

<template>
  <div>
    <p v-if="error" class="text-sm text-red-600 dark:text-red-400 mb-2">{{ error }}</p>

    <!-- problem tabs -->
    <div class="flex gap-1 flex-wrap mb-4">
      <button v-for="p in wall.problems" :key="p.id" @click="activeProblem = p.id"
              class="px-3 py-1.5 rounded-full text-sm border transition"
              :class="activeProblem === p.id
                ? 'bg-amber-500 text-white border-amber-500'
                : 'bg-white dark:bg-slate-900 text-slate-600 dark:text-slate-300 border-slate-200 dark:border-slate-700 hover:border-amber-300'">
        {{ p.title }}
      </button>
    </div>

    <div v-if="wall.examMode && !wall.viewerIsStaff"
         class="text-sm bg-purple-50 text-purple-700 dark:bg-purple-500/10 dark:text-purple-300 rounded-lg px-3 py-2 mb-3">
      🔒 Exam mode — only your own posts are shown.
    </div>

    <!-- masonry wall -->
    <div class="[column-fill:_balance] columns-1 sm:columns-2 xl:columns-3 gap-4">
      <article v-for="post in visiblePosts" :key="post.postId ?? ('d' + post.userId)"
               class="mb-4 break-inside-avoid rounded-2xl border shadow-sm relative"
               :class="post.redacted ? 'bg-white dark:bg-slate-900 border-dashed border-slate-200 dark:border-slate-700'
                 : draftFor(post) ? 'bg-amber-50 dark:bg-amber-500/10 border-amber-300 dark:border-amber-500/40'
                 : pastels[hash(post.userId)] + ' border-slate-200 dark:border-slate-800'">
        <!-- header -->
        <div class="flex items-center gap-2 px-4 pt-3">
          <span class="w-7 h-7 rounded-full text-white text-xs font-bold grid place-items-center shrink-0"
                :class="avatarColors[hash(post.userId)]">{{ initials(post.authorName) }}</span>
          <div class="min-w-0">
            <div class="text-sm font-semibold truncate">
              {{ post.redacted ? 'Hidden' : post.authorName }}
              <span v-if="post.mine" class="text-xs text-slate-400 dark:text-slate-500 font-normal">· you</span>
            </div>
            <div class="text-[11px] text-slate-400 dark:text-slate-500">
              <span v-if="draftFor(post)" class="text-amber-600 dark:text-amber-400 font-medium">✎ editing · {{ ago(draftFor(post).updatedAt) }} ago</span>
              <span v-else>{{ ago(post.updatedAt) }} ago</span>
            </div>
          </div>
          <button v-if="post.mine && post.postId" @click="toggleHiddenByStudent(post)"
                  class="ml-auto text-[10px] px-1.5 py-0.5 rounded border shrink-0"
                  :class="post.hiddenByStudent
                    ? 'bg-purple-100 text-purple-700 border-purple-200 dark:bg-purple-500/15 dark:text-purple-300 dark:border-purple-500/30'
                    : 'text-slate-400 dark:text-slate-500 border-slate-200 dark:border-slate-700 hover:border-slate-400'">
            {{ post.hiddenByStudent ? '🔒 hidden from peers' : 'visible to peers' }}
          </button>
          <VerdictBadge v-if="!post.redacted && post.verdict !== 'None'" :verdict="post.verdict" small
                        :class="post.mine && post.postId ? 'ml-1' : 'ml-auto'" />
          <span v-else-if="draftFor(post)" class="ml-1 text-[10px] bg-amber-200 text-amber-800 dark:bg-amber-500/25 dark:text-amber-200 rounded px-1.5 py-0.5 font-semibold">LIVE</span>
        </div>

        <div v-if="post.redacted" class="px-4 py-4 text-sm text-slate-400 dark:text-slate-500">
          🔒 This student’s answer is hidden from peers.
        </div>

        <template v-else>
          <!-- note -->
          <div v-if="post.postId" class="px-4 pt-2">
            <div v-if="noteDraft[post.postId] !== undefined" class="flex gap-1">
              <input v-model="noteDraft[post.postId]" @keyup.enter="saveNote(post)"
                     placeholder="Add a note…" maxlength="500"
                     class="flex-1 text-sm border border-slate-300 dark:border-slate-700 rounded-lg px-2 py-1 bg-white dark:bg-slate-800" />
              <button @click="saveNote(post)" class="text-xs text-amber-600 dark:text-amber-400 px-1">save</button>
            </div>
            <p v-else-if="post.note" @click="post.mine && startNote(post)"
               class="text-sm text-slate-700 dark:text-slate-200" :class="{ 'cursor-text': post.mine }">{{ post.note }}</p>
            <button v-else-if="post.mine" @click="startNote(post)" class="text-xs text-slate-400 dark:text-slate-500 hover:text-slate-600 dark:hover:text-slate-300">
              + add a note
            </button>
          </div>

          <!-- code preview (live draft overrides the last submitted snapshot) -->
          <pre v-if="previewCode(post)"
               class="mx-4 mt-2 text-[11px] leading-snug rounded-lg p-2 overflow-x-auto max-h-44"
               :class="draftFor(post) ? 'bg-slate-800 text-amber-50 ring-1 ring-amber-400' : 'bg-slate-900 text-slate-100'">{{ previewCode(post) }}</pre>
          <div class="px-4 mt-1 text-[11px] text-slate-400 dark:text-slate-500">
            <span v-if="post.attempts">{{ post.attempts }} attempt{{ post.attempts === 1 ? '' : 's' }}</span>
            <span v-if="post.runtimeMs"> · {{ post.runtimeMs }}ms · {{ post.memoryKb }}KB</span>
            <span v-if="post.score"> · {{ Math.round(post.score * 100) }}%</span>
          </div>

          <div v-if="!post.postId" class="px-4 pb-3 pt-1 text-[11px] text-slate-400 dark:text-slate-500">watching live · not submitted yet</div>

          <!-- reactions -->
          <div v-if="post.postId" class="flex flex-wrap gap-1 px-4 mt-2">
            <button v-for="e in EMOJIS" :key="e" @click="toggleReaction(post, e)"
                    class="text-xs rounded-full px-2 py-0.5 border transition"
                    :class="reactionCount(post, e)?.mine
                      ? 'bg-amber-100 border-amber-300 dark:bg-amber-500/20 dark:border-amber-500/40'
                      : 'bg-white/70 dark:bg-slate-800/70 border-slate-200 dark:border-slate-700 hover:border-slate-300'">
              {{ e }}<span v-if="reactionCount(post, e)" class="ml-1 text-slate-500 dark:text-slate-400">{{ reactionCount(post, e).count }}</span>
            </button>
          </div>

          <!-- comments -->
          <div v-if="post.postId" class="px-4 mt-2 pb-3">
            <button @click="openComments[post.postId] = !openComments[post.postId]"
                    class="text-xs text-slate-500 dark:text-slate-400 hover:text-slate-800 dark:hover:text-slate-200">
              💬 {{ post.comments.length }} comment{{ post.comments.length === 1 ? '' : 's' }}
            </button>
            <div v-if="openComments[post.postId]" class="mt-2 space-y-1.5">
              <div v-for="c in post.comments" :key="c.id" class="text-xs bg-white/70 dark:bg-slate-800/70 rounded-lg px-2 py-1">
                <span class="font-semibold">{{ c.authorName }}</span>
                <span class="text-slate-400 dark:text-slate-500"> · {{ ago(c.createdAt) }} ago</span>
                <button v-if="c.canDelete" @click="delComment(post, c)"
                        class="text-slate-300 dark:text-slate-600 hover:text-red-500 float-right">×</button>
                <div class="text-slate-700 dark:text-slate-200 whitespace-pre-wrap">{{ c.body }}</div>
              </div>
              <div class="flex gap-1">
                <input v-model="commentDraft[post.postId]" @keyup.enter="addComment(post)"
                       placeholder="Write a comment…"
                       class="flex-1 text-xs border border-slate-300 dark:border-slate-700 rounded-lg px-2 py-1 bg-white dark:bg-slate-800" />
                <button @click="addComment(post)" class="text-xs text-amber-600 dark:text-amber-400 px-1">send</button>
              </div>
            </div>
          </div>
        </template>

        <div class="px-4 py-1.5 text-right border-t border-slate-100 dark:border-slate-800">
          <button @click="openPost(post)"
                  class="text-[11px] text-slate-400 dark:text-slate-500 hover:text-slate-700 dark:hover:text-slate-300">open ↗</button>
        </div>
      </article>
    </div>

    <p v-if="!visiblePosts.length" class="text-slate-400 dark:text-slate-500 text-sm">
      No posts yet — a card appears here when a student runs their first submission.
    </p>

    <SubmissionView v-if="viewSubmissionId" :submission-id="viewSubmissionId" @close="viewSubmissionId = null" />
  </div>
</template>
