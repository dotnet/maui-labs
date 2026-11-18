// Browser side of the DevFlow web agent. Loaded once as an ES module by WebAgentInterop.
//
// Browsers have no screenshot API a page can use without a permission prompt, so captures render
// the live DOM: each element is cloned with its computed style inlined, images and canvases become
// data URLs, and the clone is drawn through an SVG <foreignObject> onto a canvas. Fonts loaded with
// the FontFace API cannot be embedded, so text in such fonts renders in a fallback font.

const SVG_NS = "http://www.w3.org/2000/svg";
const XHTML_NS = "http://www.w3.org/1999/xhtml";
const SKIPPED_TAGS = new Set(["SCRIPT", "NOSCRIPT", "STYLE", "LINK", "TEMPLATE", "META", "IFRAME"]);
// Inherited properties are always inlined: a child equal to the element default would otherwise
// inherit its clone parent's value instead.
const INHERITED = /^(color|font|line-height|letter-spacing|word-spacing|text-|white-space|visibility|direction|writing-mode|cursor|list-style|quotes|tab-size|word-break|overflow-wrap|hyphens|-webkit-text|fill|stroke|paint-order)/;

export function viewport() {
    return [window.innerWidth, window.innerHeight, window.devicePixelRatio || 1];
}

export function rect(element) {
    const r = element.getBoundingClientRect();
    return [r.left, r.top, r.width, r.height];
}

export function findByAutomationId(automationId) {
    return document.querySelector(`[data-automation-id="${CSS.escape(automationId)}"]`);
}

export function describe(element) {
    return [element.tagName.toLowerCase(), element.id || "", element.getAttribute("role") || ""];
}

/** Scrolls the nearest scrollable element at or above `element`. Returns false when none can scroll. */
export function scrollBy(element, deltaX, deltaY) {
    for (let current = element; current; current = current.parentElement) {
        const style = getComputedStyle(current);
        const canScrollY = /(auto|scroll)/.test(style.overflowY) && current.scrollHeight > current.clientHeight;
        const canScrollX = /(auto|scroll)/.test(style.overflowX) && current.scrollWidth > current.clientWidth;
        if (canScrollX || canScrollY) {
            current.scrollBy({ left: deltaX, top: -deltaY, behavior: "instant" });
            return true;
        }
    }
    return false;
}

/**
 * Renders the viewport region (x, y, width, height), in CSS pixels, to a base64 PNG.
 * Returns null when the browser refuses to rasterize the capture.
 */
export async function capture(x, y, width, height) {
    const viewportWidth = document.documentElement.clientWidth || window.innerWidth;
    const viewportHeight = document.documentElement.clientHeight || window.innerHeight;
    const context = { defaults: new Map(), sandbox: createSandbox(), pseudoRules: [], nextPseudo: 0, dataUrls: new Map() };
    try {
        const body = await cloneElement(document.body, context);
        if (!body)
            return null;

        const rootStyle = getComputedStyle(document.documentElement);
        const wrapper = document.createElementNS(XHTML_NS, "div");
        wrapper.setAttribute("style",
            `position:relative;overflow:hidden;width:${viewportWidth}px;height:${viewportHeight}px;` +
            `background:${rootStyle.backgroundColor};`);
        if (context.pseudoRules.length) {
            const style = document.createElementNS(XHTML_NS, "style");
            style.textContent = context.pseudoRules.join("\n");
            wrapper.appendChild(style);
        }
        // The body keeps its own computed style; shift it by the document scroll offset.
        body.style.setProperty("transform", `translate(${-window.scrollX}px, ${-window.scrollY}px)`);
        wrapper.appendChild(body);

        const markup = new XMLSerializer().serializeToString(wrapper);
        const svg = `<svg xmlns="${SVG_NS}" width="${viewportWidth}" height="${viewportHeight}">` +
            `<foreignObject x="0" y="0" width="100%" height="100%">${markup}</foreignObject></svg>`;
        const image = await loadImage("data:image/svg+xml;charset=utf-8," + encodeURIComponent(svg));

        const canvas = document.createElement("canvas");
        canvas.width = Math.max(1, Math.round(width));
        canvas.height = Math.max(1, Math.round(height));
        canvas.getContext("2d").drawImage(image, -x, -y);
        return canvas.toDataURL("image/png").substring("data:image/png;base64,".length);
    } catch (error) {
        console.warn("[Microsoft.Maui.DevFlow.Agent] Screenshot failed:", error);
        return null;
    } finally {
        context.sandbox.remove();
    }
}

