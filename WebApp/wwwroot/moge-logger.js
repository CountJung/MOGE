window.mogeLogger = {
  _getRoot: async () => {
    if (!navigator.storage || !navigator.storage.getDirectory) {
      throw new Error('OPFS not supported (navigator.storage.getDirectory unavailable).');
    }
    return await navigator.storage.getDirectory();
  },

  _getDir: async (root, parts) => {
    let dir = root;
    for (const p of parts) {
      dir = await dir.getDirectoryHandle(p, { create: true });
    }
    return dir;
  },

  _formatDate: (dateIso) => {
    // expect yyyy-MM-dd
    return dateIso;
  },

  appendDailyLog: async (platformSubfolder, dayIso, line) => {
    const root = await window.mogeLogger._getRoot();
    const dir = await window.mogeLogger._getDir(root, ['moge-logs', platformSubfolder]);
    const fileName = `${window.mogeLogger._formatDate(dayIso)}.log`;

    const fileHandle = await dir.getFileHandle(fileName, { create: true });
    const file = await fileHandle.getFile();

    const writable = await fileHandle.createWritable({ keepExistingData: true });
    try {
      await writable.seek(file.size);
      await writable.write(`${line}\n`);
    } finally {
      await writable.close();
    }
  },

  cleanup: async (platformSubfolder, deleteBeforeDayIso) => {
    const root = await window.mogeLogger._getRoot();
    const dir = await window.mogeLogger._getDir(root, ['moge-logs', platformSubfolder]);

    for await (const [name, handle] of dir.entries()) {
      if (handle.kind !== 'file') continue;
      if (!name.endsWith('.log')) continue;

      const day = name.substring(0, name.length - 4); // yyyy-MM-dd
      if (day < deleteBeforeDayIso) {
        try {
          await dir.removeEntry(name);
        } catch {
        }
      }
    }
  },

  readDailyLog: async (platformSubfolder, dayIso) => {
    const root = await window.mogeLogger._getRoot();
    const dir = await window.mogeLogger._getDir(root, ['moge-logs', platformSubfolder]);
    const fileName = `${window.mogeLogger._formatDate(dayIso)}.log`;

    try {
      const fileHandle = await dir.getFileHandle(fileName, { create: false });
      const file = await fileHandle.getFile();
      return await file.text();
    } catch {
      return null;
    }
  },

  downloadText: (fileName, text) => {
    try {
      const blob = new Blob([text ?? ''], { type: 'text/plain' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = fileName || 'moge-log.txt';
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch {
    }
  }
};
