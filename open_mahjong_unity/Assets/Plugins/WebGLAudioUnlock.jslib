mergeInto(LibraryManager.library, {
    InstallWebGLAudioUnlock: function () {
        if (Module.webGLAudioUnlockInstalled) {
            return;
        }
        Module.webGLAudioUnlockInstalled = true;

        var removeUnlockListeners = function () {
            window.removeEventListener('pointerdown', tryUnlockAudio, true);
            window.removeEventListener('touchstart', tryUnlockAudio, true);
            window.removeEventListener('mousedown', tryUnlockAudio, true);
            window.removeEventListener('keydown', tryUnlockAudio, true);
        };

        var tryUnlockAudio = function () {
            try {
                if (typeof WEBAudio === 'undefined' || !WEBAudio.audioContext) {
                    return;
                }

                var audioContext = WEBAudio.audioContext;
                if (audioContext.state === 'running') {
                    removeUnlockListeners();
                    return;
                }

                var resumeResult = audioContext.resume();
                if (resumeResult && typeof resumeResult.then === 'function') {
                    resumeResult.then(function () {
                        if (audioContext.state === 'running') {
                            removeUnlockListeners();
                        }
                    }).catch(function () {
                        // Browser policy still requires a user gesture. Keep the
                        // listeners installed and retry on the next interaction.
                    });
                }
            } catch (error) {
                // Do not let an audio-policy failure block the gameplay input.
            }
        };

        // Capture phase runs before Unity consumes the same input event.
        window.addEventListener('pointerdown', tryUnlockAudio, true);
        window.addEventListener('touchstart', tryUnlockAudio, { capture: true, passive: true });
        window.addEventListener('mousedown', tryUnlockAudio, true);
        window.addEventListener('keydown', tryUnlockAudio, true);
        if (Module.deinitializers) {
            Module.deinitializers.push(removeUnlockListeners);
        }
        tryUnlockAudio();
    }
});
