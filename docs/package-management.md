# Package management

← [Documentation index](README.md)

---

JsxCore restores npm packages itself, by talking to the npm registry directly. Nothing shells out
to npm, and Node does not have to be installed. It resolves versions, fetches and verifies each
package, unpacks it into `node_modules`, and writes a `package-lock.json` that real `npm ci`
accepts.

A machine with the .NET SDK and nothing else can build a JsxCore project.

For what to do with a package once it is installed, see [npm packages](npm-packages.md).

---

## During a build

Every build checks that everything `package.json` declares is present in `node_modules` and
restores whatever is not, so all of these fix themselves with `dotnet build`:

- a fresh clone with no `node_modules`
- a dependency added to `package.json` by hand, or arriving in someone else's commit
- a deleted or partially installed `node_modules`

```
JsxCore: restoring with native: typescript
JsxCore: created /src/app/package.json
JsxCore: fetching @typescript/typescript-linux-x64@7.0.2
```

The lock file is used first, because installing exactly what is pinned is reproducible and leaves
the file alone. Resolving against the registry is the fallback, for anything the lock file cannot
satisfy.

This matters more than it sounds. A missing package does not fail compilation, because the compiler
only needs types; the build succeeds and the view fails to render later with an error about a
module. Checking is a handful of file probes, so a build where nothing is missing does no work at
all.

Set `<JsxCoreAutoInstallDependencies>false</JsxCoreAutoInstallDependencies>` to manage packages
yourself. The check still runs, but reports what is missing as error `JSX0005` instead of
installing it.

The application checks again when it starts and logs a warning naming anything declared but not
installed. That covers the case a build cannot: a server deployed without a restore, where the
first sign would otherwise be a view failing to render.

---

## On the command line

The same client is a dotnet tool, so packages can be added without running a build and without npm
on the machine.

### Installing the tool

Globally, once per machine:

```bash
dotnet tool install -g JsxCore.Npm
```

Or per project, so everyone who clones the repository gets it from `dotnet tool restore`:

```bash
dotnet new tool-manifest
dotnet tool install JsxCore.Npm
```

Both are invoked the same way, as `dotnet npm`.

A global install works everywhere on that machine immediately. A tool manifest is a file you commit,
so it records the tool without installing it: the person who ran `dotnet tool install` has it
already, and anyone else who clones the repository runs

```bash
dotnet tool restore
```

once, before `dotnet npm` will work. The SDK says so if you forget:

```
Run "dotnet tool restore" to make the "dotnet-npm" command available.
```

None of this affects the build. `dotnet build` restores npm packages whether or not the tool is
installed, because the targets invoke the copy inside the JsxCore package rather than the tool.

### Why `dotnet npm` and not `npm`

The tool installs an executable called `dotnet-npm`, which is what makes `dotnet npm` resolve to
it. It deliberately does not install one called `npm`: that would land on `PATH` and shadow the
real npm, which would be an unusually rude thing for a tool whose whole point is not needing npm.
Your existing npm is untouched and keeps working exactly as before.

### Commands

