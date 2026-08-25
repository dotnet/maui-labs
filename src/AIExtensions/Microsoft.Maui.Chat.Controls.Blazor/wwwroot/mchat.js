// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Minimal JS glue for the Microsoft.Maui.Chat.Controls.Blazor components. Everything else is
// pure Razor and CSS. Exports:
//   - focus(element)            focuses the composer textarea and moves the caret to the end
//   - autoSize(element)         grows the composer textarea to fit its contents (max ~10rem)
//   - scrollToBottom(element)   pins the message-list to the bottom
//   - stickToBottom(element,cb) observes the message-list and calls cb(true|false) as the
//                               user scrolls up/down. Returns a handle that must be released
//                               via releaseStickToBottom(handle) when the component disposes.
//
// The C# side owns the state machine; this file only exists because Blazor cannot
// scrollBy() into a DOM element or focus a textarea without going through JS.

const stickyRegistry = new Map();
let nextStickyHandle = 1;

export function focus(element) {
    if (!element) return;
    try {
        element.focus({ preventScroll: false });
        if (typeof element.selectionStart === "number" && typeof element.value === "string") {
            const end = element.value.length;
            element.setSelectionRange(end, end);
        }
    } catch {
        // Focus is a best-effort call.
    }
}

export function autoSize(element) {
    if (!element) return;
    // Reset first so the scrollHeight reflects the current content, not the previous size.
    element.style.height = "auto";
    // scrollHeight includes padding; clamp to CSS max-height (10rem = 160px at default).
    const target = Math.min(element.scrollHeight, 320);
    element.style.height = target + "px";
}

export function scrollToBottom(element) {
    if (!element) return;
    try {
        element.scrollTop = element.scrollHeight;
    } catch {
        // Scroll is best-effort during layout thrash.
    }
}

export function stickToBottom(element, dotNet) {
    if (!element) return 0;

    // A generous threshold: if the user is within 96 px of the bottom, treat them as anchored.
    const threshold = 96;
    let anchored = true;

    const onScroll = () => {
        const distanceFromBottom = element.scrollHeight - element.scrollTop - element.clientHeight;
        const nowAnchored = distanceFromBottom <= threshold;
        if (nowAnchored !== anchored) {
            anchored = nowAnchored;
            try {
                dotNet.invokeMethodAsync("OnAnchorChanged", anchored);
            } catch {
                // The .NET peer may have already been disposed.
            }
        }
    };

    element.addEventListener("scroll", onScroll, { passive: true });
    const handle = nextStickyHandle++;
    stickyRegistry.set(handle, { element, onScroll });

    // Kick things off so the caller sees the initial state.
    onScroll();
    return handle;
}

export function releaseStickToBottom(handle) {
    const entry = stickyRegistry.get(handle);
    if (!entry) return;
    stickyRegistry.delete(handle);
    entry.element.removeEventListener("scroll", entry.onScroll);
}

export function playAudio(element) {
    if (!element) return;
    try {
        const p = element.play();
        if (p && typeof p.catch === "function") p.catch(() => { /* autoplay may block */ });
    } catch { /* best effort */ }
}

export function pauseAudio(element) {
    if (!element) return;
    try {
        element.pause();
    } catch { /* best effort */ }
}
