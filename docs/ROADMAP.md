# Roadmap

What is here, what is next, what is deliberately absent, and what will never arrive.

The rule throughout: **an item moves from "later" to "next" when something actually needs it**, not
because the serializer has the option.

---

## Now — 0.0.1-alpha, unpublished

**VL.NetTopologySuite.GeoJSON** — `VL.GeoJSON` until 2026-10-18 ([why](ARCHITECTURE.md#2026-10-18-vlgeojson-became-vlnettopologysuitegeojson)).
**Three nodes in VL.NetTopologySuite's `NetTopologySuite.IO`. 82 tests, 4 load tests, six help
patches.** Depends on VL.NetTopologySuite **0.0.3-alpha**, the first with `GetAttribute` — **so it
cannot be published before that version is.**

```text
Read GeoJSON        [ProcessNode]  text + Factory -> Features (Spread), Success, Status, Parses
GeoJSONReader (Reactive)  [ProcessNode]  File Path + Read + Factory -> Output (observable),
                    Progress, In Progress, On Completed, Success, Status
Write GeoJSON       features + Exact Round Trip + Indent -> text, Success
```

The MVP path is implemented and covered, opening the feature with VL.NetTopologySuite's nodes:

```text
GeoJSON text -> Read GeoJSON -> Spread<Feature> -> GetSlice -> Split -> NTS Geometry   ✅
                                                            -> GetAttribute -> Value   ✅
NTS Feature -> Write GeoJSON -> text -> Read GeoJSON -> the same geometry               ✅
```

**No `FeatureCollection` type is exposed, and there is no `Count` pin.** Both are deliberate and
both are backed by a survey of the shipped packs — see
[ARCHITECTURE.md](ARCHITECTURE.md#there-is-no-featurecollection).

### Verified

| | proves | state |
|---|---|---|
| `dotnet test` | the arithmetic and the semantics are right | ✅ 61 tests, ~100 ms, no network |
| negative test: the raw attributes table into the `Feature` (the nearest to dropping `Defaults.Normalise` that still compiles) | the interop claim is a check, not decoration | ✅ **10 tests go red**, 2 of them interop (2026-10-18) |
| negative test: `NormaliseValue` returns the raw value | the value types are a check | ✅ **6 tests go red** (2026-10-18) |
| negative test: drop the cache | the lifetime claim is a check | ✅ **4 tests go red** (2026-10-18) |
| `tools\Test-VLPatch.ps1` | the `.vl` documents are well formed | ✅ 7 documents |
| negative test: dangling-Pad rule | that rule can still go red after being loosened | ✅ red on a Comment-only Pad, green on an annotation |
| `tools\Test-VLPackage.ps1` | the package can structurally contribute nodes | ✅ passes |
| `tools\Compile-HelpPatches.ps1` | **every node in every patch RESOLVED** — ours, and VL.NetTopologySuite's `Split` / `GetAttribute` | ✅ 6 patches, read out of the generated `*.vl.1.cs` |
| `tools\Test-Install.ps1` | the packed package installs **with VL.NetTopologySuite 0.0.3-alpha from nuget.org**, and all six help patches compile from the install | ✅ 2026-10-18, with that version evicted from every local cache first; needs `-NoHttpCache` (now in the script) for a just-published dependency |
| negative test: a patch naming the package by its old name | the compile check cannot go silently green after a rename | ✅ FAIL, exit 1 (2026-10-18) |
| the generated C# | `Read GeoJSON` is built in `Create`, never in `Update` | ✅ counted independently: 1 ctor, in `Create`, 0 in `Update` |

### The six help patches

`Explanation Overview of available nodes` is the front door. Then `HowTo Read GeoJSON`,
`HowTo Read a GeoJSON file`, `HowTo Access properties`, `HowTo Write GeoJSON`, and
`HowTo Handle a large GeoJSON file`.

Two of them exist because a node does *not* exist here: reading a file is composed from
`FileReader`, and opening a feature is VL.NetTopologySuite's `Split` and `GetAttribute` — so help is
where they get taught. **The
ratio should keep moving toward more help rather than more nodes** — VL.Skia ships 4 C# nodes and
98 help patches.

**Verified 2026-10-18, after the rename:**

- **The category, `NetTopologySuite.IO`** — by a probe vvvvc enforces: a scratch patch whose two
  nodes carry a hard `CategoryReference` to `NetTopologySuite > IO` compiles, and to the old
  `GeoJSON` fails with `Not found` (CLAUDE.md, Verification). Not looked at in the NodeBrowser.
- **The help patches compute the right values on screen** — four opened in vvvv 7.4 and read off
  PrintWindow captures; the round found three layout faults, fixed and photographed again.

**Not yet verified:**

- **F1** on `Read GeoJSON` / `Write GeoJSON`, and `HowTo Read a GeoJSON file` on screen.
- The two bangs of `HowTo Handle a large GeoJSON file` pressed in the GUI. Opened and captured
  2026-10-18: 7,342 features arrive through the background chain, longest frame 0.02 s; the
  bangs need a human, posted clicks do not reach the editor.
- **Stages F, G, H of the brief's §13** — Mapsui feature conversion, layer construction, rendering.
  They need vvvv, and a console host would prove nothing about them.

---

## Next

In order, and each small.

| | |
|---|---|
| **Finish the GUI round** | Four of six patches done 2026-10-18 (category proved headlessly, values on screen). Left: the two file patches, and F1. Check the layouts too — a Pad's `Comment` renders as a label to the RIGHT of the box, so an IOBox occupies far more width than its `Bounds` says, and every label collision in the siblings came from ignoring that. Launch, read the values, close — never leave it running. Then run `tools\Normalize-HelpPatches.ps1`. |
| **The cross-package example patch** | `GeoJSON → Read GeoJSON → Split → FeatureLayer → Map`, living **outside** these repositories, because neither this package nor VL.Mapsui depends on the other. Precedent: `vvvv-gis\examples\Example Map with data on it.vl`. **This is the real test of the architectural hypothesis**; the interop tests here only transcribe VL.Mapsui's signature. |
| **`HowTo Handle mixed geometry`** | The one obvious gap left in the help: a document holding points, lines, polygons and a GeometryCollection at once, and how a patch sorts them out. `test\fixtures\D-mixed.geojson` is already the data for it. |
| **A real-world fixture** | Fixtures A–F are synthetic or hand-written. Something from Natural Earth or a city open-data portal, licence permitting, would exercise property shapes nobody invents. Document large ones as optional external downloads rather than committing them. |

---

## Later

| | |
|---|---|
| **`Read GeoJSON (Bytes)`** | Measured: `byte[]` parses in 101 ms where `string` takes 140 ms on 5.4 MB, because the string path transcodes UTF-8 to UTF-16 and back. Worth it once someone reads files big enough to care. |
| **Writing a bare geometry** | `Write GeoJSON (Geometry)` emitting a GeoJSON *Geometry* object rather than a FeatureCollection. One method; left out to keep the first surface small. |
| **`bbox` support** | A feature's `bbox` is read into `IFeature.BoundingBox`; the collection's is dropped along with the collection type, and `writeGeometryBBox` is an unsurfaced serializer option. Waiting for a use case, since NTS's `Envelope` has no representation in a patch (VL.NetTopologySuite returns four floats instead, and that is the precedent to copy). If it arrives, it is an output pin on `Read GeoJSON` — not a resurrected `FeatureCollection`. |
| **The `id` member proper** | It currently becomes an ordinary `"id"` property, matching the Newtonsoft reader. Round-tripping it back out *as* a GeoJSON `id` member would need `IUnique`, which the concrete `Feature` does not implement. Documented asymmetry, not a bug. |
| **Property type helpers** | Nested objects arrive as `AttributesTable` and arrays as `object[]`. Reachable raw. If anyone actually hits this, the node belongs beside VL.NetTopologySuite's `Split`, not here. |
| **Streaming** | Measured **not supported** upstream: `Deserialize<FeatureCollection>` materialises everything, and GeoJSON's `features` array is nested inside an object so `DeserializeAsyncEnumerable` cannot reach it. `GeoJSONReader (Reactive)` now walks the array with `Utf8JsonReader` one feature at a time — but still builds the whole Spread before publishing. Publishing in chunks, or a spatial filter while reading, waits for a use case; past a few hundred MB the right answer is a different format (FlatGeobuf has an NTS reader with a bbox filter), not a cleverer GeoJSON reader. |

---

## Never

Recorded as *never* so it stops being re-proposed. Reasoning in
[ARCHITECTURE.md](ARCHITECTURE.md#what-this-will-never-contain).

| | |
|---|---|
| **A Mapsui dependency** | If VL.Mapsui cannot consume what this emits, the adapter belongs on its side. |
| **Feature nodes of its own** | Constructing, splitting and reading a feature is VL.NetTopologySuite's (`Feature`, `Split`, `GetAttribute`). Two packages opening the same type two ways is what the 2026-10-18 rename ended. |
| **A `VLFeature` / `GeoFeature` / `VL.GIS.Core`** | `NetTopologySuite.Features.Feature` already exists and the whole family already shares it. |
| **CRS transformation** | ProjNet's job. No node here will imply that setting an SRID reprojects. |
| **Geometry algorithms** | `VL.NetTopologySuite`. This package must not grow an `Area` node because it happens to hold geometry. |
| **Rendering, styling, layers, maps** | `VL.Mapsui`. |
| **Other file formats** | Shapefile, GeoParquet, FlatGeobuf, MVT, PMTiles, GeoPackage. Each is its own ecosystem and its own package. Letting one in is how a focused package stops being one. |
| **A generic large-data engine** | GeoJSON is a monolithic JSON object. Distorting this package to serve millions of features means doing badly what another format does well. |
| **Mechanical coverage of the serializer** | Every option and overload as a node. That turns the node browser into an API dump. |
