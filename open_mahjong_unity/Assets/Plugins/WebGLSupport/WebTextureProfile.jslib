mergeInto(LibraryManager.library, {
  OmUseWebTextureASTC: function (linear) {
    var supplied = Module['omWebContent'];
    if (supplied != null) {
      return supplied['profile'] === 'ASTC' ? 1 : supplied['profile'] === 'DXT' ? 0 : -1;
    }
    var astc = GLctx.getExtension('WEBGL_compressed_texture_astc');
    var dxt = GLctx.getExtension('WEBGL_compressed_texture_s3tc');
    var srgb = !linear || GLctx.getExtension('WEBGL_compressed_texture_s3tc_srgb');
    return astc && !(dxt && srgb) ? 1 : 0;
  },

  // Keep the LZ4 source in the same browser-side file system as Unity's .data.
  // LoadFromFile reads individual blocks without reserving the whole bundle in WASM.
  OmMountWebBundle: function (urlPtr, pathPtr, size) {
    var url = UTF8ToString(urlPtr);
    var path = UTF8ToString(pathPtr);
    Module['omWebBundleProgress'] = 0;
    (async function () {
      var supplied = Module['omWebContent'];
      if (supplied != null) {
        var staged = supplied['take'](url);
        if (!(staged instanceof Uint8Array) || staged.byteLength !== size) {
          throw new Error('Missing or incomplete preloaded bundle');
        }
        return staged;
      }
      var response = await fetch(url);
      if (!response.ok) throw new Error('HTTP ' + response.status);
      var bytes;
      if (response.body && response.body.getReader) {
        var reader = response.body.getReader();
        bytes = new Uint8Array(size);
        var offset = 0;
        try {
          for (;;) {
            var chunk = await reader.read();
            if (chunk.done) break;
            if (chunk.value.length > size - offset) throw new Error('Unexpected bundle length');
            bytes.set(chunk.value, offset);
            offset += chunk.value.length;
            Module['omWebBundleProgress'] = Math.min(0.99, offset / size);
          }
          if (offset !== size) throw new Error('Incomplete bundle');
        } finally { reader.releaseLock(); }
      } else {
        bytes = new Uint8Array(await response.arrayBuffer());
        if (bytes.length !== size) throw new Error('Unexpected bundle length');
      }
      return bytes;
    })().then(function (bytes) {
      // Unity's own loader mounts its data with canOwn=true for the same reason.
      Module['FS_createDataFile'](path, null, bytes, true, false, true);
      Module['omWebBundleProgress'] = 1;
    }).catch(function (error) {
      console.error('[WebTexture] ' + url, error);
      Module['omWebBundleProgress'] = -1;
    });
  },

  OmWebBundleProgress: function () {
    return Module['omWebBundleProgress'];
  },

  OmNotifyWebTextureProgress: function (completed, total, progress, phase) {
    if (typeof window === 'undefined' || typeof window.dispatchEvent !== 'function') return;
    window.dispatchEvent(new CustomEvent('unity-web-texture-progress', {
      detail: { completed: completed, total: total, progress: progress, phase: phase }
    }));
  }
});
