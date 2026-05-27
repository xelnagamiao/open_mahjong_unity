var ClipboardPlugin = {
    CopyToClipboard: function(strPtr) {
        var str = UTF8ToString(strPtr);
        if (navigator.clipboard && window.isSecureContext) {
            navigator.clipboard.writeText(str);
            return;
        }
        var textarea = document.createElement('textarea');
        textarea.value = str;
        textarea.style.position = 'fixed';
        textarea.style.left = '-9999px';
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand('copy');
        document.body.removeChild(textarea);
    }
};

mergeInto(LibraryManager.library, ClipboardPlugin);