The shape follows [`dotnet package add`](https://learn.microsoft.com/dotnet/core/tools/dotnet-package-add)
rather than npm's, because the people running it are already holding the .NET CLI.

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

`-D` and `--save-dev` are accepted for `--dev`, `-v` for `--version`, and `--prefix` for
`--project`. Running `dotnet npm` with no command prints the usage.

### Examples

```bash
dotnet npm add marked                          # latest release
dotnet npm add marked --version ^12            # a range
dotnet npm add marked@^12                      # npm's own spelling, equivalent
dotnet npm add typescript --version ^7 --dev   # build and server only
dotnet npm add marked classnames               # several at once
dotnet npm add marked --project ./src/Web      # somewhere other than here
```

```bash
$ dotnet npm list
classnames  ^2.5.1
marked      ^18.0.7
typescript  ^7            dev   not installed
```

### npm's spellings

Muscle memory is accepted wherever it is unambiguous, so you do not have to remember which CLI you
are in:

| Typed | Runs |
|---|---|
| `install <PACKAGE>`, `i <PACKAGE>` | `add` |
| `install`, `i` | `restore` |
| `rm`, `un`, `uninstall` | `remove` |
| `ls` | `list` |
| `ci` | `restore`, refusing to resolve without a lock file |

`ci` is strict on purpose. Restoring exactly what is pinned and quietly resolving something else
when nothing is pinned are different promises, and a command named after the first should not
deliver the second: with no lock file it fails and says so, exactly as npm's does.

Failure is always a non-zero exit code with the reason on stderr, so these are safe in a script:

```bash
$ dotnet npm add no-such-package
JsxCore: could not install packages: There is no package named 'no-such-package' on https://registry.npmjs.org.
$ echo $?
1
```

### It is the same client the build uses

Not a reimplementation with a command line over it. The tool and the build targets are packed from
one project, so a package added here and a package restored by a build resolve identically and
write the same lock file. If they ever disagreed, one of them would be wrong.

---

## Using npm, or another package manager

Nothing here is proprietary. `package.json`, `package-lock.json` and `node_modules` are ordinary
files in their ordinary formats, so if you have npm you can simply use it and ignore everything
above:

```bash
npm install marked
npm install --save-dev typescript@^7
dotnet build
```

The build finds what npm installed, does not restore anything it does not need to, and does not
touch the lock file npm wrote. Server rendering and the browser both resolve packages out of that
`node_modules` exactly as they would have done otherwise. Mixing is fine too: add a package with
npm today and with `dotnet npm` tomorrow.

The same is true of anything else that reads and writes those files:

| | |
|---|---|
| **npm** | Works as is. Tested with npm 11 |
| **Yarn** | Works as is, because it hoists packages to the top level like npm. Tested with Yarn 1.22 |
| **pnpm** | Needs one line of configuration, below. Tested with pnpm 11 |

pnpm keeps transitive packages in `node_modules/.pnpm` and links only direct dependencies into
`node_modules`, so the TypeScript compiler, which arrives as a platform-specific package underneath
`typescript`, is installed but is not where JsxCore looks for it. The build reports `JSX0001` and
leaves compilation to startup. Point it at the store as well:

```xml
<PropertyGroup>
  <JsxCoreAdditionalSearchPaths>$(MSBuildProjectDirectory)/node_modules/.pnpm</JsxCoreAdditionalSearchPaths>
</PropertyGroup>
```

Yarn's Plug'n'Play mode has no `node_modules` at all, and is not supported.

### Using npm instead

That is about using npm yourself. The build can also be told to use it, instead of restoring
packages itself:

```xml
<PropertyGroup>
  <JsxCoreUseNpm>true</JsxCoreUseNpm>
</PropertyGroup>
```

Or name a strategy directly, which is the same switch with room for more later:

```xml
<JsxCorePackageManager>npm</JsxCorePackageManager>
```

At run time the equivalent is `options.PackageManager = "npm"`. Naming one that cannot run here is
reported rather than quietly falling back, so a build that asks for npm and does not get it says so.

Reasons to prefer npm: a private registry needing `.npmrc` authentication, a dependency that runs
install scripts, or a lock file you would rather only one tool ever wrote.

---

## What it writes

**`package.json`** is the record, and stays an ordinary manifest. Everything that reads one keeps
working: `npm audit`, `npm ci`, Dependabot and Renovate.

**`package-lock.json`** at `lockfileVersion` 3, with the `resolved` URL and `integrity` hash for
every package. Real `npm ci` accepts what JsxCore writes, installs from it, and leaves it
unrewritten. For the same input the two agree on every path, version, resolved URL and integrity
hash. npm additionally records `license` and `peerDependenciesMeta`, which JsxCore does not, so the
files are equivalent rather than byte identical.

Commit both.

---

## How close to npm is it

Close enough that the tests check it against npm rather than against a reading of the
specification.

**Version ranges.** `^`, `~`, `x`, hyphen ranges, `||`, and the prerelease rules, which are the
subtle ones: `1.0.0-beta` satisfies `^1.0.0-alpha` but not `^1.0.0`. Every form is checked against
npm's own semver package, which is how the one disagreement was found: `^1` was being read as
`<1.1.0` rather than `<2.0.0`.

**Tree layout.** A package is hoisted to the top level unless something incompatible is already
visible from where it is needed, in which case it goes to the shallowest free scope. Trees are
identical to npm's for eslint, webpack and jest: same packages, same paths, same versions, 325 of
them in jest's case.

**Platform packages.** The TypeScript compiler ships as 20 optional dependencies, one per platform,
selected by `os` and `cpu`. They are listed in both `dependencies` and `optionalDependencies`, and
the optional entry overrides the other; honouring only the first block installs all twenty.

**Peer dependencies.** Installed automatically as npm 7 and later do, placed beside the dependent
rather than underneath it, and skipped when marked optional.

**Also supported:** git dependencies (`github:user/repo`, `git+https://...#ref`), workspaces, and
`overrides`.

Independently of npm, every install checks that each dependency of each placed package resolves,
from where that package sits, to a version satisfying it. Installing refuses to proceed if it does
not. A tree that installs and then fails at run time inside somebody else's package is worse than
no tree at all.

---

## What it does not do

- **Lifecycle scripts.** `preinstall`, `install` and `postinstall` are Node programs, and running
  them would reintroduce the dependency this removes. Packages with native builds need npm.
- **`.npmrc` beyond the registry URL.** Scoped registries, auth tokens and proxies are not read.
  A private registry needing authentication needs npm.
- **Pruning on remove.** `dotnet npm remove` deletes the named package and rewrites the lock file
  correctly, but anything that package alone brought in stays on disk until `node_modules` is
  restored from scratch.
- **`npm audit`, `npm publish`, `npm dedupe`.** Restoring a declared dependency set is the goal;
  being npm is not. The lock file is a real lock file, so npm can still do all of these.

See [the roadmap](roadmap.md) for what is planned.

---

## Seeing packages in the IDE

A package name is not a file, so nothing useful comes from listing names as project items: an item
whose `Include` is not a path gets drawn as a missing file.

Instead each installed package is surfaced through the one thing about it that is a real file, its
manifest, linked into a folder that shadows `package.json` so the manifest reads as the root of
what it declares:

```
package.json/
    package.json
    package-lock.json
    dependencies/
        marked@^18.0.7/package.json
        classnames@^2.5.1/package.json
    devDependencies/
        typescript@^7.0.2/package.json
```

Each package is labelled with the range declared for it, npm's own way round, so a node can be
pasted straight back into an install command. A range that cannot be a file name, such as
`>=1.0.0 <2.0.0` or `github:user/repo`, is left off rather than rewritten into something that reads
like a different range; the package still appears under its name.

The manifest and its lock file move into the folder rather than sitting beside it, so there is one
node for the subject instead of a file and a folder sharing a name. Only their position in the tree
changes: they are still built, published and read from where they always were.

Only installed packages appear, so an entry in `package.json` you have not restored yet contributes
nothing rather than a broken link. Where the two groups sort relative to the manifest inside the
folder is the IDE's own ordering, which a project file cannot influence.

The list is written during the build, into `obj/JsxCore/JsxCore.g.props`, and read on the next
evaluation. A project tree is built without running targets, so there is no earlier point at which
it could be produced. NuGet's restore works the same way and has the same consequence: install a
package and the tree catches up after the next build.

```xml
<PropertyGroup>
  <JsxCoreShowNpmPackagesInIde>false</JsxCoreShowNpmPackagesInIde>
  <JsxCoreIdePackageFolder>package.json</JsxCoreIdePackageFolder>
</PropertyGroup>
```

---

## See also

- [npm packages](npm-packages.md) for importing a package from a view
- [Configuration](configuration.md) for every MSBuild property and option
- [Troubleshooting](troubleshooting.md) for what the restore warnings mean
- [Build and deploy](build-and-deploy.md#what-ends-up-in-the-publish-output) for getting packages onto a server
