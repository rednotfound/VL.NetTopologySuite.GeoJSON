# Pitfalls

Everything that went wrong building this package, what it cost, and what now stops it recurring.

**Why this document exists.** Almost nothing on this list produced an error message. The code
compiled, the tests were green, the document parsed, `vvvvc` exited 0 — and the thing was still
wrong. That is the shape of nearly every defect in this ecosystem, so a list of *symptoms* is worth
more than a list of rules.

Related, and deliberately not duplicated here:

| | |
|---|---|
| [`RULES.md`](RULES.md) | the packaging and runtime rules carried from the sibling repositories |
| [`VL-PATCH-XML.md`](VL-PATCH-XML.md) | the `.vl` serialization format and its commit gate |
| [`MEASUREMENTS.md`](MEASUREMENTS.md) | every number, and what has *not* been measured |

---

## 1. The upstream library does not return the types its own ecosystem uses

`NetTopologySuite.IO.GeoJSON4STJ` 4.0.0. All measured, none of it documented upstream.

| what you get | what you expected |
|---|---|
| `NetTopologySuite.IO.Converters.StjFeature` — **`internal`**, implements `IFeature` only | `NetTopologySuite.Features.Feature` |
| `JsonElementAttributesTable` — **immutable**, throws `NotSupportedException` on write, not castable to `AttributesTable` | `AttributesTable` |
| every JSON number as **`System.Decimal`**, integers included | `long` / `double` |
| the GeoJSON `id` member on **`IUnique.Id`**, not in the properties | a property called `id` |
| `Attributes` **`null`** when the feature has no `properties` member | an empty table |
| `Deserialize<Feature>` (the concrete type) → **`InvalidCastException`** | it to work |

**What it cost.** The pipeline this package exists to prove — GeoJSON → VL.Mapsui — *does not
compile* with raw output, because `FeatureLayer` takes `IEnumerable<Feature>`, the concrete class.
That was discovered by probing the library, not by reading about it; a design that had gone to
implementation on the assumption would have failed at the last step.

**Now prevented by** `Defaults.Normalise`, and it is negative-tested. Measured 2026-08-16 against 69
tests: removing it turned **47 red**, every interop test among them. Re-measured 2026-10-18 against
the 60 left after the feature nodes moved to VL.NetTopologySuite — the code can no longer drop the
copy and still compile, so the nearest variant is putting the raw table into the `Feature`: **10
red**, 2 of them interop; making only `NormaliseValue` return the raw value turns **6** red. Costs
22 ms per 10,000 features, 12% of the parse.

### The silent one

`{"hello":"world"}` — valid JSON that is not GeoJSON — **parses successfully with zero features**.
Nothing upstream objects. The count is the only signal a patch gets, which is why `Read GeoJSON`
has a `Status` pin that says `0 features` rather than only a `Success` boolean.

A legacy `crs` member is likewise **accepted and silently ignored**.

### The one that would have made a node a trap

`IAttributesTable`'s indexer **throws `ArgumentException`** for a name that is not there. An early
draft used that as the reason to omit a `GetProperty` node entirely — and that was **mistaking an
implementation detail for a design conclusion**. vvvv's own `GetAttributeValue [XElement]` answers
the same problem with a `Success` pin. The node exists now; it calls `Exists` first and never
throws. (Since 2026-10-18 it lives in VL.NetTopologySuite as `GetAttribute`, because it answers a
question about any feature, not about GeoJSON.)

### Round-tripping is not exact by default

The writer **rewinds polygon rings** to RFC 7946's winding rule. A MultiPolygon read and written
back is then `EqualsTopologically` but **not** `EqualsExact` — the shape survives, the coordinate
order does not. Exposed as the `Exact Round Trip` pin, on by default.

---

## 2. Two C# traps that compile, run, and are wrong

### The conditional unifies its branches

```csharp
// WRONG. Type is decimal: long converts implicitly to decimal, so the conversion is undone.
cond ? Convert.ToInt64(d) : d

// Right.
cond ? (object)Convert.ToInt64(d) : d
```

