var LocalConvertedRecord = {
    $LocalConvertedRecordState: {
        storageKey: 'salasasa:local-replay-record',
        bytes: null
    },

    $LocalConvertedRecordHeap: function () {
        return (typeof HEAPU8 !== 'undefined') ? HEAPU8 : Module.HEAPU8;
    },

    $LocalConvertedRecordSend: function (go, method, message) {
        SendMessage(go, method, message);
    },

    LocalConvertedRecordLoad: function (goPtr, methodPtr) {
        var go = UTF8ToString(goPtr);
        var method = UTF8ToString(methodPtr);
        try {
            if (typeof sessionStorage === 'undefined') {
                LocalConvertedRecordSend(go, method, 'empty');
                return;
            }
            var raw = sessionStorage.getItem(LocalConvertedRecordState.storageKey);
            if (!raw) {
                LocalConvertedRecordState.bytes = null;
                LocalConvertedRecordSend(go, method, 'empty');
                return;
            }
            var encoded = new TextEncoder().encode(raw);
            LocalConvertedRecordState.bytes = encoded.buffer.slice(encoded.byteOffset, encoded.byteOffset + encoded.byteLength);
            LocalConvertedRecordSend(go, method, 'ok|' + encoded.byteLength);
        } catch (e) {
            LocalConvertedRecordState.bytes = null;
            LocalConvertedRecordSend(go, method, 'error|无法读取本地转换牌谱');
        }
    },

    LocalConvertedRecordCopy: function (dstPtr, maxLen) {
        if (!LocalConvertedRecordState.bytes || maxLen <= 0) {
            return 0;
        }
        var source = new Uint8Array(LocalConvertedRecordState.bytes);
        var n = source.length < maxLen ? source.length : maxLen;
        try {
            LocalConvertedRecordHeap().set(source.subarray(0, n), dstPtr);
        } catch (e) {
            return 0;
        }
        return n;
    }
};

autoAddDeps(LocalConvertedRecord, '$LocalConvertedRecordState');
autoAddDeps(LocalConvertedRecord, '$LocalConvertedRecordHeap');
autoAddDeps(LocalConvertedRecord, '$LocalConvertedRecordSend');
mergeInto(LibraryManager.library, LocalConvertedRecord);
