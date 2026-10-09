(function() {
    var el = document.activeElement;
    if (el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable)) {
        if (el.isContentEditable) {
            document.execCommand('insertText', false, '%TEXT%');
        } else {
            var start = el.selectionStart || 0;
            var end = el.selectionEnd || 0;
            var value = el.value || '';
            el.value = value.substring(0, start) + '%TEXT%' + value.substring(end);
            el.selectionStart = el.selectionEnd = start + %TEXT_LENGTH%;
            el.dispatchEvent(new Event('input', { bubbles: true }));
            el.dispatchEvent(new Event('change', { bubbles: true }));
        }
        return 'success';
    }
    return 'no_editable_element';
})();