Every whole number silently stayed a `System.Decimal` — a type VL has no nodes for. It compiled, it
ran, nothing warned. **Caught only because a test asserted the exact type**, not the value.

The same shape bit twice: `element.TryGetInt64(out var l) ? l : element.GetDouble()` unifies to
`double` and loses exactness past 2^53.

### A symmetric-looking range guard that throws

```csharp
// WRONG. decimal tops out near 7.9e28; the cast itself is what does not fit.
d >= (decimal)double.MinValue && d <= (decimal)double.MaxValue
```

`OverflowException` at runtime. The guard is unnecessary anyway — every `decimal` fits a `double`.

### An XML comment cannot contain `--`

Cost a round on the `.csproj`, from `dotnet list package --include-transitive` written inside a
comment. It has cost three rounds across the sibling repositories. `Test-VLPatch.ps1` now checks it,
because the raw XML parse error points at a byte offset rather than at the comment.

---

## 3. Hand-authoring a `.vl`

[`VL-PATCH-XML.md`](VL-PATCH-XML.md) is the reference. These are the ones this package paid for.

### An annotation IOBox needs more than a `Value`

An IOBox carrying explanatory prose needs **a String `<p:TypeAnnotation>`** *and*
**`<p:ValueBoxSettings>` with `stringtype = Comment`**. With neither, vvvv cannot type the pad: the
box renders **empty** and its text is reported as a *warning* in the Debug panel.

**Six help patches shipped that way.** Nothing static complained — the documents were perfectly well
formed. It took opening one to see it.

### `Bounds` is not a clip rectangle

Text longer than its box **overflows onto the canvas** and lands on whatever is underneath.

The first budget for this was **3–4× too strict** — calibrated from a single box at `fontsize 12`,
it would have red-flagged all twelve GUI-reviewed annotation pads in `vl-mapsui`. Their convention
is **`fontsize 11` for body prose, `12` only for a title block**, and capacity scales with the
inverse square of the font size. Now a fontsize-aware **warning**, not a failure: it is arithmetic
about a font the script cannot measure, and a gate that disagrees with shipped code gets switched
off.

### A Pad's `Comment` renders as a label to the RIGHT of the box

So an IOBox occupies far more width than its `Bounds` says. The sibling allows *~200px of clear
space to its right* and never implemented the arithmetic.

**One collision was visible in a screenshot. Encoding the rule found ten**, across four patches.
That is the difference between checking by eye and checking arithmetically.

### A renamed output pin is the quietest failure available

`[return: Pin(Name = "Features")]` on a `[ProcessNode]`'s `Update` **is** honoured. But if the name
in the patch and the name on the node disagree, **the node still resolves** — only the *link* to
that pin is dropped, so every other check stays green and the patch computes nothing.

Verify it in the generated C#: the variable is named after the pin (`var Features_5 = …`, not
`Result_5`). `Compile-HelpPatches.ps1` asserts exactly this.

---

## 4. Verification that looked green and was not

### `NU1101` is preventable, not inherent

An early version of `Compile-HelpPatches.ps1` documented the restore failure as *"expected, because
it is the export stage after codegen"*. The generated C# really was already written, so its
assertions were real — but **the export half never ran at all**, so half of what a compile proves
was never being proved.

Two things must line up, and supplying only the first is the trap:

