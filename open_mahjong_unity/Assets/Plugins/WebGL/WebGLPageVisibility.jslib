mergeInto(LibraryManager.library, {
    InstallWebGLPageVisibility: function (gameObjectNamePtr) {
        if (typeof document === 'undefined') return;
        if (Module._omVisibilityInstalled) return;
        Module._omVisibilityInstalled = true;

        var targetName = UTF8ToString(gameObjectNamePtr);
        var hiddenAtMs = 0;
        var worker = null;
        var workerUrl = null;

        var notifyUnity = function (method, value) {
            try {
                if (typeof SendMessage === 'function') {
                    SendMessage(targetName, method, value);
                } else if (Module && typeof Module.SendMessage === 'function') {
                    Module.SendMessage(targetName, method, value);
                }
            } catch (e) {
                // Ignore if runtime is tearing down.
            }
        };

        var stopWorker = function () {
            if (worker) {
                try { worker.terminate(); } catch (e) {}
                worker = null;
            }
            if (workerUrl) {
                try { URL.revokeObjectURL(workerUrl); } catch (e) {}
                workerUrl = null;
            }
        };

        var startWorker = function () {
            stopWorker();
            if (typeof Worker === 'undefined' || typeof Blob === 'undefined' || typeof URL === 'undefined') return;
            try {
                var code = 'var id = setInterval(function(){ postMessage(1); }, 10000);';
                var blob = new Blob([code], { type: 'application/javascript' });
                workerUrl = URL.createObjectURL(blob);
                worker = new Worker(workerUrl);
                worker.onmessage = function () {
                    if (document.hidden) {
                        notifyUnity('OnWebGLBackgroundHeartbeat', '');
                    }
                };
            } catch (e) {
                stopWorker();
            }
        };

        var onVisibilityChange = function () {
            if (document.hidden) {
                hiddenAtMs = Date.now();
                startWorker();
                notifyUnity('OnWebGLVisibilityChanged', '0');
            } else {
                var awayMs = hiddenAtMs > 0 ? Math.max(1, Date.now() - hiddenAtMs) : 1;
                hiddenAtMs = 0;
                stopWorker();
                notifyUnity('OnWebGLVisibilityChanged', String(awayMs));
            }
        };

        document.addEventListener('visibilitychange', onVisibilityChange);
        if (Module.deinitializers) {
            Module.deinitializers.push(function () {
                document.removeEventListener('visibilitychange', onVisibilityChange);
                stopWorker();
                Module._omVisibilityInstalled = false;
            });
        }
    }
});
