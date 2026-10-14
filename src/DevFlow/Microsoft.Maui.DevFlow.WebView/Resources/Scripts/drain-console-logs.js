(function() {
    if (!window.__webviewLogs || window.__webviewLogs.length === 0) return null;
    var logs = JSON.stringify(window.__webviewLogs);
    window.__webviewLogs = [];
    return logs;
})();
