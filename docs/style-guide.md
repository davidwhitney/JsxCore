# Documentation style guide

Not linked from the index. This is for whoever is editing the docs, including future me, and
including an LLM asked to add a page.

The house style is **technical prose that earns every sentence**. The failure mode this guards
against is not bad writing; it is pleasant writing that repeats itself.

---

## 1. One fact, one home

The rule that matters most. Every fact is **explained** in exactly one page and **linked** from
everywhere else. Another page may state the consequence in a sentence; it may not restate the
mechanism.

Current owners:

| Subject | Owner |
|---|---|
| Restoring packages, the lock file, `dotnet npm`, other package managers | `package-management.md` |
| Importing a package from a view, what resolves, what is served | `npm-packages.md` |
| Build modes, type-checking strictness, minification, publish output | `build-and-deploy.md` |
| Build ids, the embedded runtime, why there is no bundler | `how-it-works.md` |
| Every import a view can write | `import-syntax.md` |
| Stylesheets, CSS Modules, Tailwind | `styling.md` |
| View resolution, Razor coexistence, per-response settings | `returning-views.md` |
| Every option and MSBuild property | `configuration.md` |
| Every error message and its fix | `troubleshooting.md` |

Before adding a paragraph, find the owner. If the fact has no owner, you are either adding a page
or you have found the wrong home for something.

The test: **if this fact changed, how many files would I have to edit?** More than one is a bug.

## 2. Cut the meta-sentence

A sentence that announces content instead of being content. All of these were removed from these
docs and should not come back:

> That is the whole story. · That is the whole difference in shape. · None of this is something you
> have to do. · This matters more than it sounds. · Worth knowing before you commit. · Two things
> worth noticing. · That sounds like a limitation and is mostly a simplification.

If a point is important, the sentence stating it carries the weight. Saying it is important does
not.

## 3. One reason per claim

Give the strongest reason and stop. Two reasons read as uncertainty about whether either works.

> **Before:** Order comes from the import graph rather than what rendered first, so a component's
> stylesheet is emitted before the page's and the page can override it, and two pages sharing a
> stylesheet cannot produce two different cascades.
>
> **After:** Order follows the import graph, not render order: a component's stylesheet precedes
> that of the page importing it, so the page can override it.

The same applies to lists of causes. Three "usual causes" are useful; five means two are padding.

## 4. No em-dashes

Never use `—`. Readers now treat it as a tell for generated text, and it is almost always doing a
job another mark does better:

| Instead of | Use |
|---|---|
| an aside inside a sentence | commas, or parentheses if it is genuinely parenthetical |
| a pause before an explanation | a colon |
| a list item's label and description | a colon: `- [Page](page.md)` then `: what it covers` |
| joining two independent clauses | a full stop, or a semicolon |

En-dashes (`–`) are not a substitute. Hyphens keep their ordinary job in compounds
(`build-time`, `server-rendered`).

There are none left in `docs/`. Keep it that way; `grep -n "—" docs/*.md` should return nothing.

## 5. Verbs, not gerund-phrases

| Instead of | Write |
|---|---|
| gives you back the URL | resolves to the URL |
| a file that is not there is reported | a missing file is reported |
| is responsible for the entire body | owns the entire body |
| you must emit ... yourself | it has to emit |

## 6. Person

- **Reference pages** (`configuration.md`, `extensibility.md`, `import-syntax.md`,
  `package-management.md`, `troubleshooting.md`): third person. "the views directory", not "your
  views directory". Second person is fine where it is genuinely addressing a decision the reader
  makes ("if you have npm, keep using it").
- **Tutorial pages** (`getting-started.md`, `for-frontend-developers.md`): second person throughout.
  These are walking someone through something and the "you" is real.

Never "we". JsxCore does things; the documentation does not have a narrator.

## 7. Rhetorical tics to ration

Each of these is good once per page and a tic at five:

- **"Not X, but Y."** As in "Not a reimplementation with a command line over it." Sharp when the
  wrong assumption is genuinely likely; noise otherwise.
- **The rule of three.** "a build pipeline, a second router, and a serialisation boundary." Use it
  when there are exactly three things, not to make a sentence sound finished.
- **"which is the point" / "which is what makes X practical".** Usually the sentence before it
  already made the point.

## 8. Structure

Every page:

```markdown
# Title

← [Documentation index](README.md)

One or two sentences saying what is on this page.

---

## First heading
```

- **Tutorial pages** end with **Where to go next**, a forward sequence.
- **Reference pages** end with **See also**, sideways links. Omit it if there is nothing to say.
- Headings are sentence case, and describe the subject rather than the reader's goal:
  `## Build ids and caching`, not `## How do I cache assets?`
- Tables for enumerable facts (options, exit codes, mappings). Prose for reasoning. Do not put
  reasoning in a table cell, and never interrupt a table with prose: it silently breaks the
  rendering.

## 9. Code examples

- Must compile against the current API. `JsxRuntimeLayout.Builtin()` and `compilation.Current` were
  both in these docs for months and neither existed.
- Show the imports when the example is a whole file; omit them when it is a fragment of one already
  established on the page.
- Comment the surprising line, not the obvious one.
- Prefer the shortest example that is still real. An example that skips required setup is worse
  than a longer one.

## 10. British spelling

`serialise`, `minimise`, `behaviour`, `licence` (noun). API names keep their real spelling:
`JsonSerializerOptions`, `MinifierPath`, `Minify`.

## 11. Before committing

- Every code identifier in the change exists. Grep the source; do not trust memory.
- Every relative link resolves, and every `#anchor` matches a real heading.
- Nothing in the change is stated in another file. If it is, link instead.
- No em-dashes.
- Read the change aloud. Meta-sentences are easy to hear and hard to see.
