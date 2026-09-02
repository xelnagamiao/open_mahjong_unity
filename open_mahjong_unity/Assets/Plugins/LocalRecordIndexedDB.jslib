var LocalRecordIndexedDB = {
    $LocalRecordIdbState: {
        dbName: 'omu.localRecords',
        storeName: 'records',
        indexKey: '__index__',
        bytes: null,
        db: null
    },

    $LocalRecordIdbHeap: function () {
        return (typeof HEAPU8 !== 'undefined') ? HEAPU8 : Module.HEAPU8;
    },

    $LocalRecordIdbSend: function (go, method, message) {
        SendMessage(go, method, message);
    },

    $LocalRecordIdbCopyBytes: function (ptr, length) {
        if (!ptr || length <= 0) {
            return new ArrayBuffer(0);
        }
        var heap = LocalRecordIdbHeap();
        var copy = new Uint8Array(length);
        copy.set(heap.subarray(ptr, ptr + length));
        return copy.buffer;
    },

    $LocalRecordIdbToBuffer: function (value, done) {
        if (!value) {
            done(null);
            return;
        }
        if (value instanceof ArrayBuffer) {
            done(value);
            return;
        }
        if (value instanceof Uint8Array) {
            done(value.buffer.slice(value.byteOffset, value.byteOffset + value.byteLength));
            return;
        }
        if (typeof Blob !== 'undefined' && value instanceof Blob) {
            var reader = new FileReader();
            reader.onload = function () { done(reader.result); };
            reader.onerror = function () { done(null); };
            reader.readAsArrayBuffer(value);
            return;
        }
        if (typeof value === 'string') {
            done(new TextEncoder().encode(value).buffer);
            return;
        }
        if (value.buffer) {
            done(value.buffer);
            return;
        }
        done(null);
    },

    $LocalRecordIdbOpen: function (callback) {
        if (LocalRecordIdbState.db) {
            callback(LocalRecordIdbState.db);
            return;
        }
        if (typeof indexedDB === 'undefined') {
            callback(null);
            return;
        }
        var request = indexedDB.open(LocalRecordIdbState.dbName, 1);
        request.onupgradeneeded = function () {
            var db = request.result;
            if (!db.objectStoreNames.contains(LocalRecordIdbState.storeName)) {
                db.createObjectStore(LocalRecordIdbState.storeName);
            }
        };
        request.onsuccess = function () {
            LocalRecordIdbState.db = request.result;
            callback(request.result);
        };
        request.onerror = function () {
            callback(null);
        };
        request.onblocked = function () {
            callback(LocalRecordIdbState.db || null);
        };
    },

    LocalRecordIdbPut: function (gameIdPtr, dataPtr, dataLen, indexPtr, indexLen, goPtr, methodPtr) {
        var gameId = UTF8ToString(gameIdPtr);
        var go = UTF8ToString(goPtr);
        var method = UTF8ToString(methodPtr);
        var recordBuf = LocalRecordIdbCopyBytes(dataPtr, dataLen);
        var indexBuf = LocalRecordIdbCopyBytes(indexPtr, indexLen);
        LocalRecordIdbOpen(function (db) {
            if (!db) {
                LocalRecordIdbSend(go, method, 'error|IndexedDB 不可用');
                return;
            }
            var tx = db.transaction(LocalRecordIdbState.storeName, 'readwrite');
            var store = tx.objectStore(LocalRecordIdbState.storeName);
            store.put(recordBuf, gameId);
            store.put(indexBuf, LocalRecordIdbState.indexKey);
            tx.oncomplete = function () {
                LocalRecordIdbSend(go, method, 'ok');
            };
            tx.onerror = function () {
                LocalRecordIdbSend(go, method, 'error|IndexedDB 写入失败');
            };
        });
    },

    LocalRecordIdbLoad: function (keyPtr, goPtr, methodPtr) {
        var key = UTF8ToString(keyPtr);
        var go = UTF8ToString(goPtr);
        var method = UTF8ToString(methodPtr);
        LocalRecordIdbOpen(function (db) {
            if (!db) {
                LocalRecordIdbSend(go, method, 'empty');
                return;
            }
            var tx = db.transaction(LocalRecordIdbState.storeName, 'readonly');
            var request = tx.objectStore(LocalRecordIdbState.storeName).get(key);
            request.onsuccess = function () {
                LocalRecordIdbToBuffer(request.result, function (buffer) {
                    if (!buffer || buffer.byteLength <= 0) {
                        LocalRecordIdbSend(go, method, 'empty');
                        return;
                    }
                    LocalRecordIdbState.bytes = buffer;
                    LocalRecordIdbSend(go, method, 'ok|' + buffer.byteLength);
                });
            };
            request.onerror = function () {
                LocalRecordIdbSend(go, method, 'error|IndexedDB 读取失败');
            };
        });
    },

    LocalRecordIdbCopy: function (dstPtr, maxLen) {
        if (!LocalRecordIdbState.bytes || maxLen <= 0) {
            return 0;
        }
        var source = new Uint8Array(LocalRecordIdbState.bytes);
        var n = source.length < maxLen ? source.length : maxLen;
        try {
            LocalRecordIdbHeap().set(source.subarray(0, n), dstPtr);
        } catch (e) {
            return 0;
        }
        return n;
    },

    LocalRecordIdbDelete: function (gameIdPtr, goPtr, methodPtr) {
        var gameId = UTF8ToString(gameIdPtr);
        var go = UTF8ToString(goPtr);
        var method = UTF8ToString(methodPtr);
        LocalRecordIdbOpen(function (db) {
            if (!db) {
                LocalRecordIdbSend(go, method, 'ok');
                return;
            }
            var tx = db.transaction(LocalRecordIdbState.storeName, 'readwrite');
            tx.objectStore(LocalRecordIdbState.storeName).delete(gameId);
            tx.oncomplete = function () {
                LocalRecordIdbSend(go, method, 'ok');
            };
            tx.onerror = function () {
                LocalRecordIdbSend(go, method, 'ok');
            };
        });
    }
};

autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbState');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbHeap');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbSend');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbCopyBytes');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbToBuffer');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbOpen');
mergeInto(LibraryManager.library, LocalRecordIndexedDB);
