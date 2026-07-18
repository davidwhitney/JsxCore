# Model types

← [Documentation index](README.md)

Describing a view model twice, once as a C# record and once as a TypeScript interface, is the kind
of duplication that goes stale. JsxCore generates the TypeScript from the .NET types.

---

## It works by convention

**A conventional MVC application needs no configuration for this at all.** Everything in a `Models`
or `ViewModels` namespace is exported automatically:

```csharp
namespace MyApp.Models;

public sealed record CatalogueModel(string Heading, IReadOnlyList<Product> Products)
{
    public Product? Featured { get; init; }
}

public sealed record Product(int Id, string Name, decimal Price, Availability Availability)
{
    [JsonPropertyName("sku")]
    public string StockKeepingUnit { get; init; } = "";
}

[JsonConverter(typeof(JsonStringEnumConverter<Availability>))]
public enum Availability { InStock, Backordered, Discontinued }
```

No attributes. No registration. Import the root namespace in a view:

```tsx
import type { MyApp } from "@jsxcore/generated";

export default function Catalogue({ model }: ViewProps<MyApp.Models.CatalogueModel>) {
    return <h1>{model.heading}</h1>;
}
```

The specifier is explicit on purpose: `@jsxcore/generated` makes it obvious at the import site
that these types come from .NET and that editing the declaration file is pointless.

---

## Namespaces are mirrored

Everything lands in **one declaration file**, with a TypeScript namespace per .NET namespace, so
the generated types read the way the application is organised, with no file sprawl:

```ts
export declare namespace MyApp.Models {
    type Availability =
        | "InStock"
        | "Backordered"
        | "Discontinued";

    interface Product {
        id: number;
        name: string;
        price: number;
        availability: Availability;
        sku: string;
    }

    interface CatalogueModel {
        heading: string;
        products: Product[];
        featured?: Product | null;
    }
}

export declare namespace MyApp.Models.Catalogue {
    interface Product {
        code: string;
        availability: MyApp.Models.Availability;
    }
}
```

The two `Product` records stay distinct, referenced as they are in C#.

A type is reachable at its namespace path and **nowhere else**. Nothing is re-exported to the top
level, so there is one way to name any given type and a rename cannot leave a second one
quietly working. Only types with no .NET namespace at all sit at the module's top level.

Trim a long root namespace, or drop namespaces entirely:

```csharp
options.TypeDefinitions.TrimNamespacePrefix = "MyApp";   // MyApp.Models.Product -> Models.Product
options.TypeDefinitions.MirrorNamespaces = false;        // everything at the top level
```

---

## What gets generated

Declarations describe the model **as it arrives in JavaScript**, so they follow your
`JsonSerializerOptions` rather than the .NET shape:

| .NET | TypeScript | |
|---|---|---|
| `string`, `Guid`, `Uri`, `char` | `string` | |
| `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan` | `string` | what JSON actually carries |
| `int`, `long`, `decimal`, `double`, ... | `number` | |
| `byte[]` | `string` | base64 |
| `bool` | `boolean` | |
| `string?`, `int?` | `name?: T \| null` | from nullable reference annotations |
| `List<T>`, `T[]`, `IReadOnlyList<T>` | `T[]` | |
| `Dictionary<string, T>` | `Record<string, T>` | |
| `KeyValuePair<K,V>` | `{ key: K; value: V }` | |
| `object`, `JsonElement` | `unknown` | |
| `enum` | `0 \| 1` or `"A" \| "B"` | follows your enum converter |
| nested classes/records | referenced interface | emitted transitively |

Also honoured:

- **`[JsonPropertyName]`**: the emitted property uses the JSON name
- **`[JsonIgnore]`**: omitted entirely
- **Naming policy**: camelCase by default, or whatever you configured
- **Nullable reference types**: a nullable property becomes optional *and* `| null`
- **Recursive models**: handled without looping
- **Generic types**: `Paged<Order>` emits as `PagedOrder`

Types referenced by an exported model are emitted automatically, so you rarely have to name more
than the top-level ones.

---

## Choosing what gets exported

The default is `Models`/`ViewModels` namespaces plus anything marked `[JsxModel]`. Assign
`options.AutoExport` to take over:

```csharp
// A namespace, and everything nested inside it
options.AutoExport = TypesFrom.NamespaceContaining<OrderModel>(includeChildNamespaces: true);

// Just that one namespace
options.AutoExport = TypesFrom.NamespaceContaining<OrderModel>(includeChildNamespaces: false);

// Everything the application defines, no namespaces to name, no attributes to add
options.AutoExport = TypesFrom.AllUserCode;

// Compose
options.AutoExport = TypesFrom.NamespaceContaining<OrderModel>()
                   + TypesFrom.NamespaceContaining<AccountModel>()
                   + TypesFrom.Type<LegacyPayload>();

// Compose and narrow
options.AutoExport = TypesFrom.AllUserCode
    .Where(t => t.Name.EndsWith("Model"))
    .Except<InternalDto>();

// Nothing but what is registered explicitly
options.AutoExport = TypesFrom.Nothing;
```

