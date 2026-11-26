import { describe, it, expect } from 'vitest';
import { newPlacement } from '../src/lib/placement';

describe('new board problem placement', () => {
  it('starts as a draft outside any group and follows the group inside one', () => {
    expect(newPlacement(undefined)).toMatchObject({ groupId: null, hidden: true });
    expect(newPlacement('12')).toMatchObject({ groupId: 12, hidden: false });
    expect(newPlacement('nonsense')).toMatchObject({ groupId: null, hidden: true });
  });
});
