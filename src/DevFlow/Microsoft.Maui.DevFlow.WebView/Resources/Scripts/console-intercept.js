(function() {
    if (window.__consoleIntercepted) return 'already_intercepted';
    window.__consoleIntercepted = true;
    window.__webviewLogs = [];

    var maxBuffer = 500;
    var levels = ['log', 'info', 'warn', 'error', 'debug'];

    levels.forEach(function(level) {
        var orig = console[level];
        console[level] = function() {
            // Call original
            orig.apply(console, arguments);

            // Buffer the message
            var parts = [];
            for (var i = 0; i < arguments.length; i++) {
                try {
                    parts.push(typeof arguments[i] === 'string' ? arguments[i] : JSON.stringify(arguments[i]));
                } catch(e) {
                    parts.push(String(arguments[i]));
                }
            }

            if (window.__webviewLogs.length < maxBuffer) {
                window.__webviewLogs.push({
                    l: level,
                    m: parts.join(' '),
                    t: new Date().toISOString()
                });
            }
        };
    });

    // Also capture unhandled errors
    window.addEventListener('error', function(e) {
        if (window.__webviewLogs.length < maxBuffer) {
            window.__webviewLogs.push({
                l: 'error',
                m: e.message || 'Unknown error',
                t: new Date().toISOString(),
                e: (e.filename || '') + ':' + (e.lineno || '') + ':' + (e.colno || '')
            });
        }
    });

    window.addEventListener('unhandledrejection', function(e) {
        if (window.__webviewLogs.length < maxBuffer) {
            window.__webviewLogs.push({
                l: 'error',
                m: 'Unhandled promise rejection: ' + (e.reason ? (e.reason.message || String(e.reason)) : 'unknown'),
                t: new Date().toISOString()
            });
        }
    });

    return 'intercepted';
})();
