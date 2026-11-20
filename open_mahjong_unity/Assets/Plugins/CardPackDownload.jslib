mergeInto(LibraryManager.library, {
    DownloadCardPack: function(gameObject, method, filename, bytes, length) {
        var url = URL.createObjectURL(new Blob([HEAPU8.slice(bytes, bytes + length)], { type: 'application/zip' }));
        var link = document.createElement('a');
        link.href = url;
        link.download = UTF8ToString(filename);
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(function() { URL.revokeObjectURL(url); }, 60000);
        SendMessage(UTF8ToString(gameObject), UTF8ToString(method), '');
    }
});
