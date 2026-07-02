// Hot module reload client. Injected into the page only when the view engine is running
// in development. Talks to the /_jsx/hmr WebSocket endpoint published by JsxCore.

const OVERLAY_ID = "jsxcore-error-overlay";

function endpointUrl() {
    const configured = window.__jsxcore_hmr && window.__jsxcore_hmr.endpoint;
    const path = configured || "/_jsx/hmr";
    const protocol = location.protocol === "https:" ? "wss:" : "ws:";
    return protocol + "//" + location.host + path;
}

function removeOverlay() {
    const existing = document.getElementById(OVERLAY_ID);
    if (existing) {
        existing.remove();
    }
}

function showOverlay(title, detail) {
    removeOverlay();
    const overlay = document.createElement("div");
    overlay.id = OVERLAY_ID;
    overlay.setAttribute("style", [
        "position:fixed", "inset:0", "z-index:2147483647",
        "background:rgba(10,12,16,.94)", "color:#f2f4f8",
        "font:13px/1.55 ui-monospace,SFMono-Regular,Menlo,Consolas,monospace",
        "padding:32px", "overflow:auto", "white-space:pre-wrap"
    ].join(";"));

    const heading = document.createElement("div");
    heading.setAttribute("style", "color:#ff6b6b;font-weight:700;font-size:15px;margin-bottom:16px");
    heading.textContent = title;

    const body = document.createElement("div");
    body.textContent = detail;

    overlay.appendChild(heading);
    overlay.appendChild(body);
    document.body.appendChild(overlay);
}

// Re-imports the view module with a cache-busting token and re-renders in place, so
// component-local state is the only thing lost. Falls back to a full reload.
async function applyUpdate(message) {
    const root = window.__jsxcore_root;
    const entry = window.__jsxcore_hmr && window.__jsxcore_hmr.entry;

    // Without an update function there is no way to re-render in place, and a full load is correct.
    if (!root || !entry || typeof root.update !== "function") {
        location.reload();
        return;
    }

    try {
        const module = await import(entry + (entry.includes("?") ? "&" : "?") + "v=" + message.version);
        const Component = module.default;
        if (typeof Component !== "function") {
            location.reload();
            return;
        }

        root.update(Component);
        removeOverlay();
        console.info("[JsxCore] hot update applied");
    } catch (error) {
        console.warn("[JsxCore] hot update failed, reloading", error);
        location.reload();
    }
}

function connect(attempt) {
    const socket = new WebSocket(endpointUrl());

    socket.addEventListener("open", () => {
        if (attempt > 0) {
            // The server restarted underneath us; the page may be stale.
            location.reload();
        }
    });

    socket.addEventListener("message", (event) => {
        let message;
        try {
            message = JSON.parse(event.data);
        } catch {
            return;
        }

        switch (message.type) {
            case "update":
                applyUpdate(message);
                break;
            case "reload":
                location.reload();
                break;
            case "error":
                showOverlay(message.title || "Compilation failed", message.detail || "");
                break;
            case "ok":
                removeOverlay();
                break;
        }
    });

    socket.addEventListener("close", () => {
        // Retry with a bounded backoff so a restarting server reconnects quickly.
        const delay = Math.min(1000 * Math.pow(1.5, attempt), 10000);
        setTimeout(() => connect(attempt + 1), delay);
    });

    socket.addEventListener("error", () => socket.close());
}

connect(0);
