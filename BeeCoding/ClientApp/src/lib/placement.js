// The starting choice for a new board problem (components/ProblemPlacement.vue): into a group it follows the group, else a draft.
export const newPlacement = (groupId) => {
  const id = Number(groupId) || null;
  return { groupId: id, hidden: id === null, chosen: false };
};
