// The student's local calendar day, e.g. "2026-09-16" — used for daily-streak bucketing.
// Deliberately local time (not toISOString(), which is UTC) so a streak day matches when
// the student actually experiences "today".
export function localDayKey(d = new Date()) {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}
