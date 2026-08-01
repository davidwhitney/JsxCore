/**
 * Objects registered on the .NET side via `options.Globals.Register(name, instance)`.
 *
 * These are live CLR objects injected into the JavaScript engine during server rendering,
 * so calls are synchronous. Declare the shape you expect via a type argument or module
 * augmentation:
 *
 * ```tsx
 * import { Inventory } from "dotnet:globals";
 * const greeting = (dotnet.Greeter as { greet(name: string): string }).greet("world");
 * ```
 */
export interface DotNetGlobals {
    [name: string]: any;
}

export declare const dotnet: DotNetGlobals;
export default dotnet;

/** True while running inside the server-side renderer, false in the browser. */
export declare function isServerRender(): boolean;
