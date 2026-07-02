// Access to objects registered on the .NET side with `options.Globals.Register(...)`.
//
// During server rendering these are real CLR objects injected into the JavaScript engine,
// so calls are synchronous and return immediately. On the client there is no .NET process
// to talk to, so any access throws with an explanatory message rather than failing silently.

function bridge() {
    const value = globalThis.__jsxcore_dotnet;
    if (!value) {
        throw new Error(
            "JsxCore: .NET globals are only available during server rendering. " +
            "This view ran on the client. Either switch it to RenderMode.Server, or guard " +
            "the call with `isServerRender()`."
        );
    }
    return value;
}

export function isServerRender() {
    return !!globalThis.__jsxcore_dotnet;
}

// Each registered object is handed out as a proxy rather than directly, so that a view holding on
// to one still reaches the current request's instance. Modules are evaluated once per engine and
// engines are reused between requests, so `const svc = dotnet.Inventory` at module scope would
// otherwise capture whichever instance existed when that module first ran.
function late(name) {
    const target = bridge()[name];
    if (target === null || typeof target !== "object") {
        return target;
    }

    return new Proxy(Object.create(null), {
        get(_t, member) {
            const value = bridge()[name][member];
            return typeof value === "function"
                ? (...args) => bridge()[name][member](...args)
                : value;
        },
        has(_t, member) {
            return member in bridge()[name];
        },
        ownKeys() {
            return Reflect.ownKeys(bridge()[name]);
        },
        getOwnPropertyDescriptor(_t, member) {
            return { value: bridge()[name][member], enumerable: true, configurable: true };
        }
    });
}

export const dotnet = new Proxy(Object.create(null), {
    get(_target, name) {
        return late(name);
    },
    has(_target, name) {
        return name in bridge();
    },
    ownKeys() {
        return Reflect.ownKeys(bridge());
    },
    getOwnPropertyDescriptor(_target, name) {
        return { value: bridge()[name], enumerable: true, configurable: true };
    }
});

export default dotnet;
