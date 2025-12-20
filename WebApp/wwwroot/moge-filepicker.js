window.mogeFilePicker = {
  _replaceExtension: (fileName, newExtWithDot) => {
    if (!fileName) return `image${newExtWithDot}`;
    const idx = fileName.lastIndexOf('.');
    if (idx <= 0) return `${fileName}${newExtWithDot}`;
    return `${fileName.substring(0, idx)}${newExtWithDot}`;
  },

  _blobToBase64: (blob) => {
    // Use FileReader to avoid binary-string conversion issues for large files.
    return new Promise((resolve, reject) => {
      try {
        const reader = new FileReader();
        reader.onerror = () => reject(reader.error);
        reader.onload = () => {
          const result = reader.result;
          if (typeof result !== 'string') {
            resolve('');
            return;
          }
          const comma = result.indexOf(',');
          resolve(comma >= 0 ? result.substring(comma + 1) : result);
        };
        reader.readAsDataURL(blob);
      } catch (e) {
        reject(e);
      }
    });
  },

  _convertToPngBlob: async (file) => {
    const bitmap = await createImageBitmap(file);
    try {
      let canvas;
      if (typeof OffscreenCanvas !== 'undefined') {
        canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
      } else {
        canvas = document.createElement('canvas');
        canvas.width = bitmap.width;
        canvas.height = bitmap.height;
      }

      const ctx = canvas.getContext('2d');
      ctx.drawImage(bitmap, 0, 0);

      if (canvas.convertToBlob) {
        return await canvas.convertToBlob({ type: 'image/png' });
      }

      return await new Promise((resolve) => canvas.toBlob(resolve, 'image/png'));
    } finally {
      bitmap.close();
    }
  },

  _toRgbaBytes: async (file) => {
    const bitmap = await createImageBitmap(file);
    try {
      let canvas;
      if (typeof OffscreenCanvas !== 'undefined') {
        canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
      } else {
        canvas = document.createElement('canvas');
        canvas.width = bitmap.width;
        canvas.height = bitmap.height;
      }

      const ctx = canvas.getContext('2d');
      ctx.drawImage(bitmap, 0, 0);
      const imageData = ctx.getImageData(0, 0, bitmap.width, bitmap.height);

      // Copy to a plain Uint8Array.
      const rgba = new Uint8Array(imageData.data.buffer.slice(0));
      return { width: bitmap.width, height: bitmap.height, rgba };
    } finally {
      bitmap.close();
    }
  },

  pickImage: () => {
    return new Promise((resolve) => {
      const input = document.createElement('input');
      input.type = 'file';
      input.accept = 'image/*';
      input.style.display = 'none';

      input.addEventListener('change', async () => {
        try {
          const file = input.files && input.files[0];
          if (!file) {
            resolve(null);
            return;
          }

          // Normalize to PNG in the browser so WASM OpenCV doesn't need to decode
          // platform-specific formats (e.g., HEIC/AVIF/WebP).
          try {
            const rgbaInfo = await window.mogeFilePicker._toRgbaBytes(file);
            const pngBlob = await window.mogeFilePicker._convertToPngBlob(file);

            if (pngBlob && rgbaInfo && rgbaInfo.rgba) {
              const base64 = await window.mogeFilePicker._blobToBase64(pngBlob);
              const rgbaBlob = new Blob([rgbaInfo.rgba], { type: 'application/octet-stream' });
              const rgbaBase64 = await window.mogeFilePicker._blobToBase64(rgbaBlob);

              resolve({
                fileName: window.mogeFilePicker._replaceExtension(file.name, '.png'),
                contentType: 'image/png',
                base64,
                width: rgbaInfo.width,
                height: rgbaInfo.height,
                rgbaBase64
              });
              return;
            }
          } catch {
            // Fallback to raw bytes (may still fail to decode in OpenCV on WASM).
          }

          const base64 = await window.mogeFilePicker._blobToBase64(file);
          resolve({
            fileName: file.name,
            contentType: file.type || 'application/octet-stream',
            base64,
            width: 0,
            height: 0,
            rgbaBase64: null
          });
        } finally {
          input.remove();
        }
      });

      document.body.appendChild(input);
      input.click();
    });
  }
};
