// What `dotnet:rendering` resolves to, on the server and in the browser alike.
//
// Only the .NET side of rendering lives here. Components, hooks and the JSX factory come from
// Preact or React, whichever the project builds against, and are imported by their own names.
// This module used to re-export a built-in renderer as well; importing a hook from here compiled
// and then failed to load, so those exports went with the renderer.

export { isServerRender } from "./dotnet.js";
