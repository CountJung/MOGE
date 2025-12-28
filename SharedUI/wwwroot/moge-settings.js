(() => {
  const KEY = 'moge.settings.v1';

  window.mogeSettings = {
    get: () => {
      try {
        return localStorage.getItem(KEY);
      } catch {
        return null;
      }
    },
    set: (value) => {
      try {
        localStorage.setItem(KEY, value ?? '');
        return true;
      } catch {
        return false;
      }
    }
  };
})();
