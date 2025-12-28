(() => {
  const DEFAULT_MAX_ENTRIES = 500;
  const LS_KEY = 'moge.browserlog.entries';

  function nowIso() {
    try { return new Date().toISOString(); } catch { return '' + Date.now(); }
  }

  function dayIso() {
    // yyyy-MM-dd
    const d = new Date();
    const yyyy = d.getFullYear();
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const dd = String(d.getDate()).padStart(2, '0');
    return `${yyyy}-${mm}-${dd}`;
  }

  function safeToString(v) {
    if (v == null) return String(v);
    if (typeof v === 'string') return v;
    if (v instanceof Error) return `${v.name}: ${v.message}\n${v.stack || ''}`;
    try {
      return JSON.stringify(v);
    } catch {
      try { return String(v); } catch { return '[unstringifiable]'; }
    }
  }

  function downloadText(filename, text) {
    const blob = new Blob([text], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.rel = 'noopener';
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  const state = {
    enabled: true,
    maxEntries: DEFAULT_MAX_ENTRIES,
    platformSubfolder: null,
    hookConsole: true,
    _installed: false,
  };

  const entries = [];

  function persistToLocalStorage() {
    try {
      localStorage.setItem(LS_KEY, JSON.stringify(entries.slice(-state.maxEntries)));
    } catch {
      // ignore
    }
  }

  function loadFromLocalStorage() {
    try {
      const raw = localStorage.getItem(LS_KEY);
      if (!raw) return;
      const parsed = JSON.parse(raw);
      if (Array.isArray(parsed)) {
        entries.splice(0, entries.length, ...parsed);
      }
    } catch {
      // ignore
    }
  }

  async function appendLine(line) {
    if (!state.enabled) return;

    const rec = { ts: nowIso(), line };
    entries.push(rec);
    if (entries.length > state.maxEntries) entries.splice(0, entries.length - state.maxEntries);
    persistToLocalStorage();

    // Best-effort OPFS write (WebApp loads moge-logger.js)
    try {
      if (window.mogeLogger && typeof window.mogeLogger.appendDailyLog === 'function' && state.platformSubfolder) {
        await window.mogeLogger.appendDailyLog(state.platformSubfolder, dayIso(), line);
      }
    } catch {
      // ignore
    }
  }

  function install() {
    if (state._installed) return;
    state._installed = true;

    loadFromLocalStorage();

    // window.onerror
    window.addEventListener('error', (ev) => {
      const msg = ev && ev.message ? ev.message : 'window.error';
      const src = ev && ev.filename ? `${ev.filename}:${ev.lineno || 0}:${ev.colno || 0}` : '';
      const err = ev && ev.error ? safeToString(ev.error) : '';
      void appendLine(`[BROWSER][error] ${msg} ${src}${err ? `\n${err}` : ''}`);
    });

    // unhandledrejection
    window.addEventListener('unhandledrejection', (ev) => {
      const reason = ev && 'reason' in ev ? safeToString(ev.reason) : '';
      void appendLine(`[BROWSER][unhandledrejection] ${reason}`);
    });

    if (state.hookConsole) {
      const orig = {
        log: console.log,
        info: console.info,
        warn: console.warn,
        error: console.error,
      };

      function wrap(level) {
        return function (...args) {
          try {
            const text = args.map(safeToString).join(' ');
            void appendLine(`[console.${level}] ${text}`);
          } catch {
            // ignore
          }
          return orig[level].apply(console, args);
        };
      }

      console.log = wrap('log');
      console.info = wrap('info');
      console.warn = wrap('warn');
      console.error = wrap('error');

      void appendLine('[mogeBrowserLog] console hook installed');
    }
  }

  window.mogeBrowserLog = {
    configure: (opts) => {
      if (!opts) opts = {};
      if (typeof opts.enabled === 'boolean') state.enabled = opts.enabled;
      if (typeof opts.hookConsole === 'boolean') state.hookConsole = opts.hookConsole;
      if (typeof opts.maxEntries === 'number' && opts.maxEntries > 0) state.maxEntries = Math.floor(opts.maxEntries);
      if (typeof opts.platformSubfolder === 'string') state.platformSubfolder = opts.platformSubfolder;
      install();
    },

    clear: () => {
      entries.splice(0, entries.length);
      try { localStorage.removeItem(LS_KEY); } catch { }
    },

    getEntries: () => entries.slice(),

    downloadBuffer: (filename) => {
      const name = filename || `moge-browserlog-${dayIso()}.json`;
      downloadText(name, JSON.stringify(entries, null, 2));
    },

    downloadToday: async (filename) => {
      // Prefer OPFS daily log if available.
      if (window.mogeLogger && typeof window.mogeLogger.downloadDailyLog === 'function' && state.platformSubfolder) {
        await window.mogeLogger.downloadDailyLog(state.platformSubfolder, dayIso(), filename);
        return;
      }
      window.mogeBrowserLog.downloadBuffer(filename);
    }
  };
})();
