// Colours of a badge's tier (bronze < silver < gold < platinum). Full class names, so Tailwind sees them.
export const TIER = {
  bronze: { label: 'Bronze', icon: 'text-orange-700 dark:text-orange-400', card: 'border-orange-300 dark:border-orange-500/40 bg-orange-50/60 dark:bg-orange-500/5', note: 'text-orange-700 dark:text-orange-400' },
  silver: { label: 'Silver', icon: 'text-slate-400 dark:text-slate-300', card: 'border-slate-300 dark:border-slate-500/50 bg-slate-100/70 dark:bg-slate-500/10', note: 'text-slate-600 dark:text-slate-300' },
  gold: { label: 'Gold', icon: 'text-yellow-500', card: 'border-yellow-300 dark:border-yellow-500/40 bg-yellow-50/70 dark:bg-yellow-500/5', note: 'text-yellow-700 dark:text-yellow-400' },
  platinum: { label: 'Platinum', icon: 'text-cyan-500 dark:text-cyan-400', card: 'border-cyan-300 dark:border-cyan-500/40 bg-cyan-50/70 dark:bg-cyan-500/5', note: 'text-cyan-700 dark:text-cyan-400' },
};
export const tierOf = (t) => TIER[t] || TIER.bronze;
