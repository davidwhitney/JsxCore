import type { ComponentChildren } from "preact";

// Static assets stay where they belong in an ASP.NET Core application: in wwwroot, served by
// UseStaticFiles. Importing one resolves to the URL it is served from, here "/favicon.ico", so the
// path is checked when the view compiles instead of being a string nobody verifies.
import logo from "dotnet:wwwroot/favicon.ico";

// Shared components live alongside views and are imported with plain ESM. TypeScript rewrites
// "./Layout.tsx" to "./Layout.js" on the way out, so the browser resolves this graph natively
// and the server-side renderer loads exactly the same files.

export function Page({ title, active, children }: { title: string; active: string; children?: ComponentChildren }) {
    return (
        <div class="page">
            <Nav active={active} />
            <header>
                <h1>{title}</h1>
            </header>
            <main>{children}</main>
            <footer>
                Rendered by <strong>JsxCore</strong>
            </footer>
        </div>
    );
}

const links = [
    { href: "/", label: "Client rendered" },
    { href: "/server", label: "Server rendered" },
    { href: "/hybrid", label: "Server + client" },
    { href: "/dashboard", label: ".NET globals" },
    { href: "/preact", label: "Preact features" },
    { href: "/markdown", label: "npm package" },
    { href: "/mvc", label: "MVC controller" }
];

export function Nav({ active }: { active: string }) {
    return (
        <nav>
            <img src={logo} alt="" width="16" height="16" />
            <ul>
                {links.map((link) => (
                    <li key={link.href} class={link.href === active ? "active" : undefined}>
                        <a href={link.href}>{link.label}</a>
                    </li>
                ))}
            </ul>
        </nav>
    );
}

export function Card({ title, children }: { title: string; children?: ComponentChildren }) {
    return (
        <section class="card">
            <h2>{title}</h2>
            {children}
        </section>
    );
}
