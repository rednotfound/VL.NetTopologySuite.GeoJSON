# Measurements

Everything here was measured on this machine rather than read in a document. Claims without a
measurement behind them do not belong in this file or anywhere else in the repository.

All figures 2026-08-15 unless stated. Versions: **NetTopologySuite 2.6.0**,
**NetTopologySuite.Features 2.1.0**, **NetTopologySuite.IO.GeoJSON4STJ 4.0.0**,
**NetTopologySuite.IO.GeoJSON 4.0.0**, .NET 8, Windows 11.

---

## Which serializer, and why

Both GeoJSON packages are at **4.0.0**, and — this is the load-bearing fact — **both resolve
`NetTopologySuite.Features 2.1.0`, the exact version VL.Mapsui already pins.** There is no version
conflict anywhere in this ecosystem.

| | **GeoJSON4STJ 4.0.0** (chosen) | GeoJSON 4.0.0 (Newtonsoft) |
|---|---|---|
| Extra dependency | none — STJ is in-box on net8.0 | **Newtonsoft.Json 13.0.1** |
| Feature type returned | `StjFeature` — **internal**, `IFeature` only, **not** a `Feature` | **concrete `NetTopologySuite.Features.Feature`** |
| Attributes table | `JsonElementAttributesTable`, **immutable** (`NotSupportedException` on set), not castable to `AttributesTable` | `AttributesTable`, mutable |
| JSON numbers | **all `System.Decimal`** | `Int64` / `Double` |
| Nested object / array | `JsonElementAttributesTable` / `object[]` | `AttributesTable` / `List<object>` |
| GeoJSON `id` member | → `IUnique.Id`, **not** in properties | → into properties as `"id"` |
| Ring orientation control | `RingOrientationOption` | none |
| Parse 10,000 polygons | 122 ms | 112 ms |
| Parse 50,000 polygons | 629 ms | 650 ms |
| **Live memory, 10,000 features** | **13.6 MB** | 25.4 MB |
| **Live memory, 50,000 features** | **49.0 MB** | 100.8 MB |

Parse speed is a tie. **Memory is not: Newtonsoft retains roughly twice as much**, and that is
memory a patch holds for as long as the data is on screen. Combined with dropping a Newtonsoft
dependency and gaining ring-orientation control, GeoJSON4STJ wins on every axis that is not
"returns nicer types out of the box".

The nicer types are what `Defaults.Normalise` buys back, for 12% of the parse cost.

---

## Behaviour, measured

