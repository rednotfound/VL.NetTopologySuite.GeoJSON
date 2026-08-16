# Why VL.GeoJSON is shaped this way

[docs/RULES.md](RULES.md) covers mechanics — what silently breaks when packaging, and what breaks
outside the package at runtime. [docs/MEASUREMENTS.md](MEASUREMENTS.md) holds every number quoted
below. This document is the reasoning: what the package is for, what it will never contain, and how
to decide whether a new node belongs.

---

## One job

**How does a VL patch read and write GeoJSON?**

That is the whole question. Not how to draw it, not how to reproject it, not how to run spatial
operations on it, not how to read a shapefile.

```text
VL.GeoJSON  =  GeoJSON format semantics
             + serialization / deserialization
             + VL-friendly access to GeoJSON data
```

Almost all the value is upstream. NetTopologySuite and its GeoJSON serializer are mature; what this
repository solves is *how they show up as nodes in a patch*, and — the part that turned out to
matter most — **what type crosses the package boundary**.

---

## Native types, and the one thing we add

**No `VLFeature`, no `GeoFeature`, no `VLGeometry`, no `VL.GIS.Core`.** Nodes hand back
`NetTopologySuite.Features.Feature` and
`NetTopologySuite.Geometries.Geometry` directly.

```text
                        NetTopologySuite
                     /         |         \
                    /          |          \
          VL.GeoJSON    VL.NetTopologySuite    (VL.Mapsui, as a consumer)
                    \          |          /
                     \         |         /
                      the patch composes them
```

Nothing references anything else. **VL.GeoJSON does not depend on VL.Mapsui or on
VL.NetTopologySuite, and neither of them has ever heard of it.**

### The exception: normalisation, and why it is not optional

There is exactly one behaviour here that is not raw upstream output, and it is the reason these are
hand-written nodes rather than raw .NET reflection nodes over `JsonSerializer`.

**`NetTopologySuite.IO.GeoJSON4STJ` does not return the mature public NTS types.** Measured against
4.0.0:

| | |
|---|---|
| a decoded feature | `NetTopologySuite.IO.Converters.StjFeature` — **`internal`**, implements `IFeature`, **is not a `Feature`** |
| its attributes | `JsonElementAttributesTable` — **immutable**, throws `NotSupportedException` on write, not castable to `AttributesTable` |
| every JSON number | `System.Decimal`, integers included |
| a nested object / array | another `JsonElementAttributesTable` / `object[]` |
| the GeoJSON `id` member | on `IUnique.Id`, **not** in the properties |
| `Attributes` with no `properties` member | **`null`** |

That matters because of where the values are going. **VL.Mapsui's `FeatureLayer` takes
`IEnumerable<NetTopologySuite.Features.Feature>` — the concrete class** (`FeatureLayerNodes.cs`).
A raw `FeatureCollection` from this serializer does not satisfy it, so **the pipeline this package
exists to prove would not compile.** `System.Decimal` is also a type VL has no nodes for, and an
immutable attributes table is hostile in a patch.

So `Defaults.Normalise` copies each feature into a concrete `Feature` with a mutable
`AttributesTable`, turns whole numbers into `long` and fractional ones into `double`, recurses into
nested objects, and carries the `id` across as an ordinary property.

**Cost: 22 ms per 10,000 features, 12% of the parse.** That is the right trade — the alternative is
a package whose output nothing downstream can accept.

### Why normalise rather than widen VL.Mapsui

Both would work. Normalising was chosen because **it is the reversible direction**: `Feature`
implements `IFeature`, so if VL.Mapsui later widens its pin to `IFeature` this normalisation stays
valid and merely becomes optional. Emitting `IFeature` today would require editing a sibling
repository before anything could render, and would push `System.Decimal` and an immutable
attributes table out into every patch.

It also keeps the adapter on the right side of the boundary. **This package knows about
NetTopologySuite; NetTopologySuite knows nothing about this package**, and neither sibling has to
change for the hand-off to work.

---

## SRID — 4326, and why that is not the mistake §8 warns about

`Read GeoJSON` stamps **SRID 4326** unless the `Factory` pin says otherwise. VL.NetTopologySuite
defaults to **0**. That difference is deliberate, and the two are not inconsistent — the formats
are genuinely different.

- **WKT carries no CRS semantics**, so a package reading it cannot know what its numbers mean.
  0 ("unset") is the only honest answer, and stamping 4326 would be a claim it cannot check.
- **RFC 7946 §4 mandates** that GeoJSON coordinates are WGS84 longitude and latitude. 4326 is
  therefore a *fact about the format*, not a guess about the data.
- **The upstream library already does it.** `new GeoJsonConverterFactory()` yields SRID 4326
  unaided (measured). Forcing it to 0 would mean silently patching the library to make a number
  match, which is the thing not to do.

