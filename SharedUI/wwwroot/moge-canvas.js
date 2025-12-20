(() => {
  const canvasToImage = new WeakMap();
  const canvasToRawCanvas = new WeakMap();

  function ensureSize(canvas) {
    const dpr = window.devicePixelRatio || 1;
    const widthCss = canvas.clientWidth || 1;
    const heightCss = canvas.clientHeight || 1;

    const width = Math.max(1, Math.floor(widthCss * dpr));
    const height = Math.max(1, Math.floor(heightCss * dpr));

    if (canvas.width !== width) canvas.width = width;
    if (canvas.height !== height) canvas.height = height;

    return { dpr, widthCss, heightCss };
  }

  async function loadImage(dataUrl) {
    return await new Promise((resolve, reject) => {
      const img = new Image();
      img.onload = () => resolve(img);
      img.onerror = (e) => reject(e);
      img.src = dataUrl;
    });
  }

  window.mogeCanvas = {
    getRect: (canvas) => {
      const r = canvas.getBoundingClientRect();
      return { left: r.left, top: r.top, width: r.width, height: r.height };
    },

    setImage: async (canvas, dataUrl) => {
      const img = await loadImage(dataUrl);
      canvasToImage.set(canvas, img);
      canvasToRawCanvas.delete(canvas);
      return { width: img.naturalWidth, height: img.naturalHeight };
    },

    setRawRgba: async (canvas, width, height, rgbaBytes) => {
      // rgbaBytes is a Uint8Array (marshaled from .NET byte[])
      let rawCanvas;
      if (typeof OffscreenCanvas !== 'undefined') {
        rawCanvas = new OffscreenCanvas(width, height);
      } else {
        rawCanvas = document.createElement('canvas');
        rawCanvas.width = width;
        rawCanvas.height = height;
      }

      const ctx = rawCanvas.getContext('2d');
      const clamped = new Uint8ClampedArray(rgbaBytes);
      const imageData = new ImageData(clamped, width, height);
      ctx.putImageData(imageData, 0, 0);

      canvasToRawCanvas.set(canvas, { canvas: rawCanvas, width, height });
      canvasToImage.delete(canvas);
      return { width, height };
    },

    clear: (canvas) => {
      canvasToImage.delete(canvas);
      canvasToRawCanvas.delete(canvas);
      const ctx = canvas.getContext('2d');
      if (!ctx) return;
      const { widthCss, heightCss, dpr } = ensureSize(canvas);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.clearRect(0, 0, widthCss, heightCss);
    },

    draw: (canvas, state) => {
      const img = canvasToImage.get(canvas);
      const raw = canvasToRawCanvas.get(canvas);
      const ctx = canvas.getContext('2d');
      if (!ctx) return { hasImage: false };

      const { widthCss, heightCss, dpr } = ensureSize(canvas);

      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.clearRect(0, 0, widthCss, heightCss);

      if (!img && !raw) return { hasImage: false };

      const scale = (state?.scale ?? 1) * dpr;
      const offsetX = (state?.offsetX ?? 0) * dpr;
      const offsetY = (state?.offsetY ?? 0) * dpr;

      ctx.setTransform(scale, 0, 0, scale, offsetX, offsetY);
      ctx.imageSmoothingEnabled = true;
      ctx.imageSmoothingQuality = 'high';

      if (img) {
        ctx.drawImage(img, 0, 0);
        return { hasImage: true, imageWidth: img.naturalWidth, imageHeight: img.naturalHeight, dpr };
      }

      // raw RGBA path
      ctx.drawImage(raw.canvas, 0, 0);
      return { hasImage: true, imageWidth: raw.width, imageHeight: raw.height, dpr };
    }
  };
})();
