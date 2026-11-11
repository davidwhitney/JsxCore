# Changelog

Notable changes to JsxCore and JsxCore.Npm, which share a version and ship together. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

From 1.0.0, the public .NET API and the specifiers a view can import (`dotnet:types`,
`dotnet:globals`, `dotnet:rendering`, `@jsxcore/client`) are the surface those version numbers
promise anything about.

## Unreleased

### Added

- `samples/SampleApp.ServerRender`: server rendering a component library that imports its own CSS,
  with and without JavaScript.

### Fixed

- A package that imports CSS from its own JavaScript (`import "./Button.css"`, as libraries built
  with `vite-plugin-lib-inject-css` do) no longer fails server rendering. The import is an empty
  module on the server and in the browser, and the document links the stylesheets the view's
  packages bring with them, ahead of the view's own.
- Minified imports without a space (`import"./a.js"`) are found and rewritten.
- Minification no longer hands stylesheets to the JavaScript minifier, where one stylesheet made the
  whole graph ship unminified.

## 1.0.0

Official release.