**The hazard, stated because nothing else will state it.** NTS **never throws on mixed SRIDs** —
4326 intersected with 0 returns 4326 silently. A patch mixing `Read WKT` (0) with `Read GeoJSON`
(4326) gets a confident wrong answer that looks exactly like a right one. The `Factory` pin is how
a patch makes them agree, in whichever direction it wants; it is the same pin, with the same
meaning, as every VL.NetTopologySuite creation node.

**An SRID is a label, not a transformation.** No node here reprojects anything and none ever will —
that is `VL.ProjNet`'s job. A legacy `crs` member is accepted and ignored, which is what RFC 7946
implies and what the library does (measured).

On the way out, **GeoJSON has nowhere to put an SRID**, so a projected geometry is written as
though its numbers were degrees. `Write GeoJSON` does not warn, because it cannot know — the doc
comment says so instead.

---

## Node categories

```text
category = (.NET namespace minus the "VL" prefix) + type name
```

Namespace `VL.GeoJSON` gives **`GeoJSON`**, flat. `[SkipCategory]` on the static node classes keeps
them from becoming a category level of their own (`GeoJSON.FeatureNodes`), and `[ProcessNode(...)]`
names the one process node explicitly.

**One level, deliberately.** Four nodes do not earn sub-categories, and `docs/RULES.md` warns
against excessive nesting. If this grows past a dozen nodes, `GeoJSON.IO` is the split to make.

---

## Decisions, per node

### `Read GeoJSON` is a process node, not an operation

A `public static` method in VL is evaluated **sixty times a second from the moment the document
opens**. Parsing 10,000 features takes **162 ms** against a 16.7 ms frame — a static
`Read GeoJSON` would not be slow, it would be a patch that never draws again. Even **1,000
features cost 0.8 of a frame**, so this is not an exotic-scale concern.

So the text is parsed when it *changes*, and the same `Spread` instance is returned in
between. **Returning the same instance matters as much as skipping the parse**: object identity is
the only "has this changed" signal VL has, so everything downstream skips its work too.

`Parses` is an output pin for the same reason VL.Mapsui counts layers built: *"this only runs
once"* is not a claim anyone should take on trust, and a **rate** is invisible unless something
counts it.

The cache compares by reference first (a string in a pad or from `FileReader` is the same instance
every frame, so this is free) and falls back to value equality (comparing 27 MB costs a few
milliseconds against 748 ms to re-parse it).

### There is no `Read GeoJSON File`

**VL.CoreLib already ships `FileReader (String) [IO]`**, with an explicit `Read` trigger pin. So
`FileReader → Read GeoJSON` is the whole story, and it composes with anything else that produces
text — a web response, a string built in the patch, a text IOBox.

Adding a file node would duplicate a shipped one, hide where the IO happens, and take on encoding
and path semantics this package has no business owning. **A path pin is also the single most
expensive mistake in this ecosystem** — an empty Path IOBox is not empty, and it cost 444 stray
files in a sibling. Not having one is the best way not to make it.

The measured cost of composing rather than specialising: reading via `string` is 140 ms against
101 ms for `byte[]` on a 5.4 MB document. Worth a `Read GeoJSON (Bytes)` overload one day; not
worth a file node.

### There is no `FeatureCollection`, and therefore only one `Split`

`Read GeoJSON` returns a `Spread<Feature>`. The GeoJSON *document* type never reaches a patch.

**This follows vvvv's own document readers, and an earlier version of this package got it wrong.**
Measured against `VL.CoreLib\VL.Xml.vl`: `XMLReader`, `Parse [XML]`, `JSONReader` and `ParseJSON`
all end with `XDocument (Split) → Root` internally and hand back an `XElement`; the
`XDocument`-typed variants are quarantined in an `Advanced` sub-canvas. A patch never holds a
"document".

`FeatureCollection` was our `XDocument`. It crossed no package boundary — zero occurrences in
either sibling repository — and existed only to be taken apart again. So it went, and a second node
went with it: there used to be a `Split (FeatureCollection) → Result, Count`, which broke the
shipped convention three ways at once. Measured across **815 shipped `.vl` files, 1,119 `Split`
calls, 247 categories**:

| what it did | what the corpus says |
|---|---|
| two `Split` nodes in one category, told apart only by input type | **0 precedents.** vvvv always suffixes the name — `Split (String)`, `Split (Edges)`, `Split (Componentwise)` |
| `Split` used to unwrap a collection into a `Spread` | never, except `String`/`Regex`, which *are* sequences. For real collections the vocabulary is `FromSequence` and `ToSpread` |
| output pins `Result` and `Count` | every other `Split` names the type's **own members**. `Count` is a derived quantity, not a member |

The survivor, `Split (Feature) → Geometry, Properties`, is textbook: it is field projection on a
record, and it is the same shape as VL.Mapsui's own `Split`.

**`Count` is not a pin either.** `Count [Collections.Spread]` is a shipped node, used 163 times, and
it works on our output directly — confirmed in generated code:
`Count<Feature>(Input_In: Features_5, …)`.

