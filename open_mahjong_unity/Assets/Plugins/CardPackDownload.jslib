mergeInto(LibraryManager.library, {
    DownloadCardPack: function(gameObject, method, filename, bytes, length) {
        var receiver = UTF8ToString(gameObject);
        var callback = UTF8ToString(method);
        try {
            var url = URL.createObjectURL(new Blob([HEAPU8.slice(bytes, bytes + length)], { type: 'application/zip' }));
            var link = document.createElement('a');
            link.href = url;
            link.download = UTF8ToString(filename);
            document.body.appendChild(link);
            link.click();
            link.remove();
            setTimeout(function() { URL.revokeObjectURL(url); }, 60000);
            SendMessage(receiver, callback, 'ok');
        } catch (e) {
            SendMessage(receiver, callback, 'failed');
        }
    }
});
