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
    },

    $TilePackIdbSend: function (goPtr, methodPtr, message) {
        var go = UTF8ToString(goPtr);
        var method = UTF8ToString(methodPtr);
        SendMessage(go, method, message);
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

    TilePackIdbPickZip: function (goPtr, methodPtr) {
        var input = document.createElement('input');
        input.type = 'file';
        input.accept = '.zip,application/zip';
        input.style.display = 'none';
        input.onchange = function (event) {
            var file = event.target.files && event.target.files[0];
            document.body.removeChild(input);
            if (!file) {
                TilePackIdbSend(goPtr, methodPtr, 'cancel');
                return;
            }
            var reader = new FileReader();
            reader.onload = function () {
                var buffer = reader.result;
                TilePackIdbState.zipBytes = buffer;
                TilePackIdbOpen(function (db) {
                    if (!db) {
                        TilePackIdbSend(goPtr, methodPtr, 'ok|' + buffer.byteLength);
                        return;
                    }
                    var tx = db.transaction(TilePackIdbState.storeName, 'readwrite');
                    tx.objectStore(TilePackIdbState.storeName).put(buffer, TilePackIdbState.zipKey);
                    tx.oncomplete = function () {
                        TilePackIdbSend(goPtr, methodPtr, 'ok|' + buffer.byteLength);
                    };
                    tx.onerror = function () {
                        TilePackIdbSend(goPtr, methodPtr, 'error|IndexedDB 写入失败');
                    };
                });
            };
            reader.onerror = function () {
                TilePackIdbSend(goPtr, methodPtr, 'error|读取文件失败');
            };
            reader.readAsArrayBuffer(file);
        };
        document.body.appendChild(input);
        setTimeout(function () { input.click(); }, 0);
    },

    TilePackIdbLoadZip: function (goPtr, methodPtr) {
        TilePackIdbOpen(function (db) {
            if (!db) {
                TilePackIdbSend(goPtr, methodPtr, 'empty');
                return;
            }
            var tx = db.transaction(TilePackIdbState.storeName, 'readonly');
            var request = tx.objectStore(TilePackIdbState.storeName).get(TilePackIdbState.zipKey);
            request.onsuccess = function () {
                TilePackIdbToBuffer(request.result, function (buffer) {
                    if (!buffer) {
                        TilePackIdbSend(goPtr, methodPtr, 'empty');
                        return;
                    }
                    TilePackIdbState.zipBytes = buffer;
                    TilePackIdbSend(goPtr, methodPtr, 'ok|' + buffer.byteLength);
                });
            };
            request.onerror = function () {
                TilePackIdbSend(goPtr, methodPtr, 'error|IndexedDB 读取失败');
            };
        });
    },

    TilePackIdbCopyZip: function (dstPtr, maxLen) {
        if (!TilePackIdbState.zipBytes || maxLen <= 0) {
            return 0;
        }
        var source = new Uint8Array(TilePackIdbState.zipBytes);
        var n = source.length < maxLen ? source.length : maxLen;
        TilePackIdbHeap().set(source.subarray(0, n), dstPtr);
        return n;
    },

    TilePackIdbHasZip: function () {
        return TilePackIdbState.zipBytes ? 1 : 0;
    },

    TilePackIdbClear: function (goPtr, methodPtr) {
        TilePackIdbState.zipBytes = null;
        TilePackIdbOpen(function (db) {
            if (!db) {
                if (goPtr && methodPtr) TilePackIdbSend(goPtr, methodPtr, 'ok');
                return;
            }
            var tx = db.transaction(TilePackIdbState.storeName, 'readwrite');
            tx.objectStore(TilePackIdbState.storeName).delete(TilePackIdbState.zipKey);
            tx.oncomplete = function () {
                if (goPtr && methodPtr) TilePackIdbSend(goPtr, methodPtr, 'ok');
            };
            tx.onerror = function () {
                if (goPtr && methodPtr) TilePackIdbSend(goPtr, methodPtr, 'error|IndexedDB 删除失败');
            };
        });
    },

    UnityAssetIdbPickAndPut: function (keyOrPrefixPtr, acceptPtr, goPtr, methodPtr) {
        var keyOrPrefix = UTF8ToString(keyOrPrefixPtr);
        var accept = UTF8ToString(acceptPtr) || 'image/png,image/jpeg,image/jpg,image/webp';
        var input = document.createElement('input');
        input.type = 'file';
        input.accept = accept;
        input.style.display = 'none';
        input.onchange = function (event) {
            var file = event.target.files && event.target.files[0];
            document.body.removeChild(input);
            if (!file) {
                TilePackIdbSend(goPtr, methodPtr, 'cancel');
                return;
            }
            var reader = new FileReader();
            reader.onload = function () {
                var buffer = reader.result;
                var key = keyOrPrefix;
                if (key.charAt(key.length - 1) === '/') {
                    var dot = file.name.lastIndexOf('.');
                    var ext = dot >= 0 ? file.name.substring(dot) : '.png';
                    key = keyOrPrefix + 'item_' + Date.now() + ext;
                }
                TilePackIdbState.assetBytes = buffer;
                TilePackIdbOpen(function (db) {
                    if (!db) {
                        TilePackIdbSend(goPtr, methodPtr, 'ok|' + buffer.byteLength + '|' + key);
                        return;
                    }
                    var tx = db.transaction(TilePackIdbState.storeName, 'readwrite');
                    tx.objectStore(TilePackIdbState.storeName).put(buffer, key);
                    tx.oncomplete = function () {
                        TilePackIdbSend(goPtr, methodPtr, 'ok|' + buffer.byteLength + '|' + key);
                    };
                    tx.onerror = function () {
                        TilePackIdbSend(goPtr, methodPtr, 'error|IndexedDB 写入失败');
                    };
                });
            };
            reader.onerror = function () {
                TilePackIdbSend(goPtr, methodPtr, 'error|读取文件失败');
            };
            reader.readAsArrayBuffer(file);
        };
        document.body.appendChild(input);
        setTimeout(function () { input.click(); }, 0);
    },

    UnityAssetIdbPut: function (keyPtr, dataPtr, length, goPtr, methodPtr) {
        var key = UTF8ToString(keyPtr);
        var heap = TilePackIdbHeap();
        var copy = new Uint8Array(length);
        copy.set(heap.subarray(dataPtr, dataPtr + length));
        TilePackIdbState.assetBytes = copy.buffer;
        TilePackIdbOpen(function (db) {
            if (!db) {
                TilePackIdbSend(goPtr, methodPtr, 'ok|' + length + '|' + key);
                return;
            }
            var tx = db.transaction(TilePackIdbState.storeName, 'readwrite');
            tx.objectStore(TilePackIdbState.storeName).put(copy.buffer, key);
            tx.oncomplete = function () {
                TilePackIdbSend(goPtr, methodPtr, 'ok|' + length + '|' + key);
            };
            tx.onerror = function () {
                TilePackIdbSend(goPtr, methodPtr, 'error|IndexedDB 写入失败');
            };
        });
    },

    UnityAssetIdbGet: function (keyPtr, goPtr, methodPtr) {
        var key = UTF8ToString(keyPtr);
        TilePackIdbOpen(function (db) {
            if (!db) {
                TilePackIdbSend(goPtr, methodPtr, 'empty');
                return;
            }
            var tx = db.transaction(TilePackIdbState.storeName, 'readonly');
            var request = tx.objectStore(TilePackIdbState.storeName).get(key);
            request.onsuccess = function () {
                TilePackIdbToBuffer(request.result, function (buffer) {
                    if (!buffer) {
                        TilePackIdbSend(goPtr, methodPtr, 'empty');
                        return;
                    }
                    TilePackIdbState.assetBytes = buffer;
                    TilePackIdbSend(goPtr, methodPtr, 'ok|' + buffer.byteLength + '|' + key);
                });
            };
            request.onerror = function () {
                TilePackIdbSend(goPtr, methodPtr, 'error|IndexedDB 读取失败');
            };
        });
    },

    UnityAssetIdbCopy: function (dstPtr, maxLen) {
        if (!TilePackIdbState.assetBytes || maxLen <= 0) {
            return 0;
        }
        var source = new Uint8Array(TilePackIdbState.assetBytes);
        var n = source.length < maxLen ? source.length : maxLen;
        TilePackIdbHeap().set(source.subarray(0, n), dstPtr);
        return n;
    },

    UnityAssetIdbDelete: function (keyPtr, goPtr, methodPtr) {
        var key = UTF8ToString(keyPtr);
        TilePackIdbOpen(function (db) {
            if (!db) {
                TilePackIdbSend(goPtr, methodPtr, 'ok');
                return;
            }
            var tx = db.transaction(TilePackIdbState.storeName, 'readwrite');
            tx.objectStore(TilePackIdbState.storeName).delete(key);
            tx.oncomplete = function () {
                TilePackIdbSend(goPtr, methodPtr, 'ok');
            };
            tx.onerror = function () {
                TilePackIdbSend(goPtr, methodPtr, 'error|IndexedDB 删除失败');
            };
        });
    },

    UnityAssetIdbLoadAll: function (goPtr, methodPtr) {
        TilePackIdbOpen(function (db) {
            if (!db) {
                TilePackIdbSend(goPtr, methodPtr, 'empty');
                return;
            }
            var tx = db.transaction(TilePackIdbState.storeName, 'readonly');
            var store = tx.objectStore(TilePackIdbState.storeName);
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
                var entries = [];
                var index = 0;
                var next = function () {
                    if (index >= pending.length) {
                        var packed = TilePackIdbPackEntries(entries);
                        TilePackIdbState.assetBytes = packed;
                        if (!packed || packed.byteLength <= 6) {
                            TilePackIdbSend(goPtr, methodPtr, 'empty');
                            return;
                        }
                        TilePackIdbSend(goPtr, methodPtr, 'ok|' + packed.byteLength);
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
            keysReq.onsuccess = function () {
                keys = keysReq.result || [];
                finish();
            };
            valsReq.onsuccess = function () {
                vals = valsReq.result || [];
                finish();
            };
            keysReq.onerror = function () {
                TilePackIdbSend(goPtr, methodPtr, 'error|IndexedDB 列举失败');
            };
            valsReq.onerror = function () {
                TilePackIdbSend(goPtr, methodPtr, 'error|IndexedDB 读取失败');
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
                var buffer = reader.result;
                TilePackIdbState.assetBytes = buffer;
                TilePackIdbOpen(function (db) {
                    if (!db) {
                        SendMessage(go, method, 'ok|' + buffer.byteLength + '|' + key);
                        return;
                    }
                    var tx = db.transaction(TilePackIdbState.storeName, 'readwrite');
                    tx.objectStore(TilePackIdbState.storeName).put(buffer, key);
                    tx.oncomplete = function () {
                        SendMessage(go, method, 'ok|' + buffer.byteLength + '|' + key);
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
autoAddDeps(TilePackIndexedDB, '$TilePackIdbToBuffer');
autoAddDeps(TilePackIndexedDB, '$TilePackIdbHeap');
autoAddDeps(TilePackIndexedDB, '$TilePackIdbPackEntries');
mergeInto(LibraryManager.library, TilePackIndexedDB);
