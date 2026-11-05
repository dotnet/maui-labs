(function() {
    if (typeof chobitsu !== 'undefined') {
        return 'loaded';
    }

    if (document.querySelector('script[data-devflow-chobitsu]')) {
        return 'loading';
    }

    const script = document.createElement('script');
    script.src = 'chobitsu.js';
    script.dataset.devflowChobitsu = 'true';
    document.head.appendChild(script);
    return 'loading';
})();
