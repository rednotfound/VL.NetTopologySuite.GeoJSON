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

`test\VL.NetTopologySuite.GeoJSON.Tests\LoadTests.cs`. Run with `$env:VLGEOJSON_LOAD=1; dotnet test -c Release`.
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

**D is 12% of the node's cost — on this synthetic fixture, with 3 properties per feature.** Geometry
construction (C) dominates here, and raw JSON tokenising (B) is under a third of the total — so a
slow read is not "JSON is slow", it is object construction.

**On real data the 12% does not hold** (2026-10-18, throwaway console probe, Release, median of 3):

| | cities, 7,342 × 31 properties | roads, 56,600 × 31 properties |
|---|---:|---:|
| `Deserialize` | 56 ms | 863 ms |
| normalisation (D) | **114 ms (67%)** | **860 ms (50%)** |

**That split misleads, and the reason is worth keeping.** Timed in isolation over a collection
that had already been promoted, every part of normalising the cities — enumerating, names,
values, building the tables — added up to about **25 ms**, not 114. The rest was the GC tracing
and promoting the objects the deserializer had *just* allocated, billed to the next code that
allocated. A stage split of a .NET pipeline measures who triggered the collection, not who
caused it.

What did help, through `Read GeoJSON` itself, one file per process, median of 10 after a warm-up,
SHA-256 fingerprint of every feature's geometry and attributes **identical before and after**:

| file | before | after | retained before | retained after |
|---|---:|---:|---:|---:|
| cities, 7,342 × 31 properties | 156 ms | **115 ms** (−26%) | 23.9 MB | **15.4 MB** (−36%) |
| roads, 56,600 × 31 properties | 1,689 ms | **1,303 ms** (−23%) | 196 MB | **132 MB** (−33%) |
| Delaware, 357,534 × 2 properties | 3,121 ms | 2,861 ms (−8%) | 307 MB | 271 MB (−12%) |

Two changes in `Defaults`: walk the JSON object once instead of reading the upstream table by name
(its indexer is a linear `TryGetProperty` per name, so per feature it was quadratic in the property
count), and **share one string per property name across the document** — `JsonProperty.Name`
allocates a fresh one per call, and the tables keep them: 1.75 million copies of 31 names for the
roads. Retained figures exclude the text itself.

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

### Real open data — and properties cost too (2026-10-18)

The synthetic fixture carries 3 properties per feature. Real files carry 26-41, and that shows:
`Read GeoJSON`, Release build, median of three after a warm-up, same machine:

| file | features | properties each | size | parse ms | frames at 60 fps |
|---|---:|---:|---:|---:|---:|
| USGS earthquakes, past week | 1,858 | 26 | 1.3 MB | 160 | 9.6 |
| Natural Earth lakes 1:10m (polygons) | 1,355 | 41 | 4.8 MB | 148 | 8.9 |
| **Natural Earth populated places 1:10m** | **7,342** | 31 | 4.7 MB | **220** | 13 |
| USGS earthquakes, past month | 10,120 | 26 | 6.8 MB | 275 | 16.5 |
| Natural Earth roads 1:10m | 56,600 | 31 | 48.1 MB | 1,982 | 119 |

10,000 synthetic features parse in 148-162 ms; 10,120 real ones in 275. **Cost follows features
AND their properties, not coordinates** — every property is normalised (`Defaults.NormaliseValue`)
into a fresh `AttributesTable`. The populated places file ships in
`help\...\Assets\` for `HowTo Handle a large GeoJSON file` (0.5 MB deflated in the nupkg); the
roads are the "try a bigger one" download named in that patch. All Natural Earth and USGS data is
public domain.

### The cached path

600 frames over an unchanged 10,000-feature document: **0.0 ms total, 1 parse.** Reference equality
short-circuits before anything else runs.

---

## Large files inside vvvv: blocking versus background (2026-10-18)

Measured **in vvvv 7.4**, not a console. One patch per row, read triggered at frame 1800, longest
frame measured *inside the patch* (Stopwatch minus its FrameDelay, Max-held, from frame 600 on) and
read off a capture. An idle patch's longest frame was 0.02 s. vvvv working set 0.6 GB idle.

| file | features | size | **blocking**: longest frame | **background**: longest frame | background: frames > 33 ms |
|---|---:|---:|---:|---:|---:|
| Natural Earth populated places (shipped) | 7,342 | 4.7 MB | **0.56 s** | 0.02 s | 0 |
| Natural Earth roads 1:10m | 56,600 | 48 MB | **2.49 s** | 0.03 s | 4 |
| Microsoft US building footprints, Delaware | 357,534 | 99 MB | **3.95 s** | 0.04 s | 3 |
| Microsoft US building footprints, Connecticut | 1,215,624 | 340 MB | **12.91 s** | 0.04 s | 5 |

- **Blocking** = `FileReader (String)` → `Read GeoJSON`, the chain the help taught until now.
  The whole of vvvv freezes for one frame of that length.
- **Background** = `FileReader (String Reactive)` → `ForEach [Reactive]` around `Read GeoJSON` →
  `HoldLatest` — The Gray Book's own recipe for JSON. **It works with the existing node, no code.**
  Connecticut arrived about 15 s after the trigger, at 60 fps throughout.
- **The shipped 7,342 cities freeze vvvv for 0.56 s**, not the 220 ms the console measured — cold
  JIT and the file read count inside the host.
- **Memory is the ceiling, not time.** Connecticut took vvvv from 0.6 to **3.8 GB**, blocking or
  background alike: about 10× the file. Part of it is certain — `ReadGeoJsonNode` keeps the last
  text to compare against, so a 340 MB file stays alive as a 680 MB UTF-16 string for as long as
  the node exists. How the rest splits between geometry and attribute tables is not measured.
- Delaware has 6× the roads' features but froze only 1.6× as long: it carries **2 properties per
  feature to the roads' 31.** Cost follows properties.

**The instrument, because the first one was blind.** An external probe timing `WM_NULL` round
trips to the editor window every 4 ms saw **no stall at all** during the 2.5 s blocking parse:
vvvv's main loop does not run on the editor's UI thread. The blocking row is the negative control
that caught it. Measure frame time inside the patch.

### Running a parse off the main thread — console probe, same day

| | |
|---|---|
| Thread safety of `Defaults` | 2–8 parallel parses, cold and warm, **byte-identical** to sequential (SHA-256 over geometry, SRID, attribute names, types and values). Every converter field is `readonly`; `JsonSerializerOptions` is documented thread-safe once frozen |
| Parallel throughput | none to speak of — 8 at once 836 ms against 178 for one |
| Bytes instead of a string | parse 4–5% faster; **peak memory 94 MB lower** on the roads (no 101 MB string) |
| `DeserializeAsync` cancellation | **only during the read phase.** The serializer buffers the whole document, then parses it synchronously; a token set after that is ignored |
| One feature at a time (`Utf8JsonReader` on `features`, `Deserialize<IFeature>` each) | same total time, identical result, **cancellable within 0.4 ms**, progress available, lowest peak (308 MB against 405 bytes / 499 string on the roads) |
| GC on the main thread, background parse of the roads | worst frame about 35 ms (workstation concurrent GC, which vvvv's runtimeconfig leaves on) |

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
- **Where the 3.8 GB goes** for a 1.2-million-feature file, beyond the retained text.
- **The NodeBrowser category** for these nodes. `vvvvc` resolved every one of them, which proves
  they exist and contribute; only the GUI proves *where* they appear.
