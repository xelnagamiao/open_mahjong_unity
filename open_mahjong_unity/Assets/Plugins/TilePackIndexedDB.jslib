var TilePackIndexedDB = {
    $TilePackIdbState: {
        dbName: 'omu.unityAssets',
        storeName: 'tileBlobs',
        zipKey: 'standardZip',
        zipBytes: null,
        transfers: Object.create(null),
        nextTransfer: 0,
        operations: [],
        busy: false,
        db: null
    },

    $TilePackIdbOpen: function (callback) {
        if (TilePackIdbState.db) {
            callback(TilePackIdbState.db);
            return;
        }
        if (typeof indexedDB === 'undefined') {
            callback(null);
            return;
        }
        var request, settled = false;
        try { request = indexedDB.open(TilePackIdbState.dbName, 1); }
        catch (e) { callback(null); return; }
        request.onupgradeneeded = function () {
            var db = request.result;
            if (!db.objectStoreNames.contains(TilePackIdbState.storeName)) {
                db.createObjectStore(TilePackIdbState.storeName);
            }
        };
        function finish(db) { if (!settled) { settled = true; callback(db); } }
        request.onsuccess = function() {
            var db = request.result;
            if (settled) { db.close(); return; }
            TilePackIdbState.db = db;
            db.onversionchange = function() { db.close(); if (TilePackIdbState.db === db) TilePackIdbState.db = null; };
            finish(db);
        };
        request.onerror = request.onblocked = function() { finish(null); };
    },

    $TilePackIdbSend: function (go, method, message) {
        SendMessage(go, method, message);
    },

    $TilePackIdbCloneBuffer: function (buffer) {
        if (!buffer) {
            return null;
        }
        if (buffer instanceof ArrayBuffer) {
            return buffer.slice(0);
        }
        if (buffer.buffer && typeof buffer.byteLength === 'number') {
            return buffer.buffer.slice(buffer.byteOffset, buffer.byteOffset + buffer.byteLength);
        }
        return buffer;
    },

    $TilePackIdbToBuffer: function (value, done) {
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
        if (value.buffer) {
            done(value.buffer);
            return;
        }
        done(null);
    },

    $TilePackIdbHeap: function () {
        return (typeof HEAPU8 !== 'undefined') ? HEAPU8 : Module.HEAPU8;
    },

    $TilePackIdbIsTouch: function () {
        try {
            return ('ontouchstart' in window) || (navigator.maxTouchPoints > 0);
        } catch (e) {
            return false;
        }
    },

    $TilePackIdbPickFile: function (accept, onPicked) {
        var input = document.createElement('input');
        input.type = 'file';
        input.multiple = false;
        var touch = TilePackIdbIsTouch();
        if (touch && accept && /zip/i.test(accept)) {
            input.accept = '*/*';
        } else if (accept) {
            input.accept = accept;
        }
        // iOS：display:none 会拦截系统文件选择；保持在文档内且几乎透明。
        input.style.cssText = 'position:fixed;left:0;top:0;width:100%;height:100%;opacity:0.01;z-index:2147483647;border:0;padding:0;margin:0;';
        var settled = false;
        var cleanup = function () {
            window.removeEventListener('focus', onWindowFocus);
            if (input && input.parentNode) {
                input.parentNode.removeChild(input);
            }
        };
        var finish = function (file) {
            if (settled) {
                return;
            }
            settled = true;
            cleanup();
            onPicked(file || null);
        };
        var onWindowFocus = function () {
            setTimeout(function () {
                if (!settled && (!input.files || input.files.length === 0)) {
                    finish(null);
                }
            }, 400);
        };
        input.onchange = function (event) {
            var file = event.target.files && event.target.files[0];
            finish(file || null);
        };
        input.addEventListener('cancel', function () {
            finish(null);
        });
        document.body.appendChild(input);
        // 必须在用户点击的同步栈里 click，iOS 才能弹出选择器。
        input.click();
        setTimeout(function () {
            window.addEventListener('focus', onWindowFocus);
        }, 300);
    },

    TilePackIdbPickZip: function (goPtr, methodPtr) {
        var go = UTF8ToString(goPtr);
        var method = UTF8ToString(methodPtr);
        TilePackIdbPickFile('.zip,application/zip,application/x-zip-compressed', function (file) {
            if (!file) {
                TilePackIdbSend(go, method, 'cancel');
                return;
            }
            var reader = new FileReader();
            reader.onload = function () {
                var stored = TilePackIdbCloneBuffer(reader.result);
                TilePackIdbState.zipBytes = stored;
                var safeName = (file.name || '').replace(/\|/g, '/');
                var okMsg = 'ok|' + stored.byteLength + '|' + safeName;
                // Validate in Unity first; a cancelled or invalid import never overwrites a saved pack.
                TilePackIdbSend(go, method, okMsg);
            };
            reader.onerror = function () {
                TilePackIdbSend(go, method, 'error|读取文件失败');
            };
            reader.readAsArrayBuffer(file);
        });
    },

    TilePackIdbSaveLibraryZip: function (keyPtr, dataPtr, length, goPtr) {
        var key = UTF8ToString(keyPtr), go = UTF8ToString(goPtr);
        var stored = TilePackIdbHeap().slice(dataPtr, dataPtr + length).buffer;
        TilePackIdbOpen(function (db) {
            if (!db) { TilePackIdbSend(go, 'OnResult', 'error|IndexedDB 不可用，牌组未保存'); return; }
            try {
                var tx = db.transaction(TilePackIdbState.storeName, 'readwrite');
                tx.objectStore(TilePackIdbState.storeName).put(stored, key);
                tx.oncomplete = function () { TilePackIdbSend(go, 'OnResult', 'ok'); };
                tx.onabort = function () { TilePackIdbSend(go, 'OnResult', 'error|牌组保存失败'); };
            } catch (e) { TilePackIdbSend(go, 'OnResult', 'error|' + e.message); }
        });
    },

    TilePackIdbDeleteLibraryZip: function (keyPtr, goPtr) {
        var key = UTF8ToString(keyPtr), go = UTF8ToString(goPtr);
        if (key !== 'standardZip' && !/^tilepack\/custom-[0-9a-f]{32}$/.test(key)) {
            TilePackIdbSend(go, 'OnResult', 'error|无效牌面'); return;
        }
        var settled = false;
        var finish = function (message) {
            if (settled) return;
            settled = true;
            TilePackIdbSend(go, 'OnResult', message);
        };
        TilePackIdbOpen(function (db) {
            if (settled) return;
            if (!db) { finish('error|IndexedDB 不可用，牌面未删除'); return; }
            try {
                var tx = db.transaction(TilePackIdbState.storeName, 'readwrite');
                tx.objectStore(TilePackIdbState.storeName).delete(key);
                // Publish success only when the transaction committed, not on request success.
                tx.oncomplete = function () { finish('ok'); };
                tx.onabort = function () { finish('error|牌面删除失败'); };
            } catch (e) { finish('error|' + e.message); }
        });
    },

    TilePackIdbLoadLibraryZip: function (keyPtr, goPtr) {
        var key = UTF8ToString(keyPtr), go = UTF8ToString(goPtr);
        TilePackIdbOpen(function (db) {
            if (!db) { TilePackIdbSend(go, 'OnResult', 'error|IndexedDB 不可用'); return; }
            try {
                var tx = db.transaction(TilePackIdbState.storeName, 'readonly');
                var request = tx.objectStore(TilePackIdbState.storeName).get(key);
                request.onsuccess = function () {
                    TilePackIdbToBuffer(request.result, function (buffer) {
                        if (!buffer) { TilePackIdbSend(go, 'OnResult', 'empty'); return; }
                        TilePackIdbState.zipBytes = buffer;
                        TilePackIdbSend(go, 'OnResult', 'ok|' + buffer.byteLength);
                    });
                };
                request.onerror = function () { TilePackIdbSend(go, 'OnResult', 'error|读取牌组失败'); };
            } catch (e) { TilePackIdbSend(go, 'OnResult', 'error|' + e.message); }
        });
    },

    TilePackIdbLoadZip: function (goPtr, methodPtr) {
        var go = UTF8ToString(goPtr);
        var method = UTF8ToString(methodPtr);
        TilePackIdbOpen(function (db) {
            if (!db) {
                TilePackIdbSend(go, method, 'empty');
                return;
            }
            var tx = db.transaction(TilePackIdbState.storeName, 'readonly');
            var request = tx.objectStore(TilePackIdbState.storeName).get(TilePackIdbState.zipKey);
            request.onsuccess = function () {
                TilePackIdbToBuffer(request.result, function (buffer) {
                    if (!buffer) {
                        TilePackIdbSend(go, method, 'empty');
                        return;
                    }
                    TilePackIdbState.zipBytes = buffer;
                    TilePackIdbSend(go, method, 'ok|' + buffer.byteLength);
                });
            };
            request.onerror = function () {
                TilePackIdbSend(go, method, 'error|IndexedDB 读取失败');
            };
        });
    },

    TilePackIdbCopyZip: function (dstPtr, maxLen) {
        if (!TilePackIdbState.zipBytes || maxLen <= 0) {
            return 0;
        }
        var source = new Uint8Array(TilePackIdbState.zipBytes);
        var n = source.length < maxLen ? source.length : maxLen;
        try {
            TilePackIdbHeap().set(source.subarray(0, n), dstPtr);
        } catch (e) {
            return 0;
        }
        return n;
    },

    TilePackIdbHasZip: function () {
        return TilePackIdbState.zipBytes ? 1 : 0;
    },

    TilePackIdbClear: function (goPtr, methodPtr) {
        var go = UTF8ToString(goPtr);
        var method = UTF8ToString(methodPtr);
        TilePackIdbState.zipBytes = null;
        TilePackIdbOpen(function (db) {
            if (!db) {
                TilePackIdbSend(go, method, 'ok');
                return;
            }
            var tx = db.transaction(TilePackIdbState.storeName, 'readwrite');
            tx.objectStore(TilePackIdbState.storeName).delete(TilePackIdbState.zipKey);
            tx.oncomplete = function () {
                TilePackIdbSend(go, method, 'ok');
            };
            tx.onerror = function () {
                TilePackIdbSend(go, method, 'error|IndexedDB 删除失败');
            };
        });
    },

    $UnityAssetIdbSendBytes: function (go, method, key, bytes) {
        var token = ++TilePackIdbState.nextTransfer;
        TilePackIdbState.transfers[token] = bytes;
        try {
            TilePackIdbSend(go, method, 'ok|' + bytes.byteLength + '|' + token + '|' + key);
        } catch (error) {
            delete TilePackIdbState.transfers[token];
            throw error;
        }
    },

    $UnityAssetIdbEnqueue: function(operation, go, method) {
        var state = TilePackIdbState;
        function next() {
            state.busy = state.operations.length > 0;
            if (state.busy) state.operations.shift()();
        }
        state.operations.push(function() {
            var sent = false;
            function finish(message, key, bytes) {
                if (sent) return;
                sent = true;
                try {
                    if (bytes) UnityAssetIdbSendBytes(go, method, key, bytes);
                    else TilePackIdbSend(go, method, message);
                } finally { next(); }
            }
            try { operation(finish); } catch (e) { finish('error|' + e.message); }
        });
        if (!state.busy) next();
    },

    $UnityAssetIdbReceiveFile: function (file, key, go, method) {
        if (!file) { TilePackIdbSend(go, method, 'cancel'); return; }
        var maxBytes = (key === '@read-only' ? 128 : 8) * 1024 * 1024;
        if (file.size > maxBytes) { TilePackIdbSend(go, method, 'error|文件超过大小限制'); return; }
        UnityAssetIdbEnqueue(function(finish) {
            var reader = new FileReader();
            reader.onerror = function () { finish('error|读取文件失败'); };
            reader.onload = function () {
                var bytes = new Uint8Array(reader.result);
                if (key === '@read-only') { finish(null, file.name, bytes); return; }
                if (key.endsWith('/')) {
                    var dot = file.name.lastIndexOf('.');
                    key += 'item_' + Date.now() + (dot >= 0 ? file.name.substring(dot) : '.png');
                }
                TilePackIdbOpen(function (db) {
                    if (!db) { finish('error|IndexedDB 不可用'); return; }
                    try {
                        var tx = db.transaction(TilePackIdbState.storeName, 'readwrite');
                        tx.objectStore(TilePackIdbState.storeName).put(bytes.buffer, key);
                        tx.oncomplete = function () { finish(null, key, bytes); };
                        tx.onabort = function () { finish('error|IndexedDB 写入失败'); };
                    } catch (e) { finish('error|' + e.message); }
                });
            };
            reader.readAsArrayBuffer(file);
        }, go, method);
    },

    UnityAssetIdbPickAndPut: function (keyPtr, acceptPtr, goPtr, methodPtr) {
        var key = UTF8ToString(keyPtr), go = UTF8ToString(goPtr), method = UTF8ToString(methodPtr);
        TilePackIdbPickFile(UTF8ToString(acceptPtr), function (file) { UnityAssetIdbReceiveFile(file, key, go, method); });
    },

    UnityAssetIdbPut: function (keyPtr, dataPtr, length, goPtr, methodPtr) {
        var key = UTF8ToString(keyPtr);
        var go = UTF8ToString(goPtr);
        var method = UTF8ToString(methodPtr);
        UnityAssetIdbEnqueue(function(finish) {
            TilePackIdbOpen(function(db) {
                if (!db) { finish('error|IndexedDB 不可用'); return; }
                try {
                    var tx = db.transaction(TilePackIdbState.storeName, 'readwrite');
                    var copy = TilePackIdbHeap().slice(dataPtr, dataPtr + length);
                    tx.objectStore(TilePackIdbState.storeName).put(copy.buffer, key);
                    tx.oncomplete = function() { finish('ok'); };
                    tx.onabort = function() { finish('error|IndexedDB 写入失败'); };
                } catch (e) { finish('error|' + e.message); }
            });
        }, go, method);
    },

    UnityAssetIdbGet: function (keyPtr, limit, goPtr, methodPtr) {
        var key = UTF8ToString(keyPtr), go = UTF8ToString(goPtr), method = UTF8ToString(methodPtr);
        UnityAssetIdbEnqueue(function(finish) {
            TilePackIdbOpen(function(db) {
                if (!db) { finish('error|IndexedDB 不可用'); return; }
                try {
                    var tx = db.transaction(TilePackIdbState.storeName), req = tx.objectStore(TilePackIdbState.storeName).get(key);
                    tx.onabort = function() { finish('error|IndexedDB 读取失败'); };
                    req.onsuccess = function() {
                        var value = req.result;
                        if (value === undefined) { finish('empty'); return; }
                        var size = value instanceof Blob ? value.size : value && value.byteLength;
                        if (!(size >= 0)) { finish('error|未知的素材存储格式'); return; }
                        var count = limit > 0 ? Math.min(limit, size) : size;
                        if (count > 8 * 1024 * 1024) { finish('error|资源超过 8MB'); return; }
                        if (value instanceof Blob) {
                            TilePackIdbToBuffer(value.slice(0, count), function(buf) {
                                if (buf) finish(null, key, new Uint8Array(buf)); else finish('error|图片读取失败');
                            });
                        } else {
                            var buffer = ArrayBuffer.isView(value) ? value.buffer : value;
                            finish(null, key, new Uint8Array(buffer, value.byteOffset || 0, count));
                        }
                    };
                } catch (e) { finish('error|' + e.message); }
            });
        }, go, method);
    },

    UnityAssetIdbCopy: function (token, dstPtr, length) {
        var bytes = TilePackIdbState.transfers[token];
        delete TilePackIdbState.transfers[token];
        if (!bytes || bytes.byteLength !== length) return 0;
        TilePackIdbHeap().set(bytes, dstPtr);
        return length;
    },

    UnityAssetIdbDelete: function (keyPtr, goPtr, methodPtr) {
        var key = UTF8ToString(keyPtr);
        var go = UTF8ToString(goPtr);
        var method = UTF8ToString(methodPtr);
        var settled = false;
        var finish = function (message) {
            if (settled) return;
            settled = true; TilePackIdbSend(go, method, message);
        };
        TilePackIdbOpen(function (db) {
            if (!db) {
                finish('error|IndexedDB 不可用，图片未删除');
                return;
            }
            try {
                var tx = db.transaction(TilePackIdbState.storeName, 'readwrite');
                tx.objectStore(TilePackIdbState.storeName).delete(key);
                tx.oncomplete = function () { finish('ok'); };
                tx.onabort = function () { finish('error|IndexedDB 删除失败'); };
            } catch (e) { finish('error|' + e.message); }
        });
    },

    UnityAssetIdbListKeys: function (prefixPtr, afterPtr, pageSize, goPtr, methodPtr) {
        var prefix = UTF8ToString(prefixPtr), after = UTF8ToString(afterPtr);
        var go = UTF8ToString(goPtr), method = UTF8ToString(methodPtr);
        TilePackIdbOpen(function (db) {
            if (!db) { TilePackIdbSend(go, method, 'error|IndexedDB 不可用'); return; }
            var keys = [], tx, req;
            try {
                tx = db.transaction(TilePackIdbState.storeName, 'readonly');
                var range = after ? IDBKeyRange.lowerBound(after, true) : prefix ? IDBKeyRange.lowerBound(prefix) : undefined;
                req = tx.objectStore(TilePackIdbState.storeName).openKeyCursor(range);
            } catch (e) { TilePackIdbSend(go, method, 'error|' + e.message); return; }
            req.onsuccess = function () {
                var cursor = req.result;
                if (!cursor) return;
                if (prefix && (typeof cursor.key !== 'string' || !cursor.key.startsWith(prefix))) return;
                if (typeof cursor.key === 'string' && cursor.key !== 'standardZip' && !cursor.key.startsWith('tilepack/')) keys.push(cursor.key);
                if (keys.length < pageSize) cursor.continue();
            };
            tx.oncomplete = function () { UnityAssetIdbSendBytes(go, method, '', new TextEncoder().encode(JSON.stringify(keys))); };
            tx.onabort = function () { TilePackIdbSend(go, method, 'error|IndexedDB 列举失败'); };
        });
    },

    UnityAssetIdbBindDrop: function (keyPtr, goPtr, methodPtr) {
        var canvas = document.getElementById('unity-canvas') ||
                     document.getElementById('canvas') ||
                     document.querySelector('canvas');
        if (!canvas) {
            return;
        }
        canvas.__unityAssetDropKey = UTF8ToString(keyPtr);
        canvas.__unityAssetDropGo = UTF8ToString(goPtr);
        canvas.__unityAssetDropMethod = UTF8ToString(methodPtr);
        if (canvas.__unityAssetDropBound) {
            return;
        }
        canvas.__unityAssetDropBound = true;
        canvas.addEventListener('dragover', function (e) {
            e.preventDefault();
            e.stopPropagation();
        }, false);
        canvas.addEventListener('drop', function (e) {
            e.preventDefault();
            e.stopPropagation();
            var file = e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0];
            if (!file) {
                return;
            }
            var key = canvas.__unityAssetDropKey;
            var go = canvas.__unityAssetDropGo;
            var method = canvas.__unityAssetDropMethod;
            if (!key || !go || !method) {
                return;
            }
            UnityAssetIdbReceiveFile(file, key, go, method);
        }, false);
    },

    UnityAssetIdbUnbindDrop: function () {
        var canvas = document.getElementById('unity-canvas') ||
                     document.getElementById('canvas') ||
                     document.querySelector('canvas');
        if (canvas) {
            canvas.__unityAssetDropKey = '';
        }
    }
};

autoAddDeps(TilePackIndexedDB, '$TilePackIdbState');
autoAddDeps(TilePackIndexedDB, '$TilePackIdbOpen');
autoAddDeps(TilePackIndexedDB, '$TilePackIdbSend');
autoAddDeps(TilePackIndexedDB, '$TilePackIdbCloneBuffer');
autoAddDeps(TilePackIndexedDB, '$TilePackIdbToBuffer');
autoAddDeps(TilePackIndexedDB, '$TilePackIdbHeap');
autoAddDeps(TilePackIndexedDB, '$TilePackIdbIsTouch');
autoAddDeps(TilePackIndexedDB, '$TilePackIdbPickFile');
autoAddDeps(TilePackIndexedDB, '$UnityAssetIdbSendBytes');
autoAddDeps(TilePackIndexedDB, '$UnityAssetIdbEnqueue');
autoAddDeps(TilePackIndexedDB, '$UnityAssetIdbReceiveFile');
mergeInto(LibraryManager.library, TilePackIndexedDB);
