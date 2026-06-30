namespace JsxCore;

/// <summary>
/// Controls where a JSX/TSX view is turned into HTML.
/// </summary>
public enum RenderMode
{
    /// <summary>
    /// The default. The server emits an HTML shell containing the serialised model and a module
    /// script that mounts the view in the browser. The component itself never runs on the server.
    /// </summary>
    Client,

    /// <summary>
    /// The component runs on the server and the resulting markup is written into the response,
    /// exactly like a traditional view engine. No client-side JavaScript is emitted, so event
    /// handlers and hooks do nothing. .NET globals are available.
    /// </summary>
    Server,

    /// <summary>
    /// The component runs on the server for first paint, and the same component is then mounted
    /// in the browser so that interactivity works. .NET globals are available during the server
    /// pass only, so any code that touches them must be guarded with <c>isServerRender()</c>.
    /// </summary>
    ServerAndClient
}
