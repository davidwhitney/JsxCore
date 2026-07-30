// Globals that packages built for a browser or for Node expect to already exist. Evaluated into
// every server rendering engine before any module loads, because the packages that want these
// touch them while their own module body runs, not when something calls into them.
//
// Deliberately minimal. These are not implementations, they are enough of a shape that a module
// referencing one can finish evaluating. Anything that genuinely needs the behaviour, rather than
// the symbol, will still fail, and should.

(function (global) {
    // React's entry points branch on this to pick their development or production build, and read
    // it at module scope. Production is the right answer: server rendering wants the fast build and
    // no development-only warnings written to a console this engine does not have.
    if (typeof global.process === "undefined") {
        global.process = { env: { NODE_ENV: "production" }, platform: "browser", argv: [] };
    } else if (!global.process.env) {
        global.process.env = { NODE_ENV: "production" };
    }

    // react-dom's streaming renderer constructs one at module scope to drive its scheduler. Server
    // rendering here is synchronous and never reaches the streaming path, so the ports exist and
    // deliver nothing.
    if (typeof global.MessageChannel === "undefined") {
        global.MessageChannel = function MessageChannel() {
            const port1 = { onmessage: null, close() {} };
            const port2 = {
                postMessage() {},
                close() {}
            };

            this.port1 = port1;
            this.port2 = port2;
        };
    }

    // Also constructed at module scope by the streaming renderer, to turn chunks into bytes.
    // Encoding UTF-8 by hand is short enough to be worth not pretending to be the real thing.
    if (typeof global.TextEncoder === "undefined") {
        global.TextEncoder = function TextEncoder() {
            this.encoding = "utf-8";

            this.encode = function (input) {
                const text = String(input === undefined ? "" : input);
                const bytes = [];

                for (let i = 0; i < text.length; i++) {
                    let point = text.charCodeAt(i);

                    // Surrogate pair: combine into the code point it encodes.
                    if (point >= 0xd800 && point <= 0xdbff && i + 1 < text.length) {
                        const low = text.charCodeAt(i + 1);
                        if (low >= 0xdc00 && low <= 0xdfff) {
                            point = 0x10000 + ((point - 0xd800) << 10) + (low - 0xdc00);
                            i++;
                        }
                    }

                    if (point < 0x80) {
                        bytes.push(point);
                    } else if (point < 0x800) {
                        bytes.push(0xc0 | (point >> 6), 0x80 | (point & 0x3f));
                    } else if (point < 0x10000) {
                        bytes.push(0xe0 | (point >> 12), 0x80 | ((point >> 6) & 0x3f), 0x80 | (point & 0x3f));
                    } else {
                        bytes.push(
                            0xf0 | (point >> 18),
                            0x80 | ((point >> 12) & 0x3f),
                            0x80 | ((point >> 6) & 0x3f),
                            0x80 | (point & 0x3f));
                    }
                }

                return new Uint8Array(bytes);
            };
        };
    }

    // Used by schedulers to defer work. Running it immediately keeps rendering synchronous, which
    // is the only mode this engine supports.
    if (typeof global.queueMicrotask === "undefined") {
        global.queueMicrotask = function (callback) { callback(); };
    }
})(globalThis);
