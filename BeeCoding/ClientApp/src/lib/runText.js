// What the output box of a Run shows. A timed-out program says so on the FIRST line: its output can be long
// (the server cuts it, see RunController.CutOutput) and the footer would sit far below the visible part of the box.
export function runText(r) {
  if (!r) return '';
  if (!r.compileOk) return '[compile error]\n' + r.compilerOutput;
  return (r.timedOut ? '[Time limit exceeded — the program was stopped]\n' : '')
    + (r.stdout || '') + (r.stderr ? '\n[stderr] ' + r.stderr : '')
    + `\n— ${r.runtimeMs}ms, ${r.memoryKb}KB${r.timedOut ? ', TIMED OUT' : ''}`;
}
