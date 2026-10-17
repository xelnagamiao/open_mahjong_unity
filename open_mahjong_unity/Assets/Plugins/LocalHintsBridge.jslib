// Workers are local: no scoring HTTP request is issued by this bridge.
mergeInto(LibraryManager.library, {
  $OMLocalHints__deps: ['$OMLocalHintWorkerSource'],
  $OMLocalHints: {
    worker: null,
    pending: {},
    reply: function (id, value) {
      var receiver = OMLocalHints.pending[id];
      if (!receiver) return;
      delete OMLocalHints.pending[id];
      SendMessage(receiver, 'OnLocalHint', JSON.stringify(value));
    },
    fail: function (message) {
      Object.keys(OMLocalHints.pending).forEach(function (id) {
        OMLocalHints.reply(id, {id: Number(id), error: message});
      });
      if (OMLocalHints.worker) OMLocalHints.worker.terminate();
      OMLocalHints.worker = null;
    }
  },
  OMLocalHintSubmit__deps: ['$OMLocalHints'],
  OMLocalHintSubmit: function (requestPtr, receiverPtr) {
    var request = JSON.parse(UTF8ToString(requestPtr));
    OMLocalHints.pending[request.id] = UTF8ToString(receiverPtr);
    try {
      if (!OMLocalHints.worker) {
        var url = URL.createObjectURL(new Blob([OMLocalHintWorkerSource()], {type: 'text/javascript'}));
        try { OMLocalHints.worker = new Worker(url); }
        finally { URL.revokeObjectURL(url); }
        OMLocalHints.worker.onmessage = function (event) { OMLocalHints.reply(event.data.id, event.data); };
        OMLocalHints.worker.onerror = function (event) { OMLocalHints.fail(event.message || 'Local hint worker failed'); };
      }
      OMLocalHints.worker.postMessage(request);
    } catch (error) { OMLocalHints.fail(String(error)); }
  },
  OMLocalHintShutdown__deps: ['$OMLocalHints'],
  OMLocalHintShutdown: function () {
    if (OMLocalHints.worker) OMLocalHints.worker.terminate();
    OMLocalHints.worker = null;
    OMLocalHints.pending = {};
  }
});
