import { describe, it, expect } from 'vitest';
import { groupOptions, liveProblems, pickGroup, inSelection } from '../src/lib/boardGroups';

const g = (id, extra = {}) => ({ id, title: 'G' + id, archived: false, hidden: false, state: 'Open', problemCount: 1, ...extra });
const groups = [g(1, { archived: true, state: 'Archived' }), g(2), g(3), g(4, { hidden: true })];
const problems = [{ id: 10, groupId: 1 }, { id: 20, groupId: 2 }, { id: 30, groupId: 3 }, { id: 40, groupId: 4 }, { id: 99, groupId: null }];

describe('board group selection', () => {
  const options = groupOptions(groups, problems);
  it('lists current groups, ungrouped, the archive, then all', () => {
    expect(options.map((o) => o.value)).toEqual(['2', '3', '4', 'none', '1', 'all']);
    expect(groupOptions([], problems)).toEqual([]);
  });
  it('does not offer a group with nothing to show (hidden or not yet open)', () => {
    expect(groupOptions([...groups, g(5, { state: 'Upcoming' })], problems).map((o) => o.value)).not.toContain('5');
  });
  it('leaves out what the server never sends: hidden problems and those of hidden or not-yet-open groups', () => {
    const all = [...problems, { id: 50, groupId: 2, hidden: true }, { id: 60, groupId: 5 }];
    expect(liveProblems([...groups, g(5, { state: 'Upcoming' })], all).map((p) => p.id)).toEqual([10, 20, 30, 99]);
  });
  it('defaults to the newest open group that students can see, and keeps a saved choice only while it exists', () => {
    expect(pickGroup(null, options)).toBe('3');
    expect(pickGroup('1', options)).toBe('1');
    expect(pickGroup('42', options)).toBe('3');
  });
  it('leaves the archive out of "all"', () => {
    expect(problems.filter(inSelection('all', groups)).map((p) => p.id)).toEqual([20, 30, 40, 99]);
    expect(problems.filter(inSelection('1', groups)).map((p) => p.id)).toEqual([10]);
    expect(problems.filter(inSelection('none', groups)).map((p) => p.id)).toEqual([99]);
  });
});
