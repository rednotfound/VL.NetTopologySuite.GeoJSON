# Contributing

## Read first

| | before |
|---|---|
| [`docs/RULES.md`](docs/RULES.md) | writing a **node** |
| [`docs/VL-PATCH-XML.md`](docs/VL-PATCH-XML.md) | hand-editing a **`.vl`** |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | proposing anything that changes the public surface |
| [`docs/PITFALLS.md`](docs/PITFALLS.md) | wondering why a check exists |

These are not formalities. **Almost nothing you can get wrong in this ecosystem produces an error
message** — the package installs, the patch opens, `vvvvc` exits 0, and the thing simply does not
work. Nine releases of an earlier package shipped contributing zero nodes, with no warning anywhere.

## Setup

- Windows, vvvv gamma **7.2 or newer** (7.4 is what everything here is verified against)
- .NET 8 SDK
- No other tooling: `tools\Find-Vvvv.ps1` locates vvvv, and packing uses the `NuGet.exe` that ships
  inside it

## The loop

```powershell
# vvvv must be CLOSED. build.ps1 refuses otherwise, and that refusal is the design -
# see docs/PITFALLS.md section 6. Never kill it; close it.
.\build.ps1
dotnet test test\VL.NetTopologySuite.GeoJSON.Tests\VL.NetTopologySuite.GeoJSON.Tests.csproj

.\tools\Test-VLPatch.ps1        # every .vl: BOM, IDs, link endpoints, annotation boxes, layout
.\tools\Test-VLPackage.ps1      # the package: entry point, [ImportAsIs], nuspec, help patches

.\pack.ps1                      # -> dist\feed\*.nupkg  (also evicts both stale caches)
.\tools\Compile-HelpPatches.ps1 # headless vvvvc, then READS the generated C#
.\tools\Test-Install.ps1        # installs the nupkg for real and compiles from the installed copy
```

Load measurements are off by default — they take about ten seconds and allocate hundreds of
megabytes:

```powershell
$env:VLGEOJSON_LOAD = '1'; dotnet test -c Release --filter LoadTests
```

## Be precise about which claim you have

This is the single most useful idea in these repositories. **Six different things can be true, and
exactly one check proves each.** Reporting a weaker one as a stronger one is how a false proof gets
written down.

| check | proves | does **not** prove |
|---|---|---|
| `dotnet test` | the semantics are right | anything about vvvv |
| `Test-VLPatch.ps1` | the `.vl` documents are well formed | that any node resolves |
| `Test-VLPackage.ps1` | the package *can* structurally contribute nodes | that it does |
| `Compile-HelpPatches.ps1` | every node **resolved** — read out of the generated C# | which **category** it lands in |
| `Test-Install.ps1` | the nuspec's dependencies really install | that the patches run |
| the vvvv **NodeBrowser** | which **category** a node is in | that it computes anything |
| the vvvv **GUI, running** | it computes the right value, and what it looks like | — |

Two traps inside that table, both paid for:

- **`vvvvc` exiting 0 proves nothing.** An unresolved node has its links dropped silently. Read the
  generated `*.vl.1.cs`.
- **`Compile-HelpPatches.ps1`'s `NU1101` check only works on a cold NuGet cache.** Run `pack.ps1`
  first — it evicts the cache. Running the compile twice in a row proves less the second time.

## Adding a node

Answer these first. If any answer is thin, it is a **help patch**, not a node.

- **Can a patch reach the same result by wiring existing nodes?** Then it is a help patch. There is
  no `Count` node here because the output is a `Spread` and `Count [Collections.Spread]` already
  works on it.
- **Does VL or VL.CoreLib already ship it?** There is no `Read GeoJSON File` because
  `FileReader (String) [IO]` exists and has a `Read` trigger pin.
- **Does it hold a resource, or is it expensive?** Then `[ProcessNode]`, never a static method. A
  `public static` method is a node evaluated **sixty times a second from the moment the document
  opens**.
- **Is it about GeoJSON?** Geometry belongs in `VL.NetTopologySuite`, maps in `VL.Mapsui`,
  projection in a `VL.ProjNet`. See the boundary table in `ARCHITECTURE.md`.
- **Three inputs is the target.** Five means two decisions are wearing one node. Extra *outputs* are
  free.

And check what the ecosystem already calls it. `Split` means *give me this value's components*, and
its output pins are the type's own member names — measured across 247 categories and 1,119 call
sites in the packs shipped with vvvv. An earlier version of this package had a second `Split` that
broke that three ways at once.

## Editing a help patch

**The checked-in `.vl` is the source of truth.** The generator under `tools\legacy\` is retired and
throws if run; its templates have already diverged from the patches.

Both sibling repositories reached this rule the same way, one of them by losing a hand-arranged
layout to a regeneration. So the mitigation is not *avoid hand-editing*, it is **validate every
edit**:

1. anchor each insertion on a match asserted to occur **exactly once**, and write nothing if any
   anchor misses
2. `Test-VLPatch.ps1`, then `Test-VLPackage.ps1`
3. `pack.ps1` then `Compile-HelpPatches.ps1` — **every** patch, not the one you edited. Deleting a
   node once broke a file nobody had touched.
4. open it in vvvv, look at it, close it, then run `tools\Normalize-HelpPatches.ps1`

## Writing a check

- **A check that has never failed on known-bad input is not a check.** Plant the defect it claims to
  catch and watch it go red. Three of the bugs in `docs/PITFALLS.md` §5 were found *by* doing that,
  not before it.
- **Run the negative test after the fix, not only before it.** A test that goes red on broken code
  can still be measuring the wrong thing once the code is fixed.
- **When a check finds one instance, run it over everything.** Encoding the label-overlap rule found
  ten collisions where the eye had found one.
- **Warn rather than fail for anything you cannot measure exactly.** The text-density check is
  arithmetic about a font; a hard gate that disagrees with shipped sibling code gets switched off,
  and an ignored check is worse than none.

## Style

- Every node's doc comment names where its answer comes from — almost all of them say "Uses
  NetTopologySuite". When a node is our own arithmetic instead, that is information the user wants.
- Claims carry their measurement and its date. `docs/MEASUREMENTS.md` is where numbers live, and it
  has an explicit section for what has *not* been measured.
- Prefer copying a proven shape to composing one, especially in `.vl` XML.
