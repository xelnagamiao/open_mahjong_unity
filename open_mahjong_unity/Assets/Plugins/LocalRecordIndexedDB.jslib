var LocalRecordIndexedDB = {
    $LocalRecordIdbState: {
        dbName: 'omu.localRecords',
        storeName: 'records',
        indexKey: '__index__',
        bytes: null,
        snapshots: {},
        migrations: {},
        pendingPrefix: '__pending_save_v1/',
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
        if (ArrayBuffer.isView(value)) {
            done(value.byteOffset === 0 && value.byteLength === value.buffer.byteLength ? value.buffer :
                value.buffer.slice(value.byteOffset, value.byteOffset + value.byteLength));
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
        var request, settled = false;
        try { request = indexedDB.open(LocalRecordIdbState.dbName, 1); }
        catch (e) { callback(null); return; }
        request.onupgradeneeded = function () {
            var db = request.result;
            if (!db.objectStoreNames.contains(LocalRecordIdbState.storeName)) {
                db.createObjectStore(LocalRecordIdbState.storeName);
            }
        };
        function finish(db) { if (!settled) { settled = true; callback(db); } }
        request.onsuccess = function() {
            var db = request.result;
            if (settled) { db.close(); return; }
            LocalRecordIdbState.db = db;
            db.onversionchange = function() { db.close(); if (LocalRecordIdbState.db === db) LocalRecordIdbState.db = null; };
            finish(db);
        };
        request.onerror = request.onblocked = function() { finish(null); };
    },

    // Each migration has disk-backed source and output records. The authoritative v1
    // store remains readable by older clients until the final compare-and-swap commits.
    $LocalRecordIdbIO: function (db, storeName, write, key, value) {
        return new Promise(function(resolve, reject) {
            var tx = db.transaction(storeName, write ? 'readwrite' : 'readonly'), store = tx.objectStore(storeName);
            var req = write ? store.put(value, key) : key === null ? store.getAllKeys() : store.get(key);
            tx.oncomplete = function() { resolve(req.result); };
            tx.onabort = function() { reject(tx.error || Error('IndexedDB 操作中断')); };
        });
    },

    $LocalRecordIdbBuffer: function(value) {
        return new Promise(function(resolve, reject) {
            LocalRecordIdbToBuffer(value, function(buffer) {
                if (buffer == null) reject(Error('牌谱存储类型无效')); else resolve(buffer);
            });
        });
    },

    $LocalRecordIdbResult: function(go, method, promise) {
        promise.then(function(bytes) {
            if (bytes === undefined) { LocalRecordIdbSend(go, method, 'ok'); return; }
            LocalRecordIdbState.bytes = bytes;
            LocalRecordIdbSend(go, method, 'ok|' + bytes.byteLength);
        }, function(e) { LocalRecordIdbSend(go, method, 'error|' + e.message); });
    },

    LocalRecordIdbMigrationRead: function(token, keyPtr, goPtr, methodPtr) {
        var key = UTF8ToString(keyPtr), go = UTF8ToString(goPtr), method = UTF8ToString(methodPtr);
        var state = LocalRecordIdbState, session = state.migrations[token];
        LocalRecordIdbResult(go, method, Promise.resolve().then(function() {
            if (!session) throw Error('conflict');
            if (session.sources.has(key)) return LocalRecordIdbIO(session.db, 'sources', false, key).then(LocalRecordIdbBuffer);
            return LocalRecordIdbIO(state.db, state.storeName, false, key).then(function(value) {
                if (value === undefined) throw Error('conflict');
                return LocalRecordIdbBuffer(value);
            }).then(function(buffer) {
                return LocalRecordIdbIO(session.db, 'sources', true, key, buffer).then(function() {
                    session.sources.add(key);
                    return buffer;
                });
            });
        }));
    },

    // token zero journals a new finished game before waiting for an old library upgrade.
    LocalRecordIdbStage: function(token, keyPtr, ptr, len, goPtr, methodPtr) {
        var state = LocalRecordIdbState, key = UTF8ToString(keyPtr), go = UTF8ToString(goPtr), method = UTF8ToString(methodPtr);
        var value = LocalRecordIdbCopyBytes(ptr, len);
        LocalRecordIdbOpen(function(db) {
            var session = state.migrations[token];
            if (!db || (token && !session)) { LocalRecordIdbSend(go, method, 'error|IndexedDB 不可用'); return; }
            LocalRecordIdbResult(go, method, LocalRecordIdbIO(token ? session.db : db, token ? 'writes' : state.storeName, true, key, value)
                .then(function() { if (token) session.writes.add(key); }));
        });
    },

    LocalRecordIdbPendingKeys: function(goPtr, methodPtr) {
        var state = LocalRecordIdbState, go = UTF8ToString(goPtr), method = UTF8ToString(methodPtr);
        LocalRecordIdbOpen(function(db) {
            if (!db) { LocalRecordIdbSend(go, method, 'error|IndexedDB 不可用'); return; }
            var range = IDBKeyRange.bound(state.pendingPrefix, state.pendingPrefix + '\uffff');
            var tx = db.transaction(state.storeName), req = tx.objectStore(state.storeName).getAllKeys(range);
            tx.oncomplete = function() {
                state.bytes = new TextEncoder().encode(JSON.stringify(req.result)).buffer;
                LocalRecordIdbSend(go, method, 'ok|' + state.bytes.byteLength);
            };
            tx.onabort = function() { LocalRecordIdbSend(go, method, 'error|IndexedDB 读取失败'); };
        });
    },

    $LocalRecordIdbBeginMigration: function(db, go, method) {
        var state = LocalRecordIdbState, token = ++state.nextSnapshot;
        var name = state.dbName + '.staging.' + Date.now() + '.' + Math.random().toString(36).slice(2);
        var request = indexedDB.open(name, 1);
        request.onupgradeneeded = function() { request.result.createObjectStore('sources'); request.result.createObjectStore('writes'); };
        request.onerror = function() { LocalRecordIdbSend(go, method, 'error|无法暂存牌谱'); };
        request.onsuccess = function() {
            var scratch = request.result;
            // Queue deletion now. It completes when this handle closes, including closure of the tab.
            scratch.onversionchange = function() {};
            indexedDB.deleteDatabase(name);
            LocalRecordIdbIO(db, state.storeName, false, null).then(function(keys) {
                keys = keys.filter(function(k) { return !k.startsWith(state.pendingPrefix); });
                state.migrations[token] = {db:scratch, keys:keys, sources:new Set(), writes:new Set()};
                state.bytes = new TextEncoder().encode(JSON.stringify(keys)).buffer;
                LocalRecordIdbSend(go, method, 'ok|' + state.bytes.byteLength + '|' + token);
            }).catch(function(e) { scratch.close(); LocalRecordIdbSend(go, method, 'error|' + e.message); });
        };
    },

    $LocalRecordIdbPublishMigration: function(db, token, plan, go, method) {
        var state = LocalRecordIdbState, session = state.migrations[token], reason = '牌谱事务未完成';
        var tx = db.transaction(state.storeName, 'readwrite'), store = tx.objectStore(state.storeName);
        function finish(message) { session.db.close(); delete state.migrations[token]; LocalRecordIdbSend(go, method, message); }
        tx.onabort = function() { finish('error|' + reason); };
        tx.oncomplete = function() { finish('ok'); };
        function abort(message) { reason = message; tx.abort(); }
        // IndexedDB only permits new requests in an active transaction callback.
        function resume(promise, next) {
            var ready = false, value, error;
            promise.then(function(result) { value = result; ready = true; }, function(e) { error = e; ready = true; });
            function pump() {
                store.getKey(state.indexKey).onsuccess = function() {
                    if (!ready) pump(); else if (error) abort(error.message); else next(value);
                };
            }
            pump();
        }
        var keys = Array.from(session.sources), writes = Array.from(session.writes), i = 0, j = 0;
        function publish() {
            if (j < writes.length) {
                var k = writes[j++];
                resume(LocalRecordIdbIO(session.db, 'writes', false, k), function(buffer) {
                    if (!(buffer instanceof ArrayBuffer)) { abort('暂存牌谱缺失'); return; }
                    store.put(buffer, k).onsuccess = publish;
                });
                return;
            }
            Object.keys(plan.writes).forEach(function(k) { store.put(new TextEncoder().encode(plan.writes[k]).buffer, k); });
            plan.deletes.forEach(function(k) { if (!session.writes.has(k) && !(k in plan.writes)) store.delete(k); });
        }
        function verify() {
            if (i === keys.length) { publish(); return; }
            var k = keys[i++], req = store.get(k);
            req.onsuccess = function() {
                if (req.result === undefined) { abort('conflict'); return; }
                var expected = LocalRecordIdbIO(session.db, 'sources', false, k).then(LocalRecordIdbBuffer);
                resume(Promise.all([LocalRecordIdbBuffer(req.result), expected]).then(function(buffers) {
                    var a = new Uint8Array(buffers[0]), b = new Uint8Array(buffers[1]);
                    if (a.length !== b.length) return false;
                    for (var n = 0; n < a.length; n++) if (a[n] !== b[n]) return false;
                    return true;
                }), function(equal) { if (equal) verify(); else abort('conflict'); });
            };
        }
        var req = store.getAllKeys();
        req.onsuccess = function() {
            var keys = req.result.filter(function(k) { return !k.startsWith(state.pendingPrefix); });
            if (keys.length !== session.keys.length || keys.some(function(k, n) { return k !== session.keys[n]; })) abort('conflict');
            else verify();
        };
    },

    // The JSON values inside the snapshot remain opaque UTF-8 strings; JavaScript
    // never parses replay numbers (including 64-bit wall seeds).
    LocalRecordIdbSnapshot: function (keyPtr, goPtr, methodPtr) {
        var key = UTF8ToString(keyPtr), go = UTF8ToString(goPtr), method = UTF8ToString(methodPtr);
        var state = LocalRecordIdbState;
        LocalRecordIdbOpen(function (db) {
            if (!db) { LocalRecordIdbSend(go,method,'error|IndexedDB 不可用'); return; }
            if (!key) { LocalRecordIdbBeginMigration(db, go, method); return; }
            var store = db.transaction(state.storeName,'readonly').objectStore(state.storeName);
            var values = Object.create(null), pending = [];
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
            selected.forEach(function(k) {
                var request = store.get(k);
                request.onsuccess = function() { capture(k,request.result); };
            });
            txn.onabort=function(){LocalRecordIdbSend(go,method,'error|IndexedDB 读取失败');};
            txn.oncomplete=function(){
                Promise.all(pending).then(function(){
                    var token = ++state.nextSnapshot;
                    state.snapshots[token] = {values:values, selected:selected};
                    state.bytes = new TextEncoder().encode(JSON.stringify(values)).buffer;
                    LocalRecordIdbSend(go,method,'ok|'+state.bytes.byteLength+'|'+token);
                }).catch(function(e){ LocalRecordIdbSend(go,method,'error|'+e.message); });
            };
        });
    },

    LocalRecordIdbCommit: function (token, ptr, len, goPtr, methodPtr) {
        var state = LocalRecordIdbState, source = state.snapshots[token], go = UTF8ToString(goPtr), method = UTF8ToString(methodPtr);
        delete state.snapshots[token];
        if (!source && !state.migrations[token]) { LocalRecordIdbSend(go,method,'error|conflict'); return; }
        var plan = JSON.parse(new TextDecoder().decode(LocalRecordIdbCopyBytes(ptr,len)));
        LocalRecordIdbOpen(function(db) {
            if (!db) { LocalRecordIdbSend(go,method,'error|IndexedDB 不可用'); return; }
            if (state.migrations[token]) { LocalRecordIdbPublishMigration(db, token, plan, go, method); return; }
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
            var journalKey = plan.deletes.find(function(k) { return k.startsWith(state.pendingPrefix); });
            if (journalKey) {
                var journal = store.getKey(journalKey);
                journal.onsuccess = function() {
                    if (journal.result === undefined) { reason = 'already-saved'; tx.abort(); }
                    else check(source.selected);
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
                LocalRecordIdbSend(go, method, 'error|IndexedDB 不可用');
                return;
            }
            var tx = db.transaction(LocalRecordIdbState.storeName, 'readonly');
            var request = tx.objectStore(LocalRecordIdbState.storeName).get(key);
            request.onsuccess = function () {
                if (request.result === undefined) { LocalRecordIdbSend(go, method, 'empty'); return; }
                LocalRecordIdbToBuffer(request.result, function (buffer) {
                    if (!buffer || buffer.byteLength <= 0) {
                        LocalRecordIdbSend(go, method, 'error|牌谱记录无法读取');
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
        var session = LocalRecordIdbState.migrations[token];
        if (session) { session.db.close(); delete LocalRecordIdbState.migrations[token]; }
        delete LocalRecordIdbState.snapshots[token];
    }
};

autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbState');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbHeap');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbSend');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbCopyBytes');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbToBuffer');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbOpen');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbIO');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbBuffer');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbResult');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbBeginMigration');
autoAddDeps(LocalRecordIndexedDB, '$LocalRecordIdbPublishMigration');
mergeInto(LibraryManager.library, LocalRecordIndexedDB);
