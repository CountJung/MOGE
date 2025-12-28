window.mogeTheme = window.mogeTheme || {};

window.mogeTheme.prefersDark = () => {
  try {
    return !!(window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches);
  } catch {
    return false;
  }
};
