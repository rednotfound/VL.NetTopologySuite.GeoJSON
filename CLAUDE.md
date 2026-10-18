# CLAUDE.md

Guidance for Claude Code (claude.ai/code) working in this repository.

## Project overview

**VL.NetTopologySuite.GeoJSON** wraps [NetTopologySuite.IO.GeoJSON4STJ](https://github.com/NetTopologySuite/NetTopologySuite.IO.GeoJSON)
as nodes for [vvvv gamma](https://vvvv.org): **the GeoJSON half of VL.NetTopologySuite.** One job,
**read and write GeoJSON**. Nothing about geometry algorithms, maps, rendering or reprojection —
and nothing about opening a feature either, which is VL.NetTopologySuite's.

**Until 2026-10-18 this was `VL.GeoJSON`**, with four nodes of its own (`Read GeoJSON`,
`Split (Feature)`, `GetProperty`, `Write GeoJSON`) and no dependency on any sibling. It was renamed,
took a dependency on VL.NetTopologySuite, moved `GetProperty` there as `GetAttribute`, dropped its
own `Split`, and joined `NetTopologySuite.IO`. The reasons are in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#2026-10-18-vlgeojson-became-vlnettopologysuitegeojson);
do not undo any of it without reading them. The GitHub repository is renamed by the maintainer.

**Current state (2026-10-18): 2 nodes, 60 tests + 4 load tests, 6 help patches. Static checks and a
headless `vvvvc` compile of every patch are green; the GUI check is outstanding — including the
category, which the rename moved.** Nothing is published to nuget.org, and **nothing can be until
VL.NetTopologySuite 0.0.3-alpha (the first with `GetAttribute`) is.** See
[Verification](#verification--be-precise-about-which-one-you-have).

**Help is the teaching surface here, not a fatter node.** Two things a user needs are deliberately
*not* nodes of this package — reading a file (`FileReader`) and opening a feature
(VL.NetTopologySuite's `Split` / `GetAttribute`) — so the help patches are where those get taught.
That is why there are six patches against two nodes, and why the ratio should keep going up
rather than the node count.

The sibling repositories:

- `D:\2026_Projects\vl-nettopologysuite` (`VL.NetTopologySuite`) — geometry, and the feature:
  `Feature`, `Split`, `GetAttribute`. **This package depends on it** (nuspec only — no node here
  needs it, the help does).
- `D:\2026_Projects\vl-mapsui` (`VL.Mapsui`) — map rendering. **Consumes
  `NetTopologySuite.Features.Feature`, which is why this package emits exactly that.** Also depends
  on VL.NetTopologySuite; neither it nor this package references the other.
- `D:\2026_Projects\vl-overworld` (`VL.Overworld`) — tutorials; depends on this package by id.
- `VL.GIS` — an earlier package, deliberately not a constraint.

Every dependency arrow points at VL.NetTopologySuite, and **VL.NetTopologySuite references nothing
of ours. Keep it that way** — a format or map package may depend on the geometry package, never the
reverse, and never on each other.

**Only this repository's `CLAUDE.md` loads automatically.** Read a sibling's before touching it, and
do not assume a rule written there applies here unless it has been copied.

## Read these before touching anything

| | |
|---|---|
| [`docs/RULES.md`](docs/RULES.md) | before writing a **node**. Carried verbatim from the siblings — packaging, runtime, node design |
| [`docs/VL-PATCH-XML.md`](docs/VL-PATCH-XML.md) | before hand-editing a **`.vl`**. Carried from `vl-mapsui`, plus what this package added |

**Not having the second one is what made most of a GUI round necessary here.** Its line on IOBoxes —
*"FOUR-number Bounds, plus Value **and a TypeAnnotation**"* — is a single sentence, and six help
patches shipped annotation boxes with a `Value` and no `TypeAnnotation`. They rendered as empty
rectangles and their text came out as warnings in the Debug panel. Nothing static complained.

The rule the siblings state and this repository re-learned: **a rule transfers; a note about another
repository's file does not.** Both documents are copied, not linked, for exactly that reason — and
both had already grown in `vl-mapsui` while the copy here stayed frozen.

### The two rules from `RULES.md` that bite hardest:

1. **`[assembly: ImportAsIs(Namespace = "VL")]` is mandatory.** Without it the package loads,
   compiles, packs and exports with zero warnings and contributes **no visible nodes**. Nine VL.GIS
   releases shipped that way.
2. **An upstream library must be a *package in a package repository*, not just a
   `<NugetDependency>` line.** **This failure is total here**: every node names an NTS or
   NTS.Features type. `build.ps1` installs them into `deps\`. The VL.NetTopologySuite dependency is different: `build.ps1`
skips `VL.*` NugetDependencies (they normally ship inside vvvv), so the help needs that sibling
built and packed — `Compile-HelpPatches.ps1` adds its `dist\` and `dist\feed\` itself.

And: **a `public static` method is a node evaluated sixty times a second from the moment the
document opens.** That is why `Read GeoJSON` is a `[ProcessNode]` — see below.

## The two things this package adds to the upstream library

### 1. Normalisation, because the serializer does not return the public NTS types

Measured against GeoJSON4STJ 4.0.0:

| | |
|---|---|
| a decoded feature | `StjFeature` — **`internal`**, `IFeature` only, **is not a `Feature`** |
| its attributes | `JsonElementAttributesTable` — **immutable**, throws on write, not castable to `AttributesTable` |
| every JSON number | `System.Decimal`, integers included |
| the GeoJSON `id` member | on `IUnique.Id`, **not** in the properties |
| `Attributes` with no `properties` member | **`null`** |

**VL.Mapsui's `FeatureLayer` takes `IEnumerable<NetTopologySuite.Features.Feature>` — the concrete
class.** So without `Defaults.Normalise` the pipeline this package exists to prove does not
compile. It costs **22 ms per 10,000 features, 12% of the parse**, and it is **negative-tested**:
putting the raw table into the `Feature` turns **10 of 60 tests red**, 2 of them interop; making
only `NormaliseValue` return the raw value turns **6** red (2026-10-18; it was 47 of 69 before the
feature nodes moved out).

Do not "simplify" this away.

### 2. Parsing when the input changes, not every frame

**162 ms for 10,000 features against a 16.7 ms frame.** Even 1,000 features cost 0.8 of a frame.
`ReadGeoJsonNode` caches on (text, factory) and returns **the same instance** in between — identity
is the only "has this changed" signal VL has. Negative-tested: removing the cache check turns **4
tests red**, the first reporting 100 parses instead of 1.

## Things measured, so they need not be re-derived

Full table with dates in [`docs/MEASUREMENTS.md`](docs/MEASUREMENTS.md). The load-bearing ones:

| | |
|---|---|
| Both GeoJSON packages | resolve **`NetTopologySuite.Features 2.1.0`** — the exact version VL.Mapsui pins. No conflict anywhere |
| Default SRID from `new GeoJsonConverterFactory()` | **4326.** Upstream stamps it; RFC 7946 §4 mandates WGS84 lon/lat |
| `GeoJsonConverterFactory(factory)` | adopts the factory's SRID exactly, 0 included |
| Mixing SRIDs in NTS | **never throws.** `Read WKT` gives 0 and `Read GeoJSON` gives 4326 — a real hazard, documented on both |
| Legacy `crs` member | accepted and **silently ignored** |
| Ring orientation | **not** touched on read; **rewound** on write by default. Round trip is `EqualsTopologically` but not `EqualsExact` unless `DoNotModify` |
| `{"hello":"world"}` | **succeeds with 0 features.** The count is the only signal — hence `Status` |
| `IAttributesTable` indexer, missing name | **throws `ArgumentException`** — which is why `GetAttribute` (born here as `GetProperty`, now VL.NetTopologySuite's) calls `Exists` first and reports on a `Success` pin |
| `Deserialize<Feature>` (concrete) | `InvalidCastException`. Only `IFeature` works |
| Streaming | **not supported.** Materialises the whole document |
| Newtonsoft alternative | same speed, **2× retained memory** (101 MB vs 49 at 50k), extra dependency |
| **Cost scales with feature COUNT, not coordinates** | 10,000 polygons × 20 verts = 148 ms; 200 × 1,000 verts = 83 ms, same coordinate total |
| `Spread<T>` from VL.Core | satisfies VL.Mapsui's `IEnumerable<Feature>` pin, and `GetSlice` / `Count` type correctly against it |
| `[return: Pin(Name = "...")]` on a `[ProcessNode]` `Update` | **honoured.** The generated C# names the variable after the pin — `var Features_5 = …`, not `Result_5`. `tools\Compile-HelpPatches.ps1` asserts this, because a wrong pin name drops the link silently while the node still resolves |

### Conventions measured across the shipped packs, not guessed

Established by parsing **874 `.vl` files** (the 45 packs shipped with vvvv 7.4, plus the siblings).
These cost a full survey; do not re-derive them.

| | |
|---|---|
| **A reader never hands out the document type** | `XMLReader`, `Parse [XML]`, `JSONReader`, `ParseJSON` all do `XDocument (Split) → Root` internally and return an `XElement`. Document-typed variants are quarantined in an `Advanced` sub-canvas. **This is why there is no `FeatureCollection` here** |
| `Split` | field projection on a record. **247 categories, 1,119 calls**, and the output pins are *always* the type's own member names |
| `Split` on a real collection | **never happens.** Only `String`/`Regex`, which are themselves sequences — and even those are suffixed, `Split (String)`. For collections the vocabulary is `FromSequence` / `ToSpread` |
| Two same-named `Split`s in one category | **0 precedents in 815 files.** vvvv suffixes instead: `Split (Edges)`, `Split (Componentwise)` |
| `Get…` prefix | **implies a `Success` pin and no throwing.** `GetAttributeValue`, `GetValue`, `GetValues`, `XPathGetValue` … A non-`Get` accessor returns a raw possibly-null object with no `Success` |
| The Split/**Join** pair in `docs/RULES.md` | **only 9% honoured.** The real pair is **`Create`/`Split`** — 294 of 302 groups (97%) that define `Split` also define `Create`. RULES.md is carried verbatim from the siblings and is wrong on this one point |
| Both entry points ship | vvvv gives each format a file reader *and* a string parser: `XMLReader` + `Parse [XML]`, `JSONReader` + `ParseJSON` |
| Options are pins, not node variants | `All Descendants`, `Deserialize Root Element Name`, `Indent`, `Omit Root Object` |

## Verification — be precise about which one you have

| | proves | state |
|---|---|---|
| `dotnet test` | the semantics are right | ✅ 60 tests, ~100 ms, no network |
| the negative tests | those checks can actually go red | ✅ 10 red with the raw table, 6 with raw values, 4 without the cache (2026-10-18) |
| `tools\Test-VLPatch.ps1` | the `.vl` documents are well formed | ✅ 7 documents |
| `tools\Test-VLPackage.ps1` | the package can structurally contribute nodes | ✅ passes |
| `tools\Compile-HelpPatches.ps1` | every node in every patch **resolved** — ours and VL.NetTopologySuite's | ✅ 6 patches, read out of the generated `*.vl.1.cs` |
| the vvvv **NodeBrowser** | **which category a node is in** — `NetTopologySuite.IO` is expected | ❌ **not yet checked** |
| the vvvv **GUI, running** | the patch computes the right value | ❌ **not yet checked** |

**The last three rows are three different claims, and this is where a false proof lives.**

- **`LastCategoryFullName` in a `.vl` is a hint, not the truth.** A patch compiles fine with it set
  to nonsense. A green `vvvvc` proves a node *exists* and says **nothing** about its category.
- **A green compile is still worth having, because it is byte-level evidence.** An unresolved node
  has its links dropped, `vvvvc` exits 0 and nothing is red — so **read the generated
  `*.vl.1.cs`**, not the exit code. Here it confirms the whole chain in `Update` *and* that
  `new ReadGeoJsonNode()` sits in `__Create__` and never in `Update`.

### How to re-run the headless check

`.\build.ps1`, `.\pack.ps1`, then `.\tools\Compile-HelpPatches.ps1` — with VL.NetTopologySuite built
and packed in its own repository first. It compiles every patch and **reads the generated C#**
rather than trusting an exit code. Four things it encodes, each of which cost a round:

- **The package name is discovered, not written down** (a `.vl` at the root with a `.nuspec` of the
  same name). It anchors every node check, and when it was hardcoded a rename would have matched
  nothing and passed: every patch "uses no node of ours", green. Now a run that checks no node of
  ours fails, and so does a node the table does not know. Negative-tested 2026-10-18.

- **Use absolute paths** for both the document and the output directory. With a relative document
  path `vvvvc` dies inside `PathUtils.GetRelativePath` with `UriFormatException: Invalid URI`
  before generating anything — a failure that looks like a package problem and is not.
- A `NU1101` means restore did not find a package: the script drops a `NuGet.config` naming this
  repository's `dist\feed` and VL.NetTopologySuite's beside the generated projects, so a run after
  `.\pack.ps1` restores cleanly and reports "no NU1101".
- **Anchor on `public … __Create__(`, not on `__Create__`.** The first occurrence in the file is a
  *call* (`return instance.__Create__(Node_Context);`), and matching it searches the wrong region —
  which reported all five patches as constructing the process node in the wrong place. Nor will
  `[^)]*` cross the parameter list, since it contains `n3.Pin(...)`.

**Never leave vvvv running unattended, and never start it in the background.** Launch, read the
value, close. Opening a patch *is* running it — there is no idle state.

### Detect and refuse. Never kill.

`Stop-Process` and `taskkill` appear **zero times** in either sibling repository. Their pattern is
the inverse: `build.ps1` detects a running vvvv and *refuses*, telling the human to close it.
`tools\Compile-HelpPatches.ps1` here now does the same.

That guard was already in this repository's `build.ps1`, and it was bypassed — repeatedly — by
killing vvvv instead of closing it. What that cost, recorded because neither sibling documents it:

- **Force-killing vvvv corrupts its session state.** The next launch came back with an empty editor
  shell and an unrelated patch running in a Skia window, which looked exactly like a packaging
  failure and was not.
- **`Stop-Process -Name vvvv` is indiscriminate.** It ends every vvvv on the machine, including a
  session somebody else is using. If a process genuinely must be ended, end it **by PID**, and only
  one this session started.
- A patch left open with unsaved changes is *not* a reason to kill — it is a reason to ask.

## Repository layout

```
vl-geojson/
├── VL.NetTopologySuite.GeoJSON.vl / .nuspec   # the package. .vl is hand-edited, never regenerated
├── src/VL.NetTopologySuite.GeoJSON/
│   ├── AssemblyInfo.cs            # ImportAsIs(..., Category = "NetTopologySuite.IO")
│   ├── Defaults.cs                # ⭐ the factory, the cached options, and Normalise
│   ├── ReadNodes.cs               # [ProcessNode] Read GeoJSON - the lifetime-critical one
│   └── WriteNodes.cs              # Write GeoJSON
├── test/VL.NetTopologySuite.GeoJSON.Tests/   # 60 xunit tests + 4 opt-in load tests, no network, no vvvv
├── test/fixtures/                 # A-points, B-polygons, C-multipolygon, D-mixed, E-malformed
├── help/VL.NetTopologySuite.GeoJSON/         # 6 help patches + Help.xml (ordering and tags)
│   └── Assets/cities.geojson      # what "HowTo Read a GeoJSON file" points at, RELATIVELY
├── docs/ARCHITECTURE.md           # why each node exists, the SRID decision, the boundary
├── docs/VL-PATCH-XML.md           # ⭐ carried from vl-mapsui - read before hand-editing a .vl
├── docs/MEASUREMENTS.md           # ⭐ every number, and what has NOT been measured
├── docs/ROADMAP.md                # next / later / never
├── docs/RULES.md                  # ⭐ carried from the siblings - read before any node
├── build.ps1, pack.ps1
├── tools/                         # New-VLId, Find-Vvvv, Test-VLPackage, Test-VLPatch,
│                                  # Compile-HelpPatches, Normalize-HelpPatches, Test-Install
└── tools/legacy/                  # RETIRED generator + the .vltemplate files it read.
                                   # The checked-in .vl is the source of truth - see below
```

### The checked-in `.vl` is the source of truth; the generator is retired

`tools\legacy\Build-HelpPatch.ps1` and `tools\legacy\_templates\` built the six help patches and
made one structural refactor tractable. **They are retired and the script now throws.**

Both siblings reached the same place, one of them by losing a patch. Their rule reverses an earlier
"regenerate, never edit in place", which had been written when hand-patching produced thirteen
duplicated nodes and six colliding IDs: **regenerating turned out to be the more destructive of the
two once a patch was in use.** The mitigation moved from *avoid hand-editing* to **validate every
edit** — anchor each insertion on a match asserted to occur exactly once, fail loudly otherwise,
then run the three checkers.

The templates have already diverged: the annotation-box and `fontsize` fixes went into the `.vl`
only. That divergence is why both live in `legacy\` rather than beside the patches.

Fixture **F is generated, not committed** (`Fixtures.Synthetic`) — the point of A–E is that you can
read the file and the assertion side by side; the point of F is size. **A fixture generator is not
public API** and must stay in the test project.

## Node categories

**`NetTopologySuite.IO`** — VL.NetTopologySuite's own category, beside `Read WKT` and `Write WKT`:

```csharp
[assembly: ImportAsIs(Namespace = "VL.NetTopologySuite.GeoJSON", Category = "NetTopologySuite.IO")]
```

The namespace is stripped whole and the category named outright — the form `VL.Core.Commands`
ships (`Namespace = "VL.Core", Category = "System"`), found by scanning the 7.4 pack assemblies.
`[SkipCategory]` keeps `WriteNodes` from becoming a level of its own;
`[ProcessNode(Name = "Read GeoJSON", Category = "NetTopologySuite.IO")]` places the process node.
Not yet seen in the NodeBrowser — the generated C# carries no category, so nothing else proves it.

Namespace and RootNamespace equal the assembly name, as in VL.NetTopologySuite. **Inside that
namespace a bare `NetTopologySuite` means `VL.NetTopologySuite`**, so a fully qualified upstream
name in code or in a `cref` needs `global::` — it broke four references and two crefs on the day
of the rename.

## Adding a node

Answer these before writing it. If any answer is thin, it is a help patch rather than a node.

- **Can a patch reach the same result by wiring existing nodes?** Then it is a help patch. A
  `Count` node is absent for exactly this reason — the output is a `Spread`, so
  `Count [Collections.Spread]` already works on it.
- **Does VL or VL.CoreLib already ship it?** `Read GeoJSON File` is absent because
  `FileReader (String) [IO]` exists and has a `Read` trigger pin. (Weakest of the omissions: vvvv
  ships **both** a file reader and a string parser for XML and for JSON. See `docs/ROADMAP.md`.)
- **But do not mistake an implementation detail for a design conclusion.** `GetProperty` was left
  out at first because `IAttributesTable`'s indexer throws — and vvvv's own answer to exactly that
  is a `Success` pin, not a missing node. Ask what the shipped packs *do* before concluding a node
  should not exist.
- **Does it hold a resource, or is it expensive?** Then `[ProcessNode]`, not a static method.
- **Is it about GeoJSON?** If it is about geometry, maps or projections, it belongs in another
  package. This one must not grow an `Area` node just because it happens to hold geometry — and
  must not grow a feature node either: anything that opens or builds a `Feature` belongs in
  VL.NetTopologySuite, whoever produced the feature.
- **Three inputs is the target**; five means two decisions are wearing one node. Extra *outputs*
  are free — `Read GeoJSON` has four.

## Working style

- **Change one variable at a time.** Nine failed releases in a sibling came from editing the `.vl`,
  the csproj and the nuspec in one round with no idea which mattered.
- **Before believing a green result, name the mechanism by which it could have gone red.** Both
  central claims here are negative-tested for that reason.
- **Measure the library, do not trust prose about it.** Every surprising fact in
  `docs/MEASUREMENTS.md` — the internal feature type, the immutable attributes table, `System.Decimal`,
  the silent `crs`, the ring rewinding — came from a throwaway probe and none from documentation.
- **Watch for the C# conditional unifying its branches.** `cond ? Convert.ToInt64(d) : d` has type
  **`decimal`**, silently undoing the conversion. It compiled, ran, and made every whole number stay
  a decimal; the `(object)` cast in `NormaliseValue` is load bearing. Caught only because a test
  asserted the exact type.
- **Copy node XML verbatim from a shipped patch** rather than composing it — `GetSlice` and
  `FileReader (String)` here came out of `VL.CoreLib\help\IO\File\HowTo Read Strings.vl`,
  `TryGetValue` out of `VL.Fundamentals\...\HowTo Manage Items in a Dictionary.vl`, `FrameCounter`
  out of `VL.CoreLib\help\Animation\...`. Note their inner `<Choice>` / `<CategoryReference>`
  elements are **not in a consistent order** between nodes; copy each as shipped rather than
  normalising them.
- **An XML comment cannot contain `--`**; it cost a round on the csproj
  (`dotnet list package --include-transitive` inside a comment).
- **An unwired text IOBox is a convention, not a defect.** `Test-VLPatch.ps1`'s dangling-Pad rule
  originally flagged every annotation box in the new patches. The fix was to sharpen the rule, not
  to mangle the patches: a Pad with a `Value` and no `Comment` is prose (vvvv's own packs are full
  of them; VL.Mapsui's Explanation patch is nothing else), while a Pad with a `Comment` and no
  `Value` is a label waiting for a link and is still flagged. Both directions are negative-tested.
- **A help patch that reads a file must ship the file and point at it RELATIVELY.**
  `help\VL.NetTopologySuite.GeoJSON\Assets\cities.geojson`, referenced as `Assets\cities.geojson`, which resolves the
  same in the repository and in an installed package. Never an empty Path IOBox — `Value=""` means
  the document's own folder, and that is the mistake that wrote 444 stray files in a sibling.
- **Opening a help patch in vvvv rewrites its `NugetDependency` version and saving keeps it.** Run
  `tools\Normalize-HelpPatches.ps1` after any GUI session.
