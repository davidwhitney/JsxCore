export type Dispatch<T> = (value: T | ((previous: T) => T)) => void;

export declare function useState<T>(initial: T | (() => T)): [T, Dispatch<T>];
export declare function useState<T = undefined>(): [T | undefined, Dispatch<T | undefined>];
export declare function useRef<T>(initial: T): { current: T };
export declare function useRef<T = undefined>(): { current: T | undefined };
export declare function useEffect(effect: () => void | (() => void), deps?: readonly unknown[]): void;
export declare function useMemo<T>(factory: () => T, deps?: readonly unknown[]): T;
export declare function useCallback<T extends (...args: any[]) => any>(fn: T, deps?: readonly unknown[]): T;
export declare function depsChanged(previous: readonly unknown[] | undefined, next: readonly unknown[] | undefined): boolean;
