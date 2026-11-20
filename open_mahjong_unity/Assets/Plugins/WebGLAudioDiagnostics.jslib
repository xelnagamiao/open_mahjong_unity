mergeInto(LibraryManager.library, {
    $OMAudioDiagnostics__deps: ['$WEBAudio', '$jsAudioCreateChannel'],
    $OMAudioDiagnostics: {
        trace: null,
        install: function () {
            if (OMAudioDiagnostics.trace) return;
            var capacity = 1024, records = new Array(capacity), count = 0, cursor = 0, sequence = 0;
            var enabled = true, disposed = false, ids = new WeakMap(), nextId = 0, restorers = [];
            var context = null, previousTime = null, previousWall = null, lastState = null;
            var objectId = function (obj) {
                if (!obj || (typeof obj !== 'object' && typeof obj !== 'function')) return 0;
                if (!ids.has(obj)) ids.set(obj, ++nextId);
                return ids.get(obj);
            };
            var write = function (phase, detail) {
                if (!enabled || disposed) return;
                try {
                    var ac = WEBAudio.audioContext;
                    records[cursor] = { seq: ++sequence, ms: performance.now(), phase: phase,
                        state: ac ? ac.state : 'unavailable', audioTime: ac ? ac.currentTime : null,
                        visibility: document.visibilityState, detail: detail || null };
                    cursor = (cursor + 1) % capacity;
                    count = Math.min(count + 1, capacity);
                } catch (_) { /* Diagnostics must never interrupt playback. */ }
            };
            var pending = function () {
                var queue = WEBAudio.soundsPendingContextResume || [];
                return queue.slice(-64).map(function (item) {
                    return { channel: objectId(item.channel), clip: objectId(item.clip),
                        startTime: item.startTime, offset: item.offset, stopDelay: item.stopDelay };
                });
            };
            var sourceState = function (channel) {
                var source = channel.source;
                return { channel: objectId(channel), source: objectId(source),
                    buffer: source ? objectId(source.buffer) : 0,
                    hasBuffer: !!(source && source.buffer), pausedMock: !!(source && source.isPausedMockNode),
                    stopped: !!(source && source.isStopped),
                    duration: source && source.buffer ? source.buffer.duration : null };
            };
            var wrapped = new WeakSet();
            var wrapChannel = function (channel) {
                if (!channel || typeof channel.playSoundClip !== 'function' || wrapped.has(channel)) return channel;
                wrapped.add(channel);
                ['playSoundClip', 'stop', 'pause', 'resume'].forEach(function (name) {
                    var original = channel[name];
                    if (typeof original !== 'function') return;
                    var wrapper = function () {
                        if (!enabled || disposed) return original.apply(this, arguments);
                        var detail = sourceState(this);
                        if (name === 'playSoundClip') {
                            detail.clip = objectId(arguments[0]);
                            detail.bufferReady = !!(arguments[0] && arguments[0].buffer);
                            detail.clipError = !!(arguments[0] && arguments[0].error);
                            detail.startTime = arguments[1]; detail.offset = arguments[2];
                        } else detail.argument = arguments[0];
                        write('channel-' + name + '-call', detail);
                        try {
                            var result = original.apply(this, arguments);
                            write('channel-' + name + '-return', sourceState(this));
                            if (name === 'playSoundClip' && this.source && !this.source.isPausedMockNode) {
                                var observedSource = this.source, observedChannel = this;
                                var ended = observedSource.onended;
                                observedSource.onended = function () {
                                    write('source-ended', { channel: objectId(observedChannel), source: objectId(observedSource) });
                                    if (ended) return ended.apply(this, arguments);
                                };
                            }
                            return result;
                        } catch (error) {
                            write('channel-' + name + '-throw', { channel: objectId(this), name: error.name });
                            throw error;
                        }
                    };
                    channel[name] = wrapper;
                    // Do not retain channels in a diagnostic list; Unity owns their lifetime.
                });
                return channel;
            };
            var originalCreate = jsAudioCreateChannel;
            var createWrapper = function () { return wrapChannel(originalCreate.apply(this, arguments)); };
            jsAudioCreateChannel = createWrapper;
            restorers.push(function () { if (jsAudioCreateChannel === createWrapper) jsAudioCreateChannel = originalCreate; });
            Object.keys(WEBAudio.audioInstances || {}).forEach(function (key) { wrapChannel(WEBAudio.audioInstances[key]); });
            var queue = WEBAudio.soundsPendingContextResume;
            if (queue) {
                var originalPush = queue.push;
                var pushWrapper = function () {
                    if (!enabled || disposed) return originalPush.apply(this, arguments);
                    for (var i = 0; i < arguments.length; i++) {
                        var item = arguments[i];
                        write('pending-enqueue', { channel: objectId(item.channel), clip: objectId(item.clip),
                            previousSameChannel: this.filter(function (old) { return old.channel === item.channel; }).length,
                            countBefore: this.length, stopDelay: item.stopDelay, offset: item.offset });
                    }
                    return originalPush.apply(this, arguments);
                };
                queue.push = pushWrapper;
                restorers.push(function () { if (queue.push === pushWrapper) queue.push = originalPush; });
            }
            var decodeRestore = null, resumeRestore = null;
            var onState = function () { write('context-statechange', { pending: pending() }); };
            var sample = function () {
                var ac = WEBAudio.audioContext;
                if (ac !== context) {
                    if (context) context.removeEventListener('statechange', onState);
                    if (decodeRestore) { decodeRestore(); decodeRestore = null; }
                    if (resumeRestore) { resumeRestore(); resumeRestore = null; }
                    context = ac;
                    if (context) context.addEventListener('statechange', onState);
                    if (context && typeof context.decodeAudioData === 'function') {
                        var target = context, originalDecode = target.decodeAudioData;
                        var decodeWrapper = function (data, success, failure) {
                            if (!enabled || disposed) return originalDecode.apply(this, arguments);
                            var decodeId = objectId(data);
                            write('decode-call', { decode: decodeId, bytes: data.byteLength });
                            // Preserve the engine's callback API and the original returned Promise.
                            var args = Array.prototype.slice.call(arguments);
                            if (typeof success === 'function') args[1] = function (buffer) {
                                write('decode-ready', { decode: decodeId, buffer: objectId(buffer), duration: buffer.duration });
                                return success.apply(this, arguments);
                            };
                            if (typeof failure === 'function') args[2] = function (error) {
                                write('decode-failed', { decode: decodeId, name: error && error.name });
                                return failure.apply(this, arguments);
                            };
                            return originalDecode.apply(this, args);
                        };
                        target.decodeAudioData = decodeWrapper;
                        decodeRestore = function () { if (target.decodeAudioData === decodeWrapper) target.decodeAudioData = originalDecode; };
                    }
                    if (context && typeof context.resume === 'function') {
                        var resumeTarget = context, originalResume = resumeTarget.resume;
                        var resumeWrapper = function () {
                            if (!enabled || disposed) return originalResume.apply(this, arguments);
                            var attempt = {};
                            write('context-resume-call', { attempt: objectId(attempt) });
                            try {
                                var promise = originalResume.apply(this, arguments);
                                if (promise && typeof promise.then === 'function') promise.then(function () {
                                    write('context-resume-resolved', { attempt: objectId(attempt) });
                                }, function (error) {
                                    write('context-resume-rejected', { attempt: objectId(attempt), name: error && error.name });
                                });
                                return promise;
                            } catch (error) {
                                write('context-resume-throw', { attempt: objectId(attempt), name: error.name });
                                throw error;
                            }
                        };
                        resumeTarget.resume = resumeWrapper;
                        resumeRestore = function () { if (resumeTarget.resume === resumeWrapper) resumeTarget.resume = originalResume; };
                    }
                    write('context-attached', { sampleRate: ac ? ac.sampleRate : null });
                }
                if (!enabled || !ac) return;
                var now = performance.now();
                if (previousWall !== null && document.visibilityState === 'visible' && ac.state === 'running'
                    && lastState === 'running' && now - previousWall >= 750 && now - previousWall < 3000
                    && ac.currentTime === previousTime) write('context-clock-not-advancing', {});
                previousWall = now; previousTime = ac.currentTime; lastState = ac.state;
                // State samples are bounded; do not print every sample to the console.
                write('context-sample', { pendingCount: (WEBAudio.soundsPendingContextResume || []).length,
                    pending: pending(), engineRunning: WEBAudio.contextIsRunning });
            };
            var listen = function (target, name) {
                var handler = function () { write('browser-' + name, { pending: pending() }); };
                target.addEventListener(name, handler, true);
                restorers.push(function () { target.removeEventListener(name, handler, true); });
            };
            ['visibilitychange'].forEach(function (name) { listen(document, name); });
            ['focus', 'blur', 'pageshow', 'pagehide', 'pointerdown', 'touchstart', 'keydown'].forEach(function (name) { listen(window, name); });
            var onRejection = function (event) {
                var error = event.reason;
                if (error && /^(NotAllowedError|NotSupportedError|EncodingError|AbortError)$/.test(error.name))
                    write('media-promise-rejection', { name: error.name });
            };
            window.addEventListener('unhandledrejection', onRejection);
            restorers.push(function () { window.removeEventListener('unhandledrejection', onRejection); });
            var api = {
                schema: 1,
                isEnabled: function () { return enabled && !disposed; },
                setEnabled: function (value) { enabled = !!value; previousWall = null; write('diagnostics-enabled', {}); },
                write: write,
                snapshot: function () {
                    var ordered = [];
                    for (var i = 0; i < count; i++) ordered.push(records[(cursor - count + i + capacity) % capacity]);
                    return JSON.parse(JSON.stringify({ schema: 1, capacity: capacity, overwritten: Math.max(0, sequence - count),
                        userAgent: navigator.userAgent, records: ordered }));
                },
                exportJson: function () { return JSON.stringify(api.snapshot(), null, 2); },
                mark: function () { write('user-mark', {}); },
                clear: function () { records = new Array(capacity); count = cursor = sequence = 0; }
            };
            OMAudioDiagnostics.trace = api;
            window.OMAudioDiagnostics = api;
            sample();
            var timer = window.setInterval(sample, 1000);
            Module.deinitializers.push(function () {
                disposed = true;
                window.clearInterval(timer);
                if (context) context.removeEventListener('statechange', onState);
                if (decodeRestore) decodeRestore();
                if (resumeRestore) resumeRestore();
                restorers.forEach(function (restore) { restore(); });
                if (window.OMAudioDiagnostics === api) delete window.OMAudioDiagnostics;
                OMAudioDiagnostics.trace = null;
            });
        }
    },
    InstallOMAudioDiagnostics__deps: ['$OMAudioDiagnostics'],
    InstallOMAudioDiagnostics: function () {
        try { OMAudioDiagnostics.install(); } catch (_) { /* Never prevent startup. */ }
    },
    OMAudioDiagnosticsEnabled__deps: ['$OMAudioDiagnostics'],
    OMAudioDiagnosticsEnabled: function () { return OMAudioDiagnostics.trace && OMAudioDiagnostics.trace.isEnabled() ? 1 : 0; },
    OMAudioDiagnosticsEvent__deps: ['$OMAudioDiagnostics'],
    OMAudioDiagnosticsEvent: function (json) {
        try { if (OMAudioDiagnostics.trace) OMAudioDiagnostics.trace.write('managed', JSON.parse(UTF8ToString(json))); } catch (_) {}
    }
});
