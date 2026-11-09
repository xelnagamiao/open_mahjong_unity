mergeInto(LibraryManager.library, {
    CopyInvitationWithResult: function(textPtr, receiverPtr, callbackPtr) {
        var text = UTF8ToString(textPtr);
        var receiver = UTF8ToString(receiverPtr);
        var callback = UTF8ToString(callbackPtr);
        var done = function(ok) { SendMessage(receiver, callback, ok ? 'ok' : 'failed'); };
        var fallback = function() {
            var input = document.createElement('textarea');
            var active = document.activeElement;
            input.value = text;
            input.style.position = 'fixed'; input.style.left = '-9999px';
            document.body.appendChild(input);
            var ok = false;
            try { input.focus(); input.select(); ok = document.execCommand('copy'); }
            catch (_) { ok = false; }
            finally { input.remove(); if (active && active.focus) active.focus(); }
            done(ok);
        };
        if (navigator.clipboard && window.isSecureContext) {
            navigator.clipboard.writeText(text).then(function() { done(true); }, fallback);
        } else { fallback(); }
    }
});
