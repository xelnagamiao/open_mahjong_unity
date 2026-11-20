var ClipboardPlugin = {
    $MahjongClipboard: {
        copy: function(text, done) {
            var fallback = function() {
                var input = document.createElement('textarea');
                var active = document.activeElement;
                input.value = text;
                input.readOnly = true;
                input.style.cssText = 'position:fixed;left:0;top:0;width:1px;height:1px;opacity:0;font-size:16px;';
                var onCopy = function(event) {
                    if (!event.clipboardData) return;
                    event.clipboardData.setData('text/plain', text);
                    event.preventDefault();
                    event.stopImmediatePropagation();
                };
                var ok = false;
                try {
                    document.body.appendChild(input);
                    input.focus({ preventScroll: true });
                    input.select();
                    input.setSelectionRange(0, text.length);
                    // Unity also handles clipboard events; preserve the requested text.
                    document.addEventListener('copy', onCopy, true);
                    ok = document.execCommand('copy');
                } catch (_) { ok = false; }
                finally {
                    document.removeEventListener('copy', onCopy, true);
                    input.remove();
                    if (active && active.focus) active.focus({ preventScroll: true });
                }
                done(ok);
            };
            try {
                if (navigator.clipboard && window.isSecureContext) {
                    navigator.clipboard.writeText(text).then(function() { done(true); }, fallback);
                    return;
                }
            } catch (_) { /* Some browsers throw before returning a promise. */ }
            fallback();
        }
    },
    CopyToClipboard__deps: ['$MahjongClipboard'],
    CopyToClipboard: function(strPtr) {
        MahjongClipboard.copy(UTF8ToString(strPtr), function() {});
    }
};

mergeInto(LibraryManager.library, ClipboardPlugin);
