// With many problems the progress grid labels each column with a letter (A, B, … Z, AA, AB, …) instead of the
// full title, which would make the grid very wide. Keep LETTER_THRESHOLD and letter() in step with
// BoardExportController (the downloads use the same labels).
export const LETTER_THRESHOLD = 5;

export function letter(i) {
  let s = '';
  do { s = String.fromCharCode(65 + (i % 26)) + s; i = Math.floor(i / 26) - 1; } while (i >= 0);
  return s;
}
