# Content Security Policy

← [Documentation index](README.md)

JsxCore writes inline `<script>` tags, so a policy that forbids inline script blocks the page unless
they carry a nonce. Supplying one is a single option.

---

## Why there are inline scripts at all

Three of them, each doing something a separate file cannot:

| Script | What it is |
|---|---|
| `<script type="importmap">` | the import map, which the browser requires to be inline |
| `<script type="application/json">` | your model, serialised into the page so the client mounts with the data the server used |
| `<script type="module">` | the mount call, naming this view's module and container |

Under a policy of `script-src 'self'` all three are blocked and nothing renders. Not a degraded
page: an empty one.

---

## Supplying a nonce

Generate one per request, put it in your policy header, and hand JsxCore the same value:

```csharp
app.Use(async (context, next) =>
{
    var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
    context.Items["csp-nonce"] = nonce;

    context.Response.Headers["Content-Security-Policy"] =
        $"default-src \'self\'; script-src \'nonce-{nonce}\'; style-src \'self\'";

    await next();
});
```

```csharp
builder.AddJsxCore(options =>
{
    options.Document.Nonce = http => http.Items["csp-nonce"] as string;
});
```

The callback is asked **per request**, because a nonce that is reused is not a nonce, and every
script JsxCore writes carries the result, including the hot reload client in development, which is
an external script but still governed by a `script-src` naming only a nonce.

Returning null or an empty string writes no attribute, which is what an application without a
policy wants. Nothing is validated: the value is escaped and emitted.

If you already use a library that manages CSP headers, read its nonce rather than generating your
own, so that JsxCore emits the *same* value your header names.

---

## What a policy needs to allow

Beyond the nonce:

| Directive | Why |
|---|---|
| `script-src 'nonce-…'` | the three inline scripts, and the mount module |
| `connect-src` for your own origin | hot reload uses a WebSocket in development; add `ws:` or `wss:` there |
| `style-src` | JsxCore emits no inline style of its own, so this is about your stylesheets |

Compiled views and npm packages are served from your own origin under `/_jsx/`, so `'self'` covers
them. Pointing a package at a CDN with `options.ImportMap` means adding that host.

---

## Verified

The sample in [`samples/SampleApp.Tailwind`](../samples/SampleApp.Tailwind) was run under
`default-src 'self'; script-src 'nonce-…'; style-src 'self'` in Chrome: the page rendered, hydrated
and stayed interactive with no violations reported.

---

## See also

- [Extensibility](extensibility.md): document templates and import map entries
- [Returning views](returning-views.md): per-response document settings
