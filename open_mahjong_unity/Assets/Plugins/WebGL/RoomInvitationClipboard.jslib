mergeInto(LibraryManager.library, {
    CopyInvitationWithResult__deps: ['$MahjongClipboard'],
    CopyInvitationWithResult: function(textPtr, receiverPtr, callbackPtr) {
        var text = UTF8ToString(textPtr);
        var receiver = UTF8ToString(receiverPtr);
        var callback = UTF8ToString(callbackPtr);
        var done = function(ok) { SendMessage(receiver, callback, ok ? 'ok' : 'failed'); };
        MahjongClipboard.copy(text, done);
    }
});
