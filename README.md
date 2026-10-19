# VL.NetTopologySuite.GeoJSON

**GeoJSON for [VL.NetTopologySuite](https://github.com/rednotfound/VL.NetTopologySuite), as nodes
for [vvvv gamma](https://vvvv.org).**

Read a GeoJSON document into NetTopologySuite features, and write features back out. Three nodes, in
VL.NetTopologySuite's own `NetTopologySuite.IO` category beside `Read WKT` and `Write WKT`.

> **Status: `0.0.1-alpha`, the first release, [on nuget.org](https://www.nuget.org/packages/VL.NetTopologySuite.GeoJSON) since 2026-10-18 — a prerelease, early.** It needs VL.NetTopologySuite
> 0.0.4-alpha (on nuget.org since 2026-10-18), which installs with it. Every node resolves in a
> headless `vvvvc` compile of all six help patches, 86 tests pass, the category is proved, and the
> changed help patches were run in vvvv 7.4. F1 is not yet checked. See
> [docs/ROADMAP.md](docs/ROADMAP.md#verified).

Until 2026-10-18 this was **VL.GeoJSON**, with four nodes of its own and no dependency on any
sibling. Why it changed is in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#the-package-boundary).

## Install and your first features

VL.NetTopologySuite.GeoJSON is on [nuget.org](https://www.nuget.org/packages/VL.NetTopologySuite.GeoJSON).
Verified in vvvv gamma **7.4**.

**1. Install.** In vvvv gamma 7.x: Quad menu → **Manage Nugets → Commandline**, then

```
nuget install VL.NetTopologySuite.GeoJSON -pre
```

`-pre` is needed because every version so far is a prerelease. It brings
[VL.NetTopologySuite](https://github.com/rednotfound/VL.NetTopologySuite) 0.0.4-alpha along, plus
NetTopologySuite, NetTopologySuite.Features and NetTopologySuite.IO.GeoJSON4STJ — every node here
names a type from one of them. vvvv gamma 8.0 previews also have **Packman** (Ctrl+F3: Browse → the
pack → Add); that path has not been tried with this package — if it does not show up there, use the
command line above.

**2. See it work.** Press **F1** with nothing selected to open the Help Browser, go to **Learn**
and find VL.NetTopologySuite.GeoJSON. Start with `Explanation Overview of available nodes`, then
`HowTo Read GeoJSON`: a text box of GeoJSON becomes a spread of features, with their count and a
`Status`.

**3. Use it in your own patch.** Installing does not reference a package by itself. In your
document: **Document menu → Dependencies → NuGets**, right-click **VL.NetTopologySuite.GeoJSON** to
reference it — and **VL.NetTopologySuite** as well, whose `Split` and `GetAttribute` open a
feature. Then double-click the canvas and type `Read GeoJSON`. The smallest chain:

```
FileReader (String)  →  Read GeoJSON  →  Count            a small file
GeoJSONReader (Reactive)  →  HoldLatest  →  Count         a big one, read in the background
```

Then `GetSlice` picks a feature, `Split` opens it into `Geometry` and `Attributes`, and
`GetAttribute` reads one property by name.

## Learning it

Six help patches, in the order the help browser shows them:

| | |
|---|---|
| **Explanation Overview of available nodes** | the front door — what the package is, its three nodes, and what it deliberately does not have |
| **HowTo Read GeoJSON** | text in one box, features out. Break the text on purpose and watch `Status` |
| **HowTo Read a GeoJSON file** | `FileReader (String) [IO]` → `Read GeoJSON` for a small file, and when to reach for `GeoJSONReader (Reactive)` instead |
| **HowTo Access properties** | `Split` and `GetAttribute` side by side, and what type each JSON value arrives as |
| **HowTo Write GeoJSON** | a full round trip, and the ring-winding pin that decides whether it is exact |
| **HowTo Handle a large GeoJSON file** | `GeoJSONReader (Reactive)` → `HoldLatest`, reading in the background, beside the blocking `FileReader` → `Read GeoJSON` |

```text
GeoJSON text
     ↓
Read GeoJSON            ← parses when the text CHANGES, not every frame
     ↓
Features (Spread) ─────────────────→ VL.Mapsui  FeatureLayer → Map
     ↓  GetSlice / Count / ForEach ── vvvv's own spread nodes
     ↓  Split                      ── VL.NetTopologySuite
Geometry ──────────────────────────→ VL.NetTopologySuite  Buffer / Area / Intersection
Attributes (Dictionary) → TryGetValue      … or GetAttribute, for one by name
```

**The types crossing every arrow are NetTopologySuite's own** — `Feature` and `Geometry`. Not
wrappers, and no adapter anywhere. GeoJSON calls a feature's table *properties*; NetTopologySuite
calls the same table *attributes*, and the nodes that open it use NetTopologySuite's word.

## Nodes

| | |
|---|---|
| `Read GeoJSON` | text + `Factory` → `Features` (a Spread), `Success`, `Status`, `Parses` |
| `GeoJSONReader (Reactive)` | `File Path` + `Read` + `Factory` → `Output` (an observable of the features; `HoldLatest` it), `Progress`, `In Progress`, `On Completed`, `Success`, `Status`. Reads and parses on another thread: a big file does not freeze vvvv |
| `Write GeoJSON` | features + `Exact Round Trip` + `Indent` → text, `Success` |

A feature is opened with **VL.NetTopologySuite's** `Split` (→ `Geometry`, `Attributes`) and
`GetAttribute` (→ one by name, with `Success`, never throws). That package installs with this one.

Deliberately absent, each for a reason worth reading:

- **A `FeatureCollection` type**, and therefore any node to open one. `Read GeoJSON` hands back the
  features directly, the way `XMLReader` and `JSONReader` hand back an `XElement` rather than a
  document. [Why](docs/ARCHITECTURE.md#there-is-no-featurecollection).
- **`Count`** — the result is a `Spread`, so `Count [Collections.Spread]` already works on it, as do
  `GetSlice` and a `ForEach` region.
- **A blocking file node** — `FileReader (String) [IO]` → `Read GeoJSON` is one. The one file node
  here reads in the background, because a big file must not freeze vvvv.
  [Why](docs/ARCHITECTURE.md#geojsonreader-reactive--the-one-file-node-2026-10-18).

## Two things to know before using it

**Parsing is a process node, not an operation.** 1,000 features cost 0.8 of a frame and 10,000 cost
**9.7 frames**, so re-parsing every frame is not slow — it is a patch that never draws again. The
text is parsed when it changes; watch the `Parses` pin settle at 1.

**Decoded geometry gets SRID 4326.** RFC 7946 §4 mandates WGS84 longitude and latitude, and the
upstream library stamps it unaided — so this is a fact about the format, not a guess about the
data. **It differs from `Read WKT` right beside it, which gives 0**, because WKT carries no CRS
semantics. NTS **never throws on mixed SRIDs**, so if a patch combines both, make them agree
through the `Factory` pin. Setting an SRID is a label, never a reprojection — nothing here
reprojects anything.

## Requirements

vvvv gamma 7.2 or newer (.NET 8). Depends on **VL.NetTopologySuite 0.0.4-alpha**, NetTopologySuite
2.6.0, NetTopologySuite.Features 2.1.0 and NetTopologySuite.IO.GeoJSON4STJ 4.0.0 — the same NTS
versions the sibling packages pin, so a vvvv loading all of them sees one assembly identity.

No Newtonsoft.Json. [Why](docs/MEASUREMENTS.md#which-serializer-and-why): same parse speed, half
the retained memory, and the only ring-orientation control of the two.

## Building

```powershell
# vvvv must be CLOSED first - it holds the staged assemblies open
.\build.ps1                     # build + stage dist\ + install upstream packages into deps\
dotnet test test\VL.NetTopologySuite.GeoJSON.Tests\VL.NetTopologySuite.GeoJSON.Tests.csproj
.\tools\Test-VLPackage.ps1      # static package checks
.\tools\Test-VLPatch.ps1        # structural checks on every .vl - BOM, IDs, link endpoints
.\pack.ps1                      # pack into dist\feed\

# needs VL.NetTopologySuite built and packed too: D:\2026_Projects\vl-nettopologysuite\dist
.\tools\Compile-HelpPatches.ps1 # headless vvvvc over every patch, then READS the generated C#
                                # to check each node actually resolved - vvvvc's exit code does not

# load measurements, off by default because they take ~10 s and allocate hundreds of MB
$env:VLGEOJSON_LOAD = '1'; dotnet test -c Release --filter LoadTests

# open a help patch to read or adjust it: double-click Open-HelpPatch.cmd in the repository root
# (a picker in Help.xml order, with Close my vvvv / Normalize / Check), or by name:
.\tools\Open-HelpPatch.ps1 "Access"

# the only thing that proves where a node appears in the browser:
& "C:\Program Files\vvvv\vvvv_gamma_7.4-win-x64\vvvv.exe" `
    ".\help\VL.NetTopologySuite.GeoJSON\HowTo Read GeoJSON.vl" `
    --package-repositories ".\dist;.\deps;D:\2026_Projects\vl-nettopologysuite\dist"
```

## Documentation

| | |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | **the design.** Why each node exists, why there is no `FeatureCollection`, the SRID decision, the package boundary, and what will never be here |
| [docs/PITFALLS.md](docs/PITFALLS.md) | **everything that went wrong**, what it cost, and what now stops it recurring. Almost none of it produced an error message |
| [docs/MEASUREMENTS.md](docs/MEASUREMENTS.md) | every number, the version it was measured against, and an explicit list of what has *not* been measured |
| [docs/RULES.md](docs/RULES.md) | packaging and runtime rules, carried from the sibling repositories. Read before writing a node |
| [docs/VL-PATCH-XML.md](docs/VL-PATCH-XML.md) | the `.vl` serialization format and its commit gate. Read before hand-editing a patch |
| [docs/ROADMAP.md](docs/ROADMAP.md) | next / later / never |
| [CONTRIBUTING.md](CONTRIBUTING.md) | the build loop, and **which check proves which claim** |

Two of those are copied from `vl-mapsui` rather than linked, on that repository's own principle:
**a rule transfers; a note about another repository's file does not.**

## Status

0.0.1-alpha is on nuget.org. What is verified, and what is not, is tabulated in
[docs/ROADMAP.md](docs/ROADMAP.md#verified) — the distinction between *a node resolved*, *it is in
the right category* and *it computes the right value* is three separate claims here, and only the
last two need a running vvvv.

## Licence

MIT. See [LICENSE](LICENSE).

This package wraps [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) and
[NetTopologySuite.IO.GeoJSON4STJ](https://github.com/NetTopologySuite/NetTopologySuite.IO.GeoJSON),
both BSD-3-Clause, and declares them as NuGet dependencies rather than redistributing them.

`help\VL.NetTopologySuite.GeoJSON\Assets\ne_10m_populated_places_simple.geojson` - 7,342 cities, used
by `HowTo Handle a large GeoJSON file` - is [Natural Earth](https://www.naturalearthdata.com)'s 1:10m
populated places, **public domain**, taken unmodified from
[nvkelso/natural-earth-vector](https://github.com/nvkelso/natural-earth-vector) on 2026-10-18.
