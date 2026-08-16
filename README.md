# VL.GeoJSON

**GeoJSON made natural inside [vvvv gamma](https://vvvv.org).**

Read a GeoJSON document, take its features apart into geometry and properties, and write them back
out. Four nodes, one category.

> **EARLY — not ready for real work yet.** Nothing is published to nuget.org. Every node resolves in
> a headless `vvvvc` compile of all six help patches and 69 tests pass; the vvvv GUI check is still
> outstanding. See [docs/ROADMAP.md](docs/ROADMAP.md#verified).

## Learning it

Six help patches, in the order the help browser shows them:

| | |
|---|---|
| **Explanation Overview of available nodes** | the front door — what the package is, and the two nodes it deliberately does not have |
| **HowTo Read GeoJSON** | text in one box, features out. Break the text on purpose and watch `Status` |
| **HowTo Read a GeoJSON file** | `FileReader (String) [IO]` → `Read GeoJSON`, and why there is no file node here |
| **HowTo Access properties** | `TryGetValue` on the properties, and what type each JSON value arrives as |
| **HowTo Write GeoJSON** | a full round trip, and the ring-winding pin that decides whether it is exact |
| **HowTo Handle a large GeoJSON file** | a frame counter climbing while `Parses` stays at 1 |

```text
GeoJSON text
     ↓
Read GeoJSON            ← parses when the text CHANGES, not every frame
     ↓
Features (Spread) ─────────────────→ VL.Mapsui  FeatureLayer → Map
     ↓  GetSlice / Count / ForEach ── vvvv's own spread nodes, not ours
     ↓  Split
Geometry ──────────────────────────→ VL.NetTopologySuite  Buffer / Area / Intersection
Properties (Dictionary) → TryGetValue      … or GetProperty, for one by name
```

**The types crossing every arrow are NetTopologySuite's own** — `Feature` and
`Geometry`. Not wrappers. So data read here goes straight into VL.NetTopologySuite and VL.Mapsui
with **no adapter and no dependency in either direction**: neither of those packages has ever heard
of this one.

## Nodes

| | |
|---|---|
| `Read GeoJSON` | text + `Factory` → `Features` (a Spread), `Success`, `Status`, `Parses` |
| `Split` | Feature → `Geometry`, `Properties` (a Dictionary) |
| `GetProperty` | Feature + `Name` → `Result`, `Success` — never throws |
| `Write GeoJSON` | features + `Exact Round Trip` + `Indent` → text, `Success` |

Deliberately absent, each for a reason worth reading:

- **A `FeatureCollection` type**, and therefore any node to open one. `Read GeoJSON` hands back the
  features directly, the way `XMLReader` and `JSONReader` hand back an `XElement` rather than a
  document. [Why](docs/ARCHITECTURE.md#there-is-no-featurecollection-and-therefore-only-one-split).
- **`Count`** — the result is a `Spread`, so `Count [Collections.Spread]` already works on it, as do
  `GetSlice` and a `ForEach` region.
- **`Read GeoJSON File`** — VL.CoreLib already ships `FileReader (String) [IO]` with a `Read`
  trigger. `FileReader → Read GeoJSON` composes, and this package stays out of path semantics.
  (vvvv itself ships both halves for XML and JSON, so this one is a genuine open question — see
  [docs/ROADMAP.md](docs/ROADMAP.md).)

## Two things to know before using it

**Parsing is a process node, not an operation.** 1,000 features cost 0.8 of a frame and 10,000 cost
**9.7 frames**, so re-parsing every frame is not slow — it is a patch that never draws again. The
text is parsed when it changes; watch the `Parses` pin settle at 1.

**Decoded geometry gets SRID 4326.** RFC 7946 §4 mandates WGS84 longitude and latitude, and the
upstream library stamps it unaided — so this is a fact about the format, not a guess about the
data. **It differs from VL.NetTopologySuite's `Read WKT`, which gives 0**, because WKT carries no
CRS semantics. NTS **never throws on mixed SRIDs**, so if a patch combines both, make them agree
through the `Factory` pin. Setting an SRID is a label, never a reprojection — nothing here
reprojects anything.

## Requirements

vvvv gamma 7.2 or newer (.NET 8). Depends on NetTopologySuite 2.6.0,
NetTopologySuite.Features 2.1.0 and NetTopologySuite.IO.GeoJSON4STJ 4.0.0 — the same NTS versions
the sibling packages pin, so a vvvv loading all three sees one assembly identity.

No Newtonsoft.Json. [Why](docs/MEASUREMENTS.md#which-serializer-and-why): same parse speed, half
the retained memory, and the only ring-orientation control of the two.

## Building

```powershell
# vvvv must be CLOSED first - it holds the staged assemblies open
.\build.ps1                     # build + stage dist\ + install upstream packages into deps\
dotnet test test\VL.GeoJSON.Tests\VL.GeoJSON.Tests.csproj
.\tools\Test-VLPackage.ps1      # static package checks
.\tools\Test-VLPatch.ps1        # structural checks on every .vl - BOM, IDs, link endpoints
.\pack.ps1                      # pack into dist\feed\

.\tools\Compile-HelpPatches.ps1 # headless vvvvc over every patch, then READS the generated C#
                                # to check each node actually resolved - vvvvc's exit code does not

# load measurements, off by default because they take ~10 s and allocate hundreds of MB
$env:VLGEOJSON_LOAD = '1'; dotnet test -c Release --filter LoadTests

# the only thing that proves where a node appears in the browser:
& "C:\Program Files\vvvv\vvvv_gamma_7.4-win-x64\vvvv.exe" `
    ".\help\VL.GeoJSON\HowTo Read GeoJSON.vl" --package-repositories ".\dist;.\deps"
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

Nothing is published to nuget.org. What is verified, and what is not, is tabulated in
[docs/ROADMAP.md](docs/ROADMAP.md#verified) — the distinction between *a node resolved*, *it is in
the right category* and *it computes the right value* is three separate claims here, and only the
last two need a running vvvv.

## Licence

MIT. See [LICENSE](LICENSE).

This package wraps [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) and
[NetTopologySuite.IO.GeoJSON4STJ](https://github.com/NetTopologySuite/NetTopologySuite.IO.GeoJSON),
both BSD-3-Clause, and declares them as NuGet dependencies rather than redistributing them.