async function cloneElement(element, context) {
    if (SKIPPED_TAGS.has(element.tagName) || element === context.sandbox)
        return null;

    const style = getComputedStyle(element);
    let clone;
    if (element instanceof HTMLCanvasElement) {
        clone = document.createElementNS(XHTML_NS, "img");
        try {
            clone.setAttribute("src", element.toDataURL());
        } catch {
            // A tainted canvas cannot be read; leave it blank.
        }
    } else {
        clone = element.cloneNode(false);
        clone.removeAttribute("id");
    }

    clone.setAttribute("style", inlineStyle(element, style, context));
    await copyFormState(element, clone);
    if (element instanceof HTMLImageElement && element.currentSrc)
        clone.setAttribute("src", await toDataUrl(element.currentSrc, context));
    if (element instanceof SVGImageElement) {
        const href = element.href?.baseVal;
        if (href)
            clone.setAttribute("href", await toDataUrl(new URL(href, document.baseURI).href, context));
    }

    copyPseudoElement(element, clone, "::before", context);
    copyPseudoElement(element, clone, "::after", context);

    const scrolled = element.scrollLeft !== 0 || element.scrollTop !== 0;
    if (!(element instanceof HTMLCanvasElement)) {
        for (const child of element.childNodes) {
            if (child.nodeType === Node.TEXT_NODE) {
                clone.appendChild(document.createTextNode(child.textContent));
            } else if (child.nodeType === Node.ELEMENT_NODE) {
                const childClone = await cloneElement(child, context);
                if (childClone) {
                    if (scrolled)
                        applyScrollOffset(childClone, element.scrollLeft, element.scrollTop);
                    clone.appendChild(childClone);
                }
            }
        }
    }
    return clone;
}

function inlineStyle(element, style, context) {
    const defaults = defaultStyle(element, context);
    let css = "";
    for (let i = 0; i < style.length; i++) {
        const name = style[i];
        const value = style.getPropertyValue(name);
        if (defaults.get(name) !== value || INHERITED.test(name))
            css += `${name}:${value};`;
    }
    // The clone is rendered outside the page's stacking context; keep it visible and static.
    return css + "animation:none;transition:none;caret-color:transparent;";
}

/** The computed style of a fresh element of the same kind, so only real differences are inlined. */
function defaultStyle(element, context) {
    const key = element.namespaceURI + "|" + element.tagName;
    let defaults = context.defaults.get(key);
    if (!defaults) {
        const doc = context.sandbox.contentDocument;
        const probe = element.namespaceURI === SVG_NS
            ? doc.createElementNS(SVG_NS, element.tagName)
            : doc.createElement(element.tagName);
        const host = element.namespaceURI === SVG_NS ? doc.createElementNS(SVG_NS, "svg") : doc.body;
        if (host !== doc.body)
            doc.body.appendChild(host);
        host.appendChild(probe);
        const style = doc.defaultView.getComputedStyle(probe);
        defaults = new Map();
        for (let i = 0; i < style.length; i++)
            defaults.set(style[i], style.getPropertyValue(style[i]));
        context.defaults.set(key, defaults);
        (host === doc.body ? probe : host).remove();
    }
    return defaults;
}

function createSandbox() {
    const sandbox = document.createElement("iframe");
    sandbox.setAttribute("aria-hidden", "true");
    sandbox.style.cssText = "position:fixed;left:-10000px;top:0;width:10px;height:10px;visibility:hidden;border:0";
    document.body.appendChild(sandbox);
    sandbox.contentDocument.open();
    sandbox.contentDocument.write("<!doctype html><html><body></body></html>");
    sandbox.contentDocument.close();
    return sandbox;
}

async function copyFormState(element, clone) {
    if (element instanceof HTMLInputElement) {
        if (element.type === "checkbox" || element.type === "radio") {
            if (element.checked)
                clone.setAttribute("checked", "");
            else
                clone.removeAttribute("checked");
        } else if (element.type !== "password") {
            clone.setAttribute("value", element.value);
        }
    } else if (element instanceof HTMLTextAreaElement) {
        clone.textContent = element.value;
    } else if (element instanceof HTMLSelectElement) {
        clone.setAttribute("data-devflow-selected", String(element.selectedIndex));
    } else if (element instanceof HTMLOptionElement && element.selected) {
        clone.setAttribute("selected", "");
    }
}

function copyPseudoElement(element, clone, pseudo, context) {
    const style = getComputedStyle(element, pseudo);
    const content = style.getPropertyValue("content");
    if (!content || content === "none" || content === "normal")
        return;

    const className = `devflow-pseudo-${context.nextPseudo++}`;
    clone.setAttribute("class", `${clone.getAttribute("class") || ""} ${className}`.trim());
    let css = "";
    for (let i = 0; i < style.length; i++)
        css += `${style[i]}:${style.getPropertyValue(style[i])};`;
    context.pseudoRules.push(`.${className}${pseudo}{${css}}`);
}

function applyScrollOffset(clone, scrollLeft, scrollTop) {
    const existing = clone.style.getPropertyValue("transform");
    const offset = `translate(${-scrollLeft}px, ${-scrollTop}px)`;
    clone.style.setProperty("transform", existing && existing !== "none" ? `${offset} ${existing}` : offset);
}

async function toDataUrl(url, context) {
    if (!url || url.startsWith("data:"))
        return url;
    if (context.dataUrls.has(url))
        return context.dataUrls.get(url);

    let result = url;
    try {
        const response = await fetch(url);
        if (response.ok) {
            const blob = await response.blob();
            result = await new Promise((resolve, reject) => {
                const reader = new FileReader();
                reader.onload = () => resolve(reader.result);
                reader.onerror = () => reject(reader.error);
                reader.readAsDataURL(blob);
            });
        }
    } catch {
        // Cross-origin images without CORS cannot be embedded and render blank.
    }
    context.dataUrls.set(url, result);
    return result;
}

function loadImage(source) {
    return new Promise((resolve, reject) => {
        const image = new Image();
        image.onload = () => resolve(image);
        image.onerror = () => reject(new Error("The browser could not rasterize the capture."));
        image.src = source;
    });
}
