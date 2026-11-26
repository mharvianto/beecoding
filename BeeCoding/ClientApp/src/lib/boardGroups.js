// Which problems the board's Wall and Grid show: one group, the ungrouped ones, or everything outside the archive.
// A value is 'all', 'none' (ungrouped) or a group id as a string.

export function groupOptions(groups, problems) {
  if (!groups.length) return [];
  // Only groups Wall and Grid have something for: the progress data leaves out hidden and not-yet-open ones, for staff too.
  const used = new Set(problems.map((p) => p.groupId));
  const listed = groups.filter((g) => used.has(g.id));
  const current = listed.filter((g) => !g.archived).map((g) => ({ value: String(g.id), group: g }));
  const loose = problems.some((p) => p.groupId == null) ? [{ value: 'none' }] : [];
  const archive = listed.filter((g) => g.archived).reverse().map((g) => ({ value: String(g.id), group: g }));
  return [...current, ...loose, ...archive, { value: 'all' }];
}

/** The problems Wall and Grid can have: the server leaves out hidden ones and those of a hidden or not-yet-open group, for staff too. */
export function liveProblems(groups, problems) {
  const off = new Set(groups.filter((g) => g.hidden || g.state === 'Upcoming').map((g) => g.id));
  return problems.filter((p) => !p.hidden && !off.has(p.groupId));
}

/** The saved choice while it still exists, else the newest open group (new groups are added at the end), else ungrouped, else all. */
export function pickGroup(saved, options) {
  if (options.some((o) => o.value === saved)) return saved;
  const open = options.filter((o) => o.group && !o.group.archived && !o.group.hidden && o.group.state === 'Open' && o.group.problemCount > 0);
  if (open.length) return open[open.length - 1].value;
  return options.some((o) => o.value === 'none') ? 'none' : 'all';
}

export function inSelection(selected, groups) {
  const archived = new Set(groups.filter((g) => g.archived).map((g) => g.id));
  return (p) => (selected === 'all' ? !archived.has(p.groupId)
    : selected === 'none' ? p.groupId == null
    : String(p.groupId) === selected);
}
