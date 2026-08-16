# Roadmap

What is here, what is next, what is deliberately absent, and what will never arrive.

The rule throughout: **an item moves from "later" to "next" when something actually needs it**, not
because the serializer has the option.

---

## Now — 0.0.1-alpha, unpublished

**Four nodes in one flat `GeoJSON` category. 69 tests, 4 load tests, six help patches.**

```text
Read GeoJSON        [ProcessNode]  text + Factory -> Features (Spread), Success, Status, Parses
Split               (Feature) -> Geometry, Properties
GetProperty         Feature + Name -> Result, Success
Write GeoJSON       features + Exact Round Trip + Indent -> text, Success
```

The MVP path is implemented and covered:

```text
GeoJSON text -> Read GeoJSON -> Spread<Feature> -> GetSlice -> Split -> NTS Geometry   ✅
                                                            -> GetProperty -> Value    ✅
NTS Feature -> Write GeoJSON -> text -> Read GeoJSON -> the same geometry               ✅
```

**No `FeatureCollection` type is exposed, and there is no `Count` pin.** Both are deliberate and
both are backed by a survey of the shipped packs — see
[ARCHITECTURE.md](ARCHITECTURE.md#there-is-no-featurecollection-and-therefore-only-one-split).

### Verified

| | proves | state |
|---|---|---|
| `dotnet test` | the arithmetic and the semantics are right | ✅ 69 tests, ~100 ms, no network |
| negative test: drop `Defaults.Normalise` | the interop claim is a check, not decoration | ✅ **47 tests go red**, including every interop test |
| negative test: drop the cache | the lifetime claim is a check | ✅ **4 tests go red** |
| `tools\Test-VLPatch.ps1` | the `.vl` documents are well formed | ✅ 7 documents |
| negative test: dangling-Pad rule | that rule can still go red after being loosened | ✅ red on a Comment-only Pad, green on an annotation |
| `tools\Test-VLPackage.ps1` | the package can structurally contribute nodes | ✅ passes |
| `tools\Compile-HelpPatches.ps1` | **every node in every patch RESOLVED** | ✅ 6 patches, read out of the generated `*.vl.1.cs` |
| the generated C# | `Read GeoJSON` is built in `Create`, never in `Update` | ✅ counted independently: 1 ctor, in `Create`, 0 in `Update` |

### The six help patches

`Explanation Overview of available nodes` is the front door. Then `HowTo Read GeoJSON`,
`HowTo Read a GeoJSON file`, `HowTo Access properties`, `HowTo Write GeoJSON`, and
`HowTo Handle a large GeoJSON file`.

Two of them exist because a node does *not*: reading a file and getting one property are things a
user needs and this package deliberately does not wrap, so help is where they get taught. **The
ratio should keep moving toward more help rather than more nodes** — VL.Skia ships 4 C# nodes and
98 help patches.

**Not yet verified, and the distinction matters:**

- **The NodeBrowser category.** `LastCategoryFullName` in a `.vl` is a hint — a patch compiles fine
  with it set to nonsense. A green `vvvvc` proves a node *exists*; only the NodeBrowser proves
  *where it appears*.
- **That the patch computes the right value on screen.** Only running it proves that.
- **Stages F, G, H of the brief's §13** — Mapsui feature conversion, layer construction, rendering.
  They need vvvv, and a console host would prove nothing about them.

---

## Next

In order, and each small.

| | |
|---|---|
| **Open it in the vvvv GUI** | The unverified claims above, in one session across all six patches. Check the layouts too — a Pad's `Comment` renders as a label to the RIGHT of the box, so an IOBox occupies far more width than its `Bounds` says, and every label collision in the siblings came from ignoring that. Launch, read the values, close — never leave it running. Then run `tools\Normalize-HelpPatches.ps1`. |
| **The cross-package example patch** | `GeoJSON → Read GeoJSON → Split → FeatureLayer → Map`, living **outside** all three repositories, because a patch needing three packages cannot ship inside one whose dependencies do not guarantee the others. Precedent: `vvvv-gis\examples\Example Map with data on it.vl`. **This is the real test of the architectural hypothesis**; the interop tests here only transcribe VL.Mapsui's signature. |
| **`HowTo Handle mixed geometry`** | The one obvious gap left in the help: a document holding points, lines, polygons and a GeometryCollection at once, and how a patch sorts them out. `test\fixtures\D-mixed.geojson` is already the data for it. |
| **A real-world fixture** | Fixtures A–F are synthetic or hand-written. Something from Natural Earth or a city open-data portal, licence permitting, would exercise property shapes nobody invents. Document large ones as optional external downloads rather than committing them. |

---

## Later

| | |
|---|---|
| **`Read GeoJSON (Bytes)`** | Measured: `byte[]` parses in 101 ms where `string` takes 140 ms on 5.4 MB, because the string path transcodes UTF-8 to UTF-16 and back. Worth it once someone reads files big enough to care. |
| **`GeoJSONReader`, a file entry point** | Measured: vvvv ships **both** a file reader and a string parser for the same format — `XMLReader` (pins `File Path`, `Encoding`, `Read`) beside `Parse [XML]`, and `JSONReader` beside `ParseJSON`. This package has only the string half, and the argument used to omit the file half ("`FileReader (String)` composes") is weaker than that precedent. Held back because it introduces a `Path` pin, which is this ecosystem's most expensive documented mistake. `HowTo Read a GeoJSON file` teaches the composition in the meantime. |
| **A `Feature` constructor** | Measured: of 302 shipped groups defining a `Split`, **294 (97%) also define `Create`** — the real convention is `Create`/`Split`, not the `Join`/`Split` pair `docs/RULES.md` describes (only 9% define `Join`). This package has `Split` and no constructor, so `Write GeoJSON` can only write features that came from `Read GeoJSON`. VL.Mapsui's `Feature` node fills the gap for anyone who has both, which is why this is not urgent. |
| **Writing a bare geometry** | `Write GeoJSON (Geometry)` emitting a GeoJSON *Geometry* object rather than a FeatureCollection. One method; left out to keep the first surface small. |
| **`bbox` support** | A feature's `bbox` is read into `IFeature.BoundingBox`; the collection's is dropped along with the collection type, and `writeGeometryBBox` is an unsurfaced serializer option. Waiting for a use case, since NTS's `Envelope` has no representation in a patch (VL.NetTopologySuite returns four floats instead, and that is the precedent to copy). If it arrives, it is an output pin on `Read GeoJSON` — not a resurrected `FeatureCollection`. |
| **The `id` member proper** | It currently becomes an ordinary `"id"` property, matching the Newtonsoft reader. Round-tripping it back out *as* a GeoJSON `id` member would need `IUnique`, which the concrete `Feature` does not implement. Documented asymmetry, not a bug. |
| **Property type helpers** | Nested objects arrive as `AttributesTable` and arrays as `object[]`. Reachable raw. A `Split (AttributesTable)` node is the obvious addition if anyone actually hits this. |
| **Streaming** | Measured **not supported** upstream: `Deserialize<FeatureCollection>` materialises everything, and GeoJSON's `features` array is nested inside an object so `DeserializeAsyncEnumerable` cannot reach it. A custom `Utf8JsonReader` walk is possible. **Do not build it until measurement demands it** — and note that at that point the right answer is probably a different format, not a cleverer GeoJSON reader. |
| **Async / non-blocking read** | 748 ms for 50,000 features blocks the vvvv thread. `[ProcessNode]` returning progress, or an `IObservable`, is the shape. **Never `.Result` or `.Wait()`** — vvvv's runtime thread owns a `SynchronizationContext` and sync-over-async deadlocks it. |

---

## Never

Recorded as *never* so it stops being re-proposed. Reasoning in
[ARCHITECTURE.md](ARCHITECTURE.md#what-this-will-never-contain).

| | |
|---|---|
| **A Mapsui dependency** | If VL.Mapsui cannot consume what this emits, the adapter belongs on its side. |
| **A `VLFeature` / `GeoFeature` / `VL.GIS.Core`** | `NetTopologySuite.Features.Feature` already exists and all three packages already share it. |
| **CRS transformation** | ProjNet's job. No node here will imply that setting an SRID reprojects. |
| **Geometry algorithms** | `VL.NetTopologySuite`. This package must not grow an `Area` node because it happens to hold geometry. |
| **Rendering, styling, layers, maps** | `VL.Mapsui`. |
| **Other file formats** | Shapefile, GeoParquet, FlatGeobuf, MVT, PMTiles, GeoPackage. Each is its own ecosystem and its own package. Letting one in is how a focused package stops being one. |
| **A generic large-data engine** | GeoJSON is a monolithic JSON object. Distorting this package to serve millions of features means doing badly what another format does well. |
| **Mechanical coverage of the serializer** | Every option and overload as a node. That turns the node browser into an API dump. |