| | |
|---|---|
| Default SRID from `new GeoJsonConverterFactory()` | **4326** — the library stamps it unaided |
| `new GeoJsonConverterFactory(factory)` | adopts the factory's SRID exactly; 0 gives 0 |
| Legacy `crs` member | **accepted and silently ignored.** SRID still comes from the factory |
| `bbox` on a Feature | read into `IFeature.BoundingBox` |
| Ring orientation on **read** | **not** reoriented — a clockwise shell reads back clockwise |
| Ring orientation on **write** | **rewound** by default (`EnforceRfc9746`, upstream's spelling) |
| Round trip with the default | `EqualsTopologically` true, **`EqualsExact` false** for a MultiPolygon with a hole |
| Round trip with `DoNotModify` | `EqualsExact` true |
| Z / third ordinate | survives both ways — `[1,2,300]` ↔ `POINT Z(1 2 300)` |
| `IFeature.Attributes` when `properties` is absent | **`null`** |
| `IAttributesTable` indexer, missing name | **throws `ArgumentException`** |
| `Deserialize<Feature>` (concrete) | **`InvalidCastException`** — only `IFeature` works |
| `allowModifyingAttributesTables: true` | gives `JsonObjectAttributesTable` — mutable, but still **not** an `AttributesTable` |
| Streaming | **not supported.** `Deserialize<FeatureCollection>` materialises everything |

### Malformed input

| input | result |
|---|---|
| `{ not json` | `JsonException` |
| `{"hello":"world"}` | **succeeds, 0 features** — nothing objects |
| unknown geometry type `Rhombus` | `ArgumentException: Requested value 'Rhombus' was not found` |
| coordinates as strings | `JsonException` |
| unclosed polygon ring | `ArgumentException: points must form a closed linestring` |
| empty string | `JsonException` |
| a bare geometry where a FeatureCollection is expected | `JsonException: must be FeatureCollection` |
| `null` literal | returns **`null`** |

**The second row is the trap.** Valid JSON that is not GeoJSON produces an empty collection and no
error, so the only signal a patch gets is the count. `Read GeoJSON` reports `0 features` on
`Status` for exactly this reason.

---

## Load, through the actual node

`test\VL.GeoJSON.Tests\LoadTests.cs`. Run with `$env:VLGEOJSON_LOAD=1; dotnet test -c Release`.
Synthetic polygons, 20 vertices each, three properties.

### Scaling by feature count

| features | MB text | parse ms | live MB | coordinates | × a 16.7 ms frame |
|---:|---:|---:|---:|---:|---:|
| 10 | 0.0 | 0.1 | 0.0 | 210 | 0.0 |
| 100 | 0.1 | 1.2 | 0.1 | 2,100 | 0.1 |
| 1,000 | 0.6 | 12.9 | 1.4 | 21,000 | **0.8** |
| 10,000 | 5.5 | 162.3 | 13.7 | 210,000 | **9.7** |
| 50,000 | 27.2 | 748.4 | 62.7 | 1,050,000 | **44.8** |

Cost is close to linear in feature count, and memory runs about **2.5× the text size**.

**Around 1,000 features a single parse costs a whole frame.** That is the number that makes
`Read GeoJSON` a process node rather than an operation — not a preference.

### Stages separated (§13 of the brief), 10,000 polygons

| stage | | ms |
|---|---|---:|
| A | `File.ReadAllText` | 9.8 |
| B | `Utf8JsonReader` token scan only | 54.0 |
| C | GeoJSON → NTS geometry | 103.9 |
| D | normalisation to concrete `Feature` | 22.3 |
| | **`Read GeoJSON` node, total** | **180.2** |

**D is 12% of the node's cost.** Geometry construction (C) dominates, and raw JSON tokenising (B)
is under a third of the total — so a slow read is not "JSON is slow", it is object construction.

Reading path also matters: `byte[]` 101 ms < `string` 140 ms < `FileStream` 168 ms.

### Feature count costs more than geometry complexity

Four documents, all ≈ 200,000 coordinates:

| features | vertices each | coordinates | parse ms |
|---:|---:|---:|---:|
| 10,000 | 20 | 210,000 | **148.4** |
| 2,000 | 100 | 202,000 | 89.8 |
| 1,000 | 200 | 201,000 | 83.9 |
| 200 | 1,000 | 200,200 | **83.0** |

**Per-feature overhead dominates.** 10,000 simple polygons cost nearly twice as much as 200
complex ones holding the same number of coordinates — the attributes table and the feature object,
not the coordinates. Worth knowing before optimising the wrong thing: simplifying geometry will not
help a document that is slow because it has a lot of *features*.

### The cached path

600 frames over an unchanged 10,000-feature document: **0.0 ms total, 1 parse.** Reference equality
short-circuits before anything else runs.

---

---

## Annotation boxes in a help patch — and a constant that was wrong

`Bounds` is **not** a clip rectangle. Text longer than its box overflows onto the canvas and lands
on the nodes and labels underneath, with nothing to say so: the document is well formed, `vvvvc`
compiles it, and only opening it shows the damage.

**The first measurement here was made against a single box and was 3–4× too strict.** At
`fontsize 12` a 420px-wide box wraps at roughly 48 characters with about 25px between lines — about
650 characters in 420×340, or 0.0046 chars/px². Setting the budget there would have red-flagged
**all twelve** annotation pads in `vl-mapsui`, every one of which has been through a GUI review.

The sample that corrected it — `vl-mapsui`'s four Explanation blocks and eight HowTo notes:

| | box | fontsize | chars | chars/px² |
|---|---|---|---|---|
| Explanation, title | 700×150 | **12** | 708 | 0.0067 |
| Explanation, two columns | 340×230 | 11 | 772 / 793 | 0.0099 / 0.0101 |
| Explanation, footer | 700×120 | 11 | 1618 | 0.0193 |
| HowTo notes (8) | ~620×160–200 | 11 | 1361–5114 | 0.0142–0.0413 |

Two things fall out of it, and both are now the convention here:

- **`fontsize 11` for body prose, `12` only for a title block.** Capacity scales with the inverse
  square of the font size, so 11 and 12 are not the same budget. This package had been writing
  everything at 12.
- The comfortable band is roughly **0.010–0.017 at fontsize 11**. The pads above 0.025 are the
  later, sloppier ones — and are exactly the three that also lost their `<p:ValueBoxSettings>`.

`tools\Test-VLPatch.ps1` now budgets **0.0105 at fontsize 11 and 0.0070 at 12**, and **warns rather
than fails**: it is arithmetic about a font the script cannot measure, and a gate that disagrees
with shipped sibling code gets switched off. The two *structural* annotation checks — a String
`TypeAnnotation`, and `stringtype = Comment` — stay hard failures, because those are byte-checkable
and were real bugs.

---

## What has **not** been measured

Stated plainly, because an unmeasured stage quietly becomes an assumed one.

- **Mapsui feature conversion, layer construction, and rendering.** They need vvvv running, and a
  console host would prove nothing about them — that is the false proof this ecosystem has already
  made once. Stages F, G and H of the brief's §13 are open.
- **VL spread/dataflow overhead** (stage E) at scale, for the same reason.
- **The NodeBrowser category** for these nodes. `vvvvc` resolved every one of them, which proves
  they exist and contribute; only the GUI proves *where* they appear.
