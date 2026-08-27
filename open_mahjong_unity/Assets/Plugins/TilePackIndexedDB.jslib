var TilePackIndexedDB = {
    $TilePackIdbState: {
        dbName: 'omu.unityAssets',
        storeName: 'tileBlobs',
        zipKey: 'standardZip',
        zipBytes: null,
        assetBytes: null,
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
        var request = indexedDB.open(TilePackIdbState.dbName, 1);
        request.onupgradeneeded = function () {
            var db = request.result;
            if (!db.objectStoreNames.contains(TilePackIdbState.storeName)) {
                db.createObjectStore(TilePackIdbState.storeName);
            }
        };
        request.onsuccess = function () {
            TilePackIdbState.db = request.result;
            callback(TilePackIdbState.db);
        };
        request.onerror = function () {
            callback(null);
        };
        request.onblocked = function () {
            callback(TilePackIdbState.db || null);
        };
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
                TilePackIdbOpen(function (db) {
                    if (!db) {
                        TilePackIdbSend(go, method, okMsg);
                        return;
                    }
                    var tx = db.transaction(TilePackIdbState.storeName, 'readwrite');
                    tx.objectStore(TilePackIdbState.storeName).put(stored, TilePackIdbState.zipKey);
                    tx.oncomplete = function () {
                        TilePackIdbSend(go, method, okMsg);
                    };
                    tx.onerror = function () {
                        TilePackIdbSend(go, method, 'error|IndexedDB 写入失败');
                    };
                });
            };
            reader.onerror = function () {
                TilePackIdbSend(go, method, 'error|读取文件失败');
            };
            reader.readAsArrayBuffer(file);
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

    UnityAssetIdbPickAndPut: function (keyOrPrefixPtr, acceptPtr, goPtr, methodPtr) {
        var keyOrPrefix = UTF8ToString(keyOrPrefixPtr);
        var accept = UTF8ToString(acceptPtr) || 'image/png,image/jpeg,image/jpg,image/webp';
        var go = UTF8ToString(goPtr);
        var method = UTF8ToString(methodPtr);
        TilePackIdbPickFile(accept, function (file) {
            if (!file) {
                TilePackIdbSend(go, method, 'cancel');
                return;
            }
            var reader = new FileReader();
            reader.onload = function () {
                var stored = TilePackIdbCloneBuffer(reader.result);
                var key = keyOrPrefix;
                if (key.charAt(key.length - 1) === '/') {
                    var dot = file.name.lastIndexOf('.');
                    var ext = dot >= 0 ? file.name.substring(dot) : '.png';
                    key = keyOrPrefix + 'item_' + Date.now() + ext;
                }
                TilePackIdbState.assetBytes = stored;
                var okMsg = 'ok|' + stored.byteLength + '|' + key;
                TilePackIdbOpen(function (db) {
                    if (!db) {
                        TilePackIdbSend(go, method, okMsg);
                        return;
                    }
                    var tx = db.transaction(TilePackIdbState.storeName, 'readwrite');
                    tx.objectStore(TilePackIdbState.storeName).put(stored, key);
                    tx.oncomplete = function () {
                        TilePackIdbSend(go, method, okMsg);
                    };
                    tx.onerror = function () {
                        TilePackIdbSend(go, method, 'error|IndexedDB 写入失败');
                    };
                });
            };
            reader.onerror = function () {
                TilePackIdbSend(go, method, 'error|读取文件失败');
            };
            reader.readAsArrayBuffer(file);
        });
    },

    UnityAssetIdbPut: function (keyPtr, dataPtr, length, goPtr, methodPtr) {
        var key = UTF8ToString(keyPtr);
        var go = UTF8ToString(goPtr);
        var method = UTF8ToString(methodPtr);
        var heap = TilePackIdbHeap();
        var copy = new Uint8Array(length);
        copy.set(heap.subarray(dataPtr, dataPtr + length));
        var stored = copy.buffer;
        TilePackIdbState.assetBytes = stored;
        TilePackIdbOpen(function (db) {
            if (!db) {
                TilePackIdbSend(go, method, 'ok|' + length + '|' + key);
                return;
            }
            var tx = db.transaction(TilePackIdbState.storeName, 'readwrite');
            tx.objectStore(TilePackIdbState.storeName).put(stored, key);
            tx.oncomplete = function () {
                TilePackIdbSend(go, method, 'ok|' + length + '|' + key);
            };
            tx.onerror = function () {
                TilePackIdbSend(go, method, 'error|IndexedDB 写入失败');
            };
        });
    },

    UnityAssetIdbGet: function (keyPtr, goPtr, methodPtr) {
        var key = UTF8ToString(keyPtr);
        var go = UTF8ToString(goPtr);
        var method = UTF8ToString(methodPtr);
        TilePackIdbOpen(function (db) {
            if (!db) {
                TilePackIdbSend(go, method, 'empty');
                return;
            }
            var tx = db.transaction(TilePackIdbState.storeName, 'readonly');
            var request = tx.objectStore(TilePackIdbState.storeName).get(key);
            request.onsuccess = function () {
                TilePackIdbToBuffer(request.result, function (buffer) {
                    if (!buffer) {
                        TilePackIdbSend(go, method, 'empty');
                        return;
                    }
                    TilePackIdbState.assetBytes = buffer;
                    TilePackIdbSend(go, method, 'ok|' + buffer.byteLength + '|' + key);
                });
            };
            request.onerror = function () {
                TilePackIdbSend(go, method, 'error|IndexedDB 读取失败');
            };
        });
    },

    UnityAssetIdbCopy: function (dstPtr, maxLen) {
        if (!TilePackIdbState.assetBytes || maxLen <= 0) {
            return 0;
        }
        var source = new Uint8Array(TilePackIdbState.assetBytes);
        var n = source.length < maxLen ? source.length : maxLen;
        try {
            TilePackIdbHeap().set(source.subarray(0, n), dstPtr);
        } catch (e) {
            return 0;
        }
        return n;
    },

    UnityAssetIdbDelete: function (keyPtr, goPtr, methodPtr) {
        var key = UTF8ToString(keyPtr);
        var go = UTF8ToString(goPtr);
        var method = UTF8ToString(methodPtr);
        TilePackIdbOpen(function (db) {
            if (!db) {
                TilePackIdbSend(go, method, 'ok');
                return;
            }
            var tx = db.transaction(TilePackIdbState.storeName, 'readwrite');
            tx.objectStore(TilePackIdbState.storeName).delete(key);
            tx.oncomplete = function () {
                TilePackIdbSend(go, method, 'ok');
            };
            tx.onerror = function () {
                TilePackIdbSend(go, method, 'error|IndexedDB 删除失败');
            };
        });
    },

    UnityAssetIdbLoadAll: function (goPtr, methodPtr) {
        var go = UTF8ToString(goPtr);
        var method = UTF8ToString(methodPtr);
        TilePackIdbOpen(function (db) {
            if (!db) {
                TilePackIdbSend(go, method, 'empty');
                return;
            }
            var tx = db.transaction(TilePackIdbState.storeName, 'readonly');
            var store = tx.objectStore(TilePackIdbState.storeName);
            var emitPacked = function (pending) {
                var entries = [];
                var index = 0;
                var next = function () {
                    if (index >= pending.length) {
                        var packed = TilePackIdbPackEntries(entries);
                        TilePackIdbState.assetBytes = packed;
                        if (!packed || packed.byteLength <= 6) {
                            TilePackIdbSend(go, method, 'empty');
                            return;
                        }
                        TilePackIdbSend(go, method, 'ok|' + packed.byteLength);
                        return;
                    }
                    var item = pending[index++];
                    TilePackIdbToBuffer(item.value, function (buffer) {
                        if (buffer) {
                            entries.push({ key: item.key, buffer: buffer });
                        }
                        next();
                    });
                };
                next();
            };
            if (typeof store.getAllKeys === 'function' && typeof store.getAll === 'function') {
                var keysReq = store.getAllKeys();
                var valsReq = store.getAll();
                var keys = null;
                var vals = null;
                var finish = function () {
                    if (!keys || !vals) {
                        return;
                    }
                    var pending = [];
                    for (var i = 0; i < keys.length; i++) {
                        var key = String(keys[i]);
                        if (key === TilePackIdbState.zipKey) {
                            continue;
                        }
                        pending.push({ key: key, value: vals[i] });
                    }
                    emitPacked(pending);
                };
                keysReq.onsuccess = function () {
                    keys = keysReq.result || [];
                    finish();
                };
                valsReq.onsuccess = function () {
                    vals = valsReq.result || [];
                    finish();
                };
                keysReq.onerror = function () {
                    TilePackIdbSend(go, method, 'error|IndexedDB 列举失败');
                };
                valsReq.onerror = function () {
                    TilePackIdbSend(go, method, 'error|IndexedDB 读取失败');
                };
                return;
            }
            var pending = [];
            var cursorReq = store.openCursor();
            cursorReq.onsuccess = function (event) {
                var cursor = event.target.result;
                if (cursor) {
                    var key = String(cursor.key);
                    if (key !== TilePackIdbState.zipKey) {
                        pending.push({ key: key, value: cursor.value });
                    }
                    cursor.continue();
                    return;
                }
                emitPacked(pending);
            };
            cursorReq.onerror = function () {
                TilePackIdbSend(go, method, 'error|IndexedDB 列举失败');
            };
        });
    },

    $TilePackIdbPackEntries: function (entries) {
        var encoder = new TextEncoder();
        var total = 6;
        var encodedKeys = [];
        for (var i = 0; i < entries.length; i++) {
            var keyBytes = encoder.encode(entries[i].key);
            encodedKeys.push(keyBytes);
            total += 2 + keyBytes.length + 4 + entries[i].buffer.byteLength;
        }
        var out = new Uint8Array(total);
        var view = new DataView(out.buffer);
        out[0] = 79; out[1] = 77; out[2] = 65; out[3] = 66;
        view.setUint16(4, entries.length, true);
        var offset = 6;
        for (var j = 0; j < entries.length; j++) {
            var kb = encodedKeys[j];
            view.setUint16(offset, kb.length, true);
            offset += 2;
            out.set(kb, offset);
            offset += kb.length;
            var data = new Uint8Array(entries[j].buffer);
            view.setUint32(offset, data.length, true);
            offset += 4;
            out.set(data, offset);
            offset += data.length;
        }
        return out.buffer;
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
            var reader = new FileReader();
            reader.onload = function () {
                var stored = TilePackIdbCloneBuffer(reader.result);
                TilePackIdbState.assetBytes = stored;
                TilePackIdbOpen(function (db) {
                    if (!db) {
                        SendMessage(go, method, 'ok|' + stored.byteLength + '|' + key);
                        return;
                    }
                    var tx = db.transaction(TilePackIdbState.storeName, 'readwrite');
                    tx.objectStore(TilePackIdbState.storeName).put(stored, key);
                    tx.oncomplete = function () {
                        SendMessage(go, method, 'ok|' + stored.byteLength + '|' + key);
                    };
                    tx.onerror = function () {
                        SendMessage(go, method, 'error|IndexedDB 写入失败');
                    };
                });
            };
            reader.readAsArrayBuffer(file);
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
autoAddDeps(TilePackIndexedDB, '$TilePackIdbPackEntries');
mergeInto(LibraryManager.library, TilePackIndexedDB);