1. `--package-repositories` → the package **folder** (`dist\`, not `dist\feed\`)
2. a NuGet **source** carrying the nupkg (`dist\feed\`), via a `NuGet.config` at the output root

### …and that check is only meaningful on a cold cache

Once `%USERPROFILE%\.nuget\packages\<id>\<version>` exists, restore is satisfied from disk and never
consults a source — so the `NU1101` check **cannot fail**, whatever is configured. The first
negative test of it passed for that reason and proved nothing.

`pack.ps1` evicts that folder, so the honest sequence is `pack.ps1` → `Compile-HelpPatches.ps1`.
**Running it twice in a row proves less the second time.** The sibling believed for weeks that one
repository argument was enough, for exactly this reason.

### `vvvvc` exit code 0 says nothing about resolution

An unresolved node is built with no working pins, every link to it is dropped, `vvvvc` exits 0 and
nothing is red. **Read the generated `*.vl.1.cs`.**

Two traps in doing so, both of which reported all five patches as broken:

- **Anchor on `public … __Create__(`, not on `__Create__`.** The first occurrence in the file is a
  *call* — `return instance.__Create__(Node_Context);` — so matching the bare name searches the
  wrong region entirely.
- `[^)]*` will not cross the parameter list, because it contains `n3.Pin(…)`.

### `vvvvc` needs absolute paths

With a relative document path it dies inside `PathUtils.GetRelativePath` with
`UriFormatException: Invalid URI` **before generating anything** — a failure that looks like a
package problem and is not.

### A script that reports a passing run as failed gets ignored

`vvvvc`'s own non-zero exit code stays in `$LASTEXITCODE`, so falling off the end of a PowerShell
script propagates it. Both check scripts now `exit 0` explicitly.

---

## 5. Self-inflicted, and worth listing because they are all the same shape

Every one of these is *a check that could not go red*, or *a wrong number that reads as right*.

| what | how it showed |
|---|---|
| A loop variable named `$targets` **shadowed the outer list of documents** | the summary said "1 document" instead of 7. The loop still ran — `foreach` captures its collection up front — so **only the count lied** |
| A regex assumed attribute order: `<(Pad\|Node) Id="` | real Nodes are `<Node Bounds="…" Id="…">`, so the overlap check **never saw a single Node**. Found by the negative test, which planted exactly that case and stayed green |
| `\sBounds=` where the attribute string starts with `Bounds=` | no leading whitespace, no match, silent |
| A ring-orientation test written against a fixture **already wound the RFC way** | it could not fail. Rewritten with a deliberately clockwise ring |
| `Start-Process -ArgumentList` does not quote paths containing spaces | vvvv received three broken arguments, silently opened a fresh empty scratch document, and the screenshot looked like the package had failed |
| StrictMode throws on a missing hashtable key | `$reference.Split` where only one entry has it — use `ContainsKey` |

**The rule underneath all of them:** a check that has never failed on known-bad input is not a
check. Every check added here was negative-tested by planting the defect it claims to catch — and
three of the six above were found *by* that, not before it.

---

## 6. Working with vvvv itself

### Detect and refuse. Never kill.

`Stop-Process` and `taskkill` appear **zero times** in either sibling repository. Their pattern is
the inverse: `build.ps1` detects a running vvvv and *refuses*, telling the human to close it.

That guard was already in this repository and was bypassed — repeatedly — by killing vvvv instead.
What that cost, which neither sibling documents:

- **Force-killing corrupts vvvv's session state.** The next launch came back with an empty editor
  shell and an unrelated patch running in a Skia window. It looked exactly like a packaging failure
  and was not.
- **`Stop-Process -Name vvvv` is indiscriminate.** It ends every vvvv on the machine, including a
  session somebody else is using. If a process must be ended, end it **by PID**, and only one you
  started yourself.

### Opening a patch *is* running it

There is no idle state. Never leave vvvv running unattended, and never start it in the background.
Opening a help patch also rewrites its `NugetDependency` version and can flip a Boolean toggle —
run `tools\Normalize-HelpPatches.ps1` after any GUI session, and `Test-VLPackage.ps1` fails if you
forget.

### Capture the window with `PrintWindow`, not a screen grab

`SetForegroundWindow` loses the race whenever another application takes focus, and the result is a
screenshot of whatever was in front.

---

## 7. What is still unverified

Stated because an unmeasured claim quietly becomes an assumed one.

- **The NodeBrowser category.** `LastCategoryFullName` in a `.vl` is a hint — a patch compiles fine
  with it set to nonsense. A green compile proves a node *exists* and says nothing about *where it
  appears*.
- **Mapsui feature conversion, layer construction and rendering** (stages F–H of the original
  brief's §13). They need vvvv running; a console host would prove nothing about them.
- **The cross-package integration for real.** The interop tests here *transcribe* VL.Mapsui's
  signature. If that signature changes they stay green while the real integration breaks.
