# Contributing

Thanks for looking. Issues and pull requests are both welcome, and so is asking first if you are
about to spend real time on something.

## Getting it building

The .NET SDK is the only prerequisite. JsxCore restores the npm packages it needs by talking to the
registry directly, so a clean checkout builds with no Node and no npm installed.

```bash
git clone https://github.com/davidwhitney/JsxCore
cd JsxCore
dotnet build JsxCore.slnx
dotnet test test/JsxCore.Tests
```

The test suite targets net10.0 and takes about a minute. A handful of tests hand a lock file JsxCore
wrote to real `npm ci` and check that npm accepts it; those skip themselves when npm is absent, so
install Node if you are changing the package client and want them to mean something.

Run a sample to see a change working:

```bash
dotnet run --project samples/SampleApp            # every render mode, .NET globals, MVC
dotnet run --project samples/SampleApp.React      # the React runtime
dotnet run --project samples/SampleApp.Tailwind   # stylesheets and Tailwind
```

## The shape of the repository

| | |
|---|---|
| `src/JsxCore` | The library: compilation, rendering, hosting, and the embedded runtime under `Assets/` |
| `src/JsxCore.PackageManagement` | The npm client, and the few primitives both halves need. No Jint, no ASP.NET Core |
| `src/JsxCore.Tool` | Build-time logic, invoked by the MSBuild targets. Ships inside the `JsxCore` package |
| `src/JsxCore.Npm` | The `dotnet npm` command line tool. Ships as the `JsxCore.Npm` package |
| `src/JsxCore.Analyzers` | Source generators: view location annotations, and recording registered globals |
| `src/JsxCore/build/JsxCore.targets` | What runs during `dotnet build` and `dotnet publish` |
| `test/JsxCore.Tests` | `Unit/` for pieces in isolation, `Component/` for a real host serving real views |
| `docs/` | The documentation, which is part of the product |

Three things are worth knowing before changing the build:

**The targets decide when, the tool decides what.** Anything needing real parsing, probing or JSON
construction is C# in `JsxCore.Tool` with tests behind it, rather than property functions in a
`.targets` file. The approximations used to drift from the C# doing the same job at run time.

**`JsxCore.PackageManagement` is the bottom of the stack, and stays there.** It references nothing
of JsxCore's, which is what lets `JsxCore.Npm` ship as a command line npm client rather than as a
view engine with a CLI attached. A reference from it back up to `JsxCore` is the one edit that
undoes that, so anything both need moves down into this project instead, into the folder matching
the namespace it belongs in rather than into a folder named for being shared. It is not published
on its own: `JsxCore` builds it into `lib/`, and `JsxCore.Npm` bundles it.

**The samples import the targets from source.** They use a `ProjectReference` plus an explicit
`Import`, which exercises build-time compilation but not the package layout. If you change how the
package is assembled, pack it and consume it from a scratch project, because nothing else does:

```bash
dotnet pack src/JsxCore -c Release -o /tmp/feed
dotnet new web -o /tmp/consumer && cd /tmp/consumer
dotnet add package JsxCore --source /tmp/feed
```

Clear `~/.nuget/packages/jsxcore/<version>` between attempts, or NuGet will keep serving the copy it
already extracted and you will test the wrong thing.

## Style

Match the surrounding code; it is fairly consistent. A few things that are deliberate:

- **Comments say why, not what.** If a line needs explaining, the explanation is usually the failure
  that made it necessary. Comments that restate the code get removed.
- **Documentation is for members where it adds something**, not a checklist to satisfy. `CS1591` is
  suppressed on purpose.
- **British spelling** in prose and identifiers where it comes up, except where a .NET or web API
  name settles it.
- `.editorconfig` covers the mechanical parts and `EnforceCodeStyleInBuild` is on, so the build will
  tell you.

## Pull requests

- One change per pull request, with the reasoning in the description. If it fixes something, say
  what the broken behaviour was.
- Tests for anything behavioural. `Unit/` if the piece can be tested in isolation, `Component/` if it
  needs a host, a compiler and a real view; there is a fixture for that.
- Add a line to `CHANGELOG.md` for anything a user would notice, under an `Unreleased` heading at
  the top, adding that heading if the last release closed it off.
- Update `docs/` in the same pull request. Documentation that lags the code is how the docs came to
  describe a renderer that had stopped running.
- CI builds on Linux and Windows. Paths and process invocation are the usual reasons something
  passes on one and not the other.

## Reporting a bug

Include the JsxCore version, the .NET version, the operating system, and whether the framework is
Preact or React. If it involves the build, the output of `dotnet build -v normal` is usually enough
to see which step went wrong. If it involves rendering, the browser console and the server log
together generally tell the whole story, because the two sides fail differently.

## Security

Please do not open a public issue for a security problem. Report it privately through
[GitHub's advisory form](https://github.com/davidwhitney/JsxCore/security/advisories/new).

Worth knowing: server rendering runs in an interpreter, in-process, with access to exactly the .NET
objects an application registers as globals. [.NET interop](docs/dotnet-interop.md#security) covers
what that does and does not expose.

## Licence

By contributing you agree that your contributions are licensed under the [MIT licence](LICENSE),
the same as the rest of the project.
