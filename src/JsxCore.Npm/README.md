# JsxCore.Npm

**npm packages without npm.**

A dotnet tool that adds and restores npm packages with nothing installed but the .NET SDK. It talks
to the registry directly: resolves versions, verifies integrity, unpacks into `node_modules`, and
writes a `package-lock.json` that real `npm ci` accepts.

```bash
dotnet tool install -g JsxCore.Npm
dotnet npm add marked
```

It ships the same client the [JsxCore](https://www.nuget.org/packages/JsxCore) build uses, one
assembly referenced by both, so a package added here and a package restored by a build resolve
identically and write the same lock file.

It is useful on its own, and the package says so: the client has no dependency on the view engine,
so installing this brings the npm client and nothing else.

## Commands

The shape follows `dotnet package add` rather than npm's, because whoever is running it is already
holding the .NET CLI.

| Command | What it does |
|---|---|
| `dotnet npm add <PACKAGE>` | Resolves and installs a package, and records it in `package.json` |
| `dotnet npm remove <PACKAGE>` | Drops it from `package.json`, deletes it, and re-resolves the rest |
| `dotnet npm list` | Every declared package, its range, and whether it is installed |
| `dotnet npm restore` | Installs what the lock file pins, or resolves `package.json` if there is none |
| `dotnet npm ci` | Installs what the lock file pins, and fails if there is no lock file |
| `dotnet npm init` | Creates a `package.json` |

| Option | Applies to | Meaning |
|---|---|---|
| `--version <RANGE>` | `add` | Version range, as `^12` or `12.0.1`. Defaults to the latest release |
| `--dev` | `add` | Add to `devDependencies` rather than `dependencies` |
| `--project <PATH>` | all | Directory or project file whose `package.json` to use. Defaults to the current directory |
| `--registry <URL>` | all | Registry to resolve from. Defaults to `registry.npmjs.org` |

npm's own spellings are accepted wherever they are unambiguous, so `install`, `i`, `rm`, `un`,
`uninstall` and `ls` all work, as does `marked@^12`.

```bash
dotnet npm add marked --version ^12
dotnet npm add typescript --version ^7 --dev
dotnet npm add marked classnames --project ./src/Web
```

## Why `dotnet npm` and not `npm`

The tool installs an executable called `dotnet-npm`, which is what makes `dotnet npm` resolve to
it. It deliberately does not install one called `npm`: that would land on `PATH` and shadow the real
npm, which would be an unusually rude thing for a tool whose whole point is not needing npm. Your
existing npm is untouched.

## What it does not do

Lifecycle scripts. `preinstall`, `install` and `postinstall` are Node programs, so a package that
compiles something during installation needs real npm. Nothing is linked into `node_modules/.bin`
for the same reason.

## Documentation

Full reference: [Package management](https://github.com/davidwhitney/JsxCore/blob/main/docs/package-management.md).

MIT licensed. Issues and source: [github.com/davidwhitney/JsxCore](https://github.com/davidwhitney/JsxCore).