| Factory | Yields |
|---|---|
| `TypesFrom.NamespaceContaining<T>(includeChildNamespaces)` | the namespace holding `T`, from `T`'s assembly |
| `TypesFrom.Namespace(name, assembly, includeChildNamespaces)` | a named namespace |
| `TypesFrom.AssemblyContaining<T>()` / `InAssembly(a)` | a whole assembly |
| `TypesFrom.AllUserCode` | every assembly that is not framework or JsxCore |
| `TypesFrom.UserCode(predicate)` | user assemblies you select |
| `TypesFrom.ConventionalNamespaces(a, "Models", ...)` | namespaces named after a segment |
| `TypesFrom.MarkedTypes(a)` / `MarkedTypesIn<T>()` | `[JsxModel]` types |
| `TypesFrom.Types(...)` / `Type<T>()` | exactly those types |
| `TypesFrom.Matching(a, predicate)` | anything you like, unfiltered |
| `TypesFrom.Nothing` | nothing |

Sources compose with `+` (or `|`) and narrow with `.Where(...)` and `.Except(...)`. They are lazy;
nothing is enumerated until generation runs.

### What scanning skips

Pointing a source at a whole namespace does not produce a pile of empty interfaces. Scanning
excludes delegates, attributes, exceptions, static classes, open generic definitions and non-public
types. `TypesFrom.Types` and `TypesFrom.Matching` are exact and skip nothing.

`TypesFrom.AllUserCode` is the blunt option and is honest about it: it will pick up your services
and controllers too. Narrow it if that becomes noise.

### Conventional namespace names

```csharp
options.TypeDefinitions.ConventionalNamespaceNames.Clear();
options.TypeDefinitions.ConventionalNamespaceNames.Add("Contracts");
```

The match is on whole namespace **segments**, so `Models` picks up `MyApp.Models` and
`MyApp.Models.Catalogue` but not `MyApp.ModelBinding`.

---

## The attribute is for types defined elsewhere

`[JsxModel]` is not the main mechanism; the namespace convention is. Use the attribute for models
that live somewhere the convention does not reach: a shared contracts assembly, a domain namespace,
a DTO that sits next to the service that produces it.

```csharp
namespace MyApp.Integration;   // not a models namespace

[JsxModel]
public sealed record WebhookPayload(string Kind, string Signature);
```

Rename a single type with `[JsxModel(Name = "ProductSummary")]`.

Add individual types imperatively if you prefer:

```csharp
options.TypeDefinitions.Add<SomeExternalDto>();
```

Explicit additions survive alongside whatever `AutoExport` yields.

---

## When generation happens

Twice, from the same generator.

**During the build**, from the assembly the build just produced. The assembly is loaded for
inspection only; your `Program.cs` never runs, so nothing it does at startup happens on a build
machine. This is what makes a **fresh clone** type-check: `dotnet build` on a machine that has
never run the application still compiles views that import model types.

**At application startup**, from the types actually loaded in the running process.

Output goes to `obj/JsxCore/types/index.d.ts` by default, so nothing generated appears in your
source tree, and the file is only rewritten when its contents change, which keeps the asset build
id, and therefore browser caches, stable across restarts.

### Why both

The build cannot see your configuration. `AutoExport`, a naming policy on `JsonSerializerOptions`,
`EnumsAsStrings` and the rest are set in application code that has not run, so the build generates
with the defaults. For an application that has not customised them, which is most, the two are
identical, and there is a test asserting exactly that.

Customise them and the build's answer is an approximation until the application runs and replaces
it. If that shows up as type errors on a fresh clone that disappear after running the app, that is
what you are seeing. Point the output at a location you commit to skip the approximation entirely:

```csharp
options.TypeDefinitions.OutputPath = "Views/generated";
```

If the assembly cannot be loaded at all, the compiler is given an ambient declaration for
`@jsxcore/generated` instead, so imports from it type as `any` rather than failing to resolve.
Build-time generation can be turned off with
`<JsxCoreGenerateModelTypes>false</JsxCoreGenerateModelTypes>`.

---

## Turning it off

```csharp
options.TypeDefinitions.Enabled = false;
```

Views then hand-write their own model interfaces, as they would without this feature.
