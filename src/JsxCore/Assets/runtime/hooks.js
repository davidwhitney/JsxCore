// A deliberately small hook implementation. The client renderer installs a dispatcher
// that keeps state across re-renders; the server installs one that runs a single pass.

export const dispatcher = { current: null };

function require_() {
    if (!dispatcher.current) {
        throw new Error("JsxCore: hooks can only be called while a component is rendering.");
    }
    return dispatcher.current;
}

export function useState(initial) {
    return require_().useState(initial);
}

export function useRef(initial) {
    return require_().useRef(initial);
}

export function useEffect(effect, deps) {
    return require_().useEffect(effect, deps);
}

export function useMemo(factory, deps) {
    return require_().useMemo(factory, deps);
}

export function useCallback(fn, deps) {
    return useMemo(() => fn, deps);
}

export function depsChanged(previous, next) {
    if (!previous || !next || previous.length !== next.length) {
        return true;
    }
    for (let i = 0; i < next.length; i++) {
        if (!Object.is(previous[i], next[i])) {
            return true;
        }
    }
    return false;
}

// Used by the server renderer: hooks resolve to their initial value and effects never run.
export const serverDispatcher = {
    useState(initial) {
        const value = typeof initial === "function" ? initial() : initial;
        return [value, () => {
            throw new Error("JsxCore: state cannot be updated during server rendering.");
        }];
    },
    useRef(initial) {
        return { current: initial };
    },
    useEffect() {
        // Effects are a client-only concern.
    },
    useMemo(factory) {
        return factory();
    }
};