What this costs: the collection's `bbox` member, which was never surfaced anyway. When something
needs it, it becomes an output pin on `Read GeoJSON`, not a resurrected type.

### `Read GeoJSON` returns a `Spread`, not a bare sequence

`Spread<T>` is what VL actually patches with — `GetSlice`, `Count` and `ForEach` all expect one —
and it costs nothing at the boundary, since `Spread<T>` is an `IReadOnlyList<T>` and satisfies
VL.Mapsui's `IEnumerable<Feature>` pin unchanged.

### `GetProperty` exists, and carries a `Success` pin

An earlier version of this document argued that a property getter would be a *trap*, because
`IAttributesTable`'s indexer throws `ArgumentException` on a missing name (measured) and a node that
throws sixty times a second while somebody types is not a diagnostic.

**That was an implementation detail mistaken for a design conclusion.** vvvv's own answer is a
`Success` pin, not the absence of a node: `GetAttributeValue [XElement]` takes `Input` and `Name`
and returns `Value` and `Success`, as do `GetValue`, `GetValues`, `GetAttributeValues`,
`XPathGetValue` and `XPathGetAttributeValue`. The house rule is legible — **a `Get…` node carries a
`Success` pin and never throws**, while a non-`Get` accessor hands back a raw, possibly-null object
and carries none.

It sits **beside** `Split`'s `Properties` output rather than replacing it, because the two answer
different questions and vvvv ships exactly the same pair: `XElement (Split)` hands over
`Attributes` as a spread, and `GetAttributeValue` takes one by name.

### `Write GeoJSON` has an `Exact Round Trip` pin

RFC 7946 wants counter-clockwise exterior rings and clockwise holes, and the library rewinds rings
on the way out to match. Measured: a MultiPolygon read and written back is then
`EqualsTopologically` but **not** `EqualsExact`.

That is the kind of difference that makes a test fail for a reason nobody can see, so the default
is to write the geometry the patch actually holds. Switch it off for a consumer that insists on the
RFC winding. Neither is right for everyone, which is why it is a visible pin rather than a decision
taken here.

### Failure is a pin, never an exception

Half-typed text is the **normal** state of an IOBox someone is editing, and a node that throws
sixty times a second is not a diagnostic. `Success` goes false, `Status` carries the reason, the
result is empty.

`Status` leads with the exception type on purpose: `JsonException` means the bytes are not JSON,
`ArgumentException` means they are valid JSON describing something that is not valid geometry. Two
different problems with two different fixes, and the message alone does not always say which.

**A failing parse is not retried every frame either** — the cache keys on the text, not on success.

---

## What stays raw

Reachable through VL's raw .NET nodes, deliberately not wrapped:

- **The serializer's own configuration**: `GeoJsonConverterFactory`, `JsonSerializerOptions`,
  `idPropertyName`, `writeGeometryBBox`.
- **`IAttributesTable` and `AttributesTable` themselves.** `Split` converts; the types stay open.
- **Nested objects and arrays.** They arrive as `AttributesTable` and `object[]`. Real, reachable,
  and not given nodes until something needs them.
- **`bbox`**, on the collection and per feature. The collection's is dropped with the collection
  type; a feature's survives on `IFeature.BoundingBox` and is reachable raw.
- **Everything in NetTopologySuite.** That is `VL.NetTopologySuite`'s job, not this one's.

---

## The package boundary

| If it answers | It belongs in |
|---|---|
| How do I read or write GeoJSON? | **here** |
| How do I create or manipulate geometry? | `VL.NetTopologySuite` |
| How do I display this on a map? | `VL.Mapsui` |
| How do I transform between coordinate reference systems? | `VL.ProjNet` |
| How do I read Shapefile / GeoParquet / FlatGeobuf? | a focused package, one each |
| Nothing yet — we imagine needing it | **nowhere. Do not build it.** |

**GeoJSON is not the final large-data format.** It is common, readable, interoperable and excellent
for testing, which is why it is first. It is a monolithic JSON object, which makes it the wrong
answer for millions of features — and that is a job for GeoParquet, FlatGeobuf, MVT or PMTiles, in
their own packages. **This package should be a good GeoJSON package, not a universal large-data
engine.**

---

## What this will never contain

- **A universal `Feature` model.** `NetTopologySuite.Features.Feature` already exists and
  VL.Mapsui already uses it. Inventing `VLFeature` would make this package the definition of the
  whole domain.
- **Generalised GIS abstractions** — `VL.GIS.Core`, `IGeoObject`, `IGISGeometry`, `GISContext`.
  No current problem requires them, and the whole point of this repository is to test whether they
  are needed at all. **So far: they are not.**
- **CRS transformation.** Nothing here will imply that setting an SRID reprojects.
- **Geometry algorithms, rendering, styling, layers, maps, tiles.**
- **A Mapsui dependency.** If VL.Mapsui ever cannot consume what this package emits, the adapter
  belongs on *its* side, not here.
- **Other file formats.** Letting one more in is how a focused package stops being one.
