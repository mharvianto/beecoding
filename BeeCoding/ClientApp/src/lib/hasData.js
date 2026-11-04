// The trend endpoints return every period in the window, zeros included: "nothing to show" means all of them are 0.
export const hasData = (points) => !!points?.some((p) => Object.values(p).some((v) => typeof v === 'number' && v > 0));
