(function() {
    // Prevent double-injection
    if (window.__chobitsuDebugEnabled) {
        console.log('[ChobitsuDebug] Already initialized');
        return 'already_initialized';
    }
    if (typeof chobitsu === 'undefined') {
        console.error('[ChobitsuDebug] chobitsu not found');
        return 'chobitsu_not_found';
    }
    
    window.__chobitsuDebugEnabled = true;
    console.log('[ChobitsuDebug] Chobitsu initialized for single-eval CDP.');
    window.__chobitsuReady = true;
    return 'ready';
})();
