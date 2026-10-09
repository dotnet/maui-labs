(function() {
    if (window.__cdpResponseReady) {
        var r = window.__cdpResponse;
        window.__cdpResponse = null;
        window.__cdpResponseReady = false;
        return r;
    }
    return null;
})();
