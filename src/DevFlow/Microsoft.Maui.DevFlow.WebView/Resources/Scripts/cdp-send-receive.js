(function() {
    if (typeof chobitsu === 'undefined') return JSON.stringify({error: 'chobitsu not loaded'});
    window.__cdpResponse = null;
    window.__cdpResponseReady = false;
    var command = JSON.parse('%CDP_MESSAGE%');
    var nativeBinding = %NATIVE_CDP_BINDING%;
    var deliver = nativeBinding && window[nativeBinding];
    if (nativeBinding && typeof deliver !== 'function')
        return JSON.stringify({error: 'Native CDP response binding is unavailable'});
    if (!window.__cdpMailboxCaptured) {
        window.__cdpOriginalOnMessage = chobitsu.onMessage;
        window.__cdpMailboxCaptured = true;
    }
    var orig = window.__cdpOriginalOnMessage;
    chobitsu.setOnMessage(function(msg) {
        var response;
        try { response = typeof msg === 'string' ? JSON.parse(msg) : msg; }
        catch (_) { return; }
        if (response.id !== command.id) return;
        chobitsu.setOnMessage(orig);
        if (deliver) {
            deliver(JSON.stringify({
                channel: nativeBinding,
                response: typeof msg === 'string' ? msg : JSON.stringify(msg)
            }));
        } else {
            window.__cdpResponse = msg;
            window.__cdpResponseReady = true;
        }
    });
    try {
        chobitsu.sendRawMessage('%CDP_MESSAGE%');
    } catch(e) {
        chobitsu.setOnMessage(orig);
        return JSON.stringify({error: e.message});
    }
    return '__cdp_pending__';
})();
