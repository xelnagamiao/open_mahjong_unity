var LocalRecordIndexedDB = {
    $LocalRecordIdbState: {
        dbName: 'omu.localRecords',
        storeName: 'records',
        indexKey: '__index__',
        bytes: null,
        snapshots: {},
        nextSnapshot: 0,
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

    // The JSON values inside the snapshot remain opaque UTF-8 strings; JavaScript
    // never parses replay numbers (including 64-bit wall seeds).
    LocalRecordIdbSnapshot: function (keyPtr, goPtr, methodPtr) {
        var key = UTF8ToString(keyPtr), go = UTF8ToString(goPtr), method = UTF8ToString(methodPtr);
        var state = LocalRecordIdbState;
        LocalRecordIdbOpen(function (db) {
            if (!db) { LocalRecordIdbSend(go,method,'error|IndexedDB 不可用'); return; }
            var store = db.transaction(state.storeName,'readonly').objectStore(state.storeName);
            var values = Object.create(null), pending = [], all = !key;
            var selected = [state.indexKey, '__record_aliases__', key];
            var txn = store.transaction;
            function capture(k,v) {
                if (v == null) return;
                pending.push(new Promise(function(resolve,reject) {
                    LocalRecordIdbToBuffer(v,function(buf) {
                        if (buf == null) { reject(Error('牌谱存储类型无效')); return; }
                        values[k]=new TextDecoder().decode(buf); resolve();
                    });
                }));
            }
            if (all) {
                var cursor = store.openCursor();
                cursor.onsuccess = function() {
                    var c = cursor.result;
                    if (c) { capture(c.key,c.value); c.continue(); }
                };
            } else selected.forEach(function(k) {
                var request = store.get(k);
                request.onsuccess = function() { capture(k,request.result); };
            });
            txn.onabort=function(){LocalRecordIdbSend(go,method,'error|IndexedDB 读取失败');};
            txn.oncomplete=function(){
                Promise.all(pending).then(function(){
                    var token = ++state.nextSnapshot;
                    state.snapshots[token] = {values:values, all:all, selected:selected};
                    state.bytes = new TextEncoder().encode(JSON.stringify(values)).buffer;
                    LocalRecordIdbSend(go,method,'ok|'+state.bytes.byteLength+'|'+token);
                }).catch(function(e){ LocalRecordIdbSend(go,method,'error|'+e.message); });
            };
        });
    },

    LocalRecordIdbCommit: function (token, ptr, len, goPtr, methodPtr) {
        var state = LocalRecordIdbState, source = state.snapshots[token], go = UTF8ToString(goPtr), method = UTF8ToString(methodPtr);
        delete state.snapshots[token];
        if (!source) { LocalRecordIdbSend(go,method,'error|conflict'); return; }
        var plan = JSON.parse(new TextDecoder().decode(LocalRecordIdbCopyBytes(ptr,len)));
        LocalRecordIdbOpen(function(db) {
            if (!db) { LocalRecordIdbSend(go,method,'error|IndexedDB 不可用'); return; }
            var tx = db.transaction(state.storeName,'readwrite'), store = tx.objectStore(state.storeName);
            var reason='IndexedDB 写入失败', decoder = new TextDecoder(), encoder = new TextEncoder();
            var expected=source.values;
            tx.onabort=function(){LocalRecordIdbSend(go,method,'error|'+reason);};
            tx.oncomplete=function(){LocalRecordIdbSend(go,method,'ok');};
            function conflict(){reason='conflict'; tx.abort();}
            function write(){
                Object.keys(plan.writes).forEach(function(k){ store.put(encoder.encode(plan.writes[k]).buffer,k); });
                plan.deletes.forEach(function(k){ if (!Object.prototype.hasOwnProperty.call(plan.writes,k)) store.delete(k); });
            }
            function check(keys) {
                var i=0;
                function next() {
                    if(i===keys.length){write();return;}
                    var k=keys[i++], request=store.get(k);
                    request.onsuccess=function(){
                        var value=request.result;
                        if(value == null){ if(k in expected) conflict(); else next(); return; }
                        if(!(k in expected)){conflict();return;}
                        if(typeof Blob!=='undefined' && value instanceof Blob) {
                            // Keep the read/write transaction alive until the FileReader completes.
                            var done=false, equals=false;
                            LocalRecordIdbToBuffer(value,function(b){equals=!!b && decoder.decode(b)===expected[k];done=true;});
                            function wait(){ store.getKey(k).onsuccess=function(){if(!done)wait();else if(equals)next();else conflict();}; }
                            wait();return;
                        }
                        var current = typeof value==='string'?value:decoder.decode(value);
                        if(current!==expected[k])conflict(); else next();
                    };
                }
                next();
            }
            if(source.all){
                var keys=store.getAllKeys();
                keys.onsuccess=function(){
                    if(keys.result.length!==Object.keys(expected).length || keys.result.some(function(k){return !(k in expected);})) conflict();
                    else check(keys.result);
                };
            } else check(source.selected);
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
        if (n === source.length) LocalRecordIdbState.bytes = null;
        return n;
    },

    LocalRecordIdbRelease: function (token) {
        delete LocalRecordIdbState.snapshots[token];
    }
};

autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbState');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbHeap');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbSend');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbCopyBytes');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbToBuffer');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbOpen');
mergeInto(LibraryManager.library, LocalRecordIndexedDB);
