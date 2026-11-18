// What the output box of a Run shows. The verdict goes on the FIRST line: a timed-out or runaway program can have long output
// (the server cuts it, see RunController.CutOutput), so a footer at the end would sit far below the visible part of the box.
const SIGNALS = {
  6: 'abort() was called. An assertion failed, or the program stopped itself (for example a double free, or an allocation that failed in C++).',
  8: 'Arithmetic error, usually a division or modulo by zero.',
  11: 'Segmentation fault. The program touched memory it must not: an array index out of range, a null or dangling pointer, '
    + 'deep recursion, or malloc/calloc/new that failed (returned NULL) because it asked for more than the memory limit, then was used anyway.',
  25: 'The program wrote more output than allowed.',
};

function verdictLine(r) {
  if (r.timedOut || r.signal === 24 || r.signal === 9) return '[Time limit exceeded — the program was stopped]';
  if (r.signal) return `[Runtime error — ${SIGNALS[r.signal] || `the program was killed by signal ${r.signal}.`}]`;
  if (r.exitCode > 0) return `[Runtime error — main returned ${r.exitCode}; it should return 0 when everything went well]`;
  return '';
}

export function runText(r) {
  if (!r) return '';
  if (!r.compileOk) return '[compile error]\n' + r.compilerOutput;
  const v = verdictLine(r);
  return (v ? v + '\n' : '') + (r.stdout || '') + (r.stderr ? '\n[stderr] ' + r.stderr : '')
    + `\n— ${r.runtimeMs}ms, ${r.memoryKb}KB${r.timedOut ? ', TIMED OUT' : ''}`;
}
