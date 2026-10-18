<#
.SYNOPSIS
    Compiles every help patch headlessly with vvvvc and checks that this package's nodes RESOLVED.

.DESCRIPTION
    This is the only automated check that says anything about whether a node actually works.
    Test-VLPatch.ps1 proves the XML is well formed; Test-VLPackage.ps1 proves the package could
    contribute nodes. Neither notices that a node failed to resolve.

    AND THE EXIT CODE OF vvvvc DOES NOT NOTICE EITHER. An unresolved node is built with no working
    pins, every link to it is dropped, and vvvvc exits 0 with nothing red. So this script does not
    trust the exit code: it READS the generated C# and asserts that each node named in the patch
    appears in it, and that a renamed output pin produced a variable of that name.

    TWO THINGS HAVE TO LINE UP, and this was carried from vl-mapsui rather than rediscovered:

      1. `--package-repositories` - how vvvv finds a *package folder*, i.e. dist\VL.NetTopologySuite.GeoJSON\.
         Point it at dist\, not dist\feed\, or vvvvc says "Missing package: VL.NetTopologySuite.GeoJSON".

      2. a NuGet source carrying the *nupkg* - because vvvvc then generates a .csproj with a
         PackageReference and runs a normal restore. Point that at dist\feed\, or restore says
         "NU1101: package VL.NetTopologySuite.GeoJSON not found" while listing nuget.org and friends.

    An earlier version of this script supplied only (1) and documented the resulting NU1101 as
    "expected, because it is the export stage after codegen". That was wrong in a way worth
    recording: the C# it reads was indeed already written, so the assertions below were real - but
    the export half never ran at all, so half of what a compile proves was never being proved.
    A NuGet.config dropped at the output root fixes it; restore finds it by walking up from the
    generated project.

    Requires .\pack.ps1 to have run: dist\feed\*.nupkg is what (2) reads.

    Also learned here, and not in the sibling: pass ABSOLUTE paths for both the document and the
    output directory. With a relative document path vvvvc dies inside PathUtils.GetRelativePath
    with "UriFormatException: Invalid URI" before generating anything.

    What it still cannot tell you: which CATEGORY a node lands in. LastCategoryFullName in a .vl
    is a hint - a patch compiles fine with it set to nonsense. Only the NodeBrowser proves that,
    and only running the patch proves it computes the right value.

.PARAMETER Patch
    Compile only patches whose name matches this wildcard. Default: all of them, and that is the
    gate - "compile every help patch, not the one you edited". Deleting a node once produced
    "Not found" in a file nobody had touched, in the sibling repository.

.EXAMPLE
    .\pack.ps1 ; .\tools\Compile-HelpPatches.ps1
.EXAMPLE
    .\tools\Compile-HelpPatches.ps1 -Patch "*properties*" -KeepOutput
#>
param(
    [string]$OutputDirectory = '',
    [string]$Patch = '*',
    [switch]$KeepOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path $PSScriptRoot -Parent
$Dist     = Join-Path $RepoRoot 'dist'
$Deps     = Join-Path $RepoRoot 'deps'
$Feed     = Join-Path $RepoRoot 'dist\feed'
$NtsRepo  = 'D:\2026_Projects\vl-nettopologysuite'

# Discovered the way build.ps1 and pack.ps1 discover it - a .vl at the root with a .nuspec of the
# same name - because this name anchors every node check below. It was hardcoded as "VL.GeoJSON"
# until the package was renamed (2026-10-18), and a stale anchor does not fail: it matches
# nothing, every patch reports "uses no node of ours", and the run is green.
$PackageName = @(Get-ChildItem $RepoRoot -Filter '*.vl' -File |
    Where-Object { Test-Path (Join-Path $RepoRoot "$($_.BaseName).nuspec") }).BaseName
if (@($PackageName).Count -ne 1) { throw "expected exactly one .vl with a matching .nuspec at $RepoRoot" }

if (-not (Test-Path $Dist)) { throw "dist\ not found. Run .\build.ps1 first." }
if (-not (Get-ChildItem $Feed -Filter '*.nupkg' -ErrorAction SilentlyContinue)) {
    throw "no .nupkg in dist\feed - run .\pack.ps1 first, or restore will fail with NU1101."
}

# Discovered, not hardcoded. The sibling hardcodes the 7.4 path in this script even though it ships
# Find-Vvvv.ps1 beside it; that is a bug of theirs, not a pattern to copy.
$vvvvc = & (Join-Path $PSScriptRoot 'Find-Vvvv.ps1') -Compiler
if (-not (Test-Path $vvvvc)) { throw "vvvvc.exe not found at '$vvvvc'" }

# Detect and refuse - never kill. A running vvvv holds the staged assemblies open, and killing it
# corrupts its session state: the next launch came back with an empty editor and an unrelated patch
# running in a Skia window. Same guard as build.ps1.
$running = @(Get-Process 'vvvv' -ErrorAction SilentlyContinue)
if ($running) { throw "vvvv is running (PID $($running.Id -join ', ')). Close it by hand, then run this again." }

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) "vlgeojson-compile-$PID"
}
# Absolute, or vvvvc dies in PathUtils.GetRelativePath before generating anything.
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null

# (2): restore walks up from the generated .csproj and finds this.
$sources = @("    <add key=""geojson-dist"" value=""$Feed"" />")
$ntsFeed = Join-Path $NtsRepo 'dist\feed'
if (Test-Path $ntsFeed) { $sources += "    <add key=""nts-dist"" value=""$ntsFeed"" />" }

@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
$($sources -join "`n")
  </packageSources>
</configuration>
"@ | Set-Content (Join-Path $OutputDirectory 'NuGet.config') -Encoding utf8

# (1): package *folders*, not the feed.
$repositories = @($Dist, $Deps)
$ntsDist = Join-Path $NtsRepo 'dist'
if (Test-Path $ntsDist) { $repositories += $ntsDist }
$repositories = (($repositories | Where-Object { Test-Path $_ }) -join ';')

$patches = @(Get-ChildItem (Join-Path $RepoRoot 'help') -Filter '*.vl' -Recurse -File |
             Where-Object { $_.BaseName -like $Patch } | Sort-Object Name)
if ($patches.Count -eq 0) { throw "No help patch matches -Patch '$Patch'" }

# The names a patch may reference from this package, and the C# each must produce.
$OurNodes = @{
    'Read GeoJSON'  = 'ReadGeoJsonNode'
    'Write GeoJSON' = 'WriteNodes.WriteGeoJson'
    'GeoJSONReader (Reactive)' = 'GeoJsonReaderReactiveNode'
}

# VL.NetTopologySuite's nodes the help opens a feature with. Not ours, but a help patch that uses
# them depends on that package resolving here - the same silent link-drop applies - so they are
# checked the same way, anchored on that package's own .vl.
$DependencyNodes = @{
    'VL.NetTopologySuite' = @{
        'Split'        = 'FeatureNodes.Split('
        'GetAttribute' = 'FeatureNodes.GetAttribute('
    }
}

# Pin names this package renames away from VL's defaults. A wrong one is the quietest failure
# there is: the NODE resolves, so every check above stays green, while the LINK to that pin is
# dropped and the patch silently computes nothing. Assert the generated C# names the variable
# after the pin, which is what vvvvc does.
$OurPins = @{
    'Read GeoJSON' = 'Features_'
}

$failures = New-Object System.Collections.Generic.List[string]
$nodesChecked = 0

foreach ($p in $patches) {
    Write-Host "`ncompiling $($p.Name)" -ForegroundColor Cyan

    $outDir = Join-Path $OutputDirectory ($p.BaseName -replace '[^\w\-]', '_')
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    New-Item -ItemType Directory -Force $outDir | Out-Null
    Copy-Item (Join-Path $OutputDirectory 'NuGet.config') $outDir

    $log = & $vvvvc $p.FullName '--package-repositories' $repositories '--output-directory' $outDir 2>&1 | Out-String

    # This check is only meaningful on a COLD %USERPROFILE%\.nuget\packages\<id> cache, and that
    # is worth knowing before trusting a green run. Negative-tested by emptying the NuGet.config:
    # with the cache evicted it correctly reports NU1101, and with the cache warm it cannot fail
    # at all, because restore is satisfied from disk and never consults a source.
    #
    # pack.ps1 evicts that folder, so the honest sequence is `.\pack.ps1` then this script - which
    # is what the precondition above enforces. Running this twice in a row proves less the second
    # time. Same false green that made vl-mapsui believe for weeks that one repository argument was
    # enough.
    if ($log -match 'NU1101') {
        $failures.Add("$($p.Name): restore failed with NU1101 - the feed is not being found")
        Write-Host "  FAIL  NU1101 - dist\feed is not reaching restore" -ForegroundColor Red
    }

    $generated = @(Get-ChildItem $outDir -Recurse -Filter '*.vl.1.cs' -ErrorAction SilentlyContinue)
    if ($generated.Count -eq 0) {
        $failures.Add("$($p.Name): no C# was generated")
        Write-Host "  FAIL  no C# generated" -ForegroundColor Red
        Write-Host (($log -split "`n" | Select-Object -Last 12) -join "`n") -ForegroundColor DarkGray
        continue
    }

    $csharp = ($generated | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
    $patchXml = Get-Content $p.FullName -Raw

    # Every node the patch takes from THIS package, by name. A name missing from $OurNodes is a
    # failure, so a node added to the package and forgotten in the table shows up the first time a
    # patch uses it instead of going unchecked.
    $ourAnchor = 'LastDependency="' + [regex]::Escape("$PackageName.vl") + '">'
    $claimed = @([regex]::Matches($patchXml,
            $ourAnchor + '\s*(?:<[^>]+>\s*)*?<Choice Kind="(?:OperationCallFlag|ProcessAppFlag|ProcessNodeFlag|OperationFlag)" Name="([^"]+)"') |
        ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    foreach ($name in $claimed) {
        if (-not $OurNodes.ContainsKey($name)) {
            $failures.Add("$($p.Name): uses '$name' from $PackageName, which the node table does not know")
            Write-Host "  FAIL  '$name' is not in the node table" -ForegroundColor Red
        }
    }
    # The patch names our package but no node of it was recognised: the anchor is stale.
    if ($patchXml -match $ourAnchor -and $claimed.Count -eq 0) {
        $failures.Add("$($p.Name): references $PackageName.vl but no node of it was recognised - the anchor is stale")
        Write-Host "  FAIL  references $PackageName.vl, recognised no node" -ForegroundColor Red
    }

    $checked = 0
    foreach ($nodeName in $OurNodes.Keys) {
        # Does the patch claim to use this node, against OUR dependency rather than someone else's?
        # "Split" in particular is a name several libraries use.
        $claims = [regex]::IsMatch(
            $patchXml,
            $ourAnchor + '\s*(?:<[^>]+>\s*)*?<Choice Kind="\w+Flag" Name="' + [regex]::Escape($nodeName) + '"')
        if (-not $claims) { continue }

        $checked++
        if ($csharp -notmatch [regex]::Escape($OurNodes[$nodeName])) {
            $failures.Add("$($p.Name): '$nodeName' is in the patch but NOT in the generated C# - it did not resolve")
            Write-Host "  FAIL  '$nodeName' did not resolve" -ForegroundColor Red
        }
        else {
            Write-Host "  ok    '$nodeName' resolved" -ForegroundColor DarkGray
        }

        if ($OurPins.ContainsKey($nodeName)) {
            $expected = $OurPins[$nodeName]
            if ($csharp -notmatch [regex]::Escape($expected)) {
                $failures.Add("$($p.Name): '$nodeName' resolved but its renamed output pin did not - no '$expected' variable in the generated C#, so the link was dropped")
                Write-Host "  FAIL  '$nodeName' output pin rename did not take" -ForegroundColor Red
            }
            else {
                Write-Host "  ok    '$nodeName' output pin is named as intended" -ForegroundColor DarkGray
            }
        }
    }

    $nodesChecked += $checked
    if ($checked -eq 0) { Write-Host "  ok    compiled (uses no $PackageName node)" -ForegroundColor DarkGray }

    foreach ($dep in $DependencyNodes.Keys) {
        $depAnchor = 'LastDependency="' + [regex]::Escape("$dep.vl") + '">'
        $depClaimed = @([regex]::Matches($patchXml,
                $depAnchor + '\s*(?:<[^>]+>\s*)*?<Choice Kind="(?:OperationCallFlag|ProcessAppFlag|ProcessNodeFlag|OperationFlag)" Name="([^"]+)"') |
            ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        foreach ($name in $depClaimed) {
            $table = $DependencyNodes[$dep]
            if (-not $table.ContainsKey($name)) {
                $failures.Add("$($p.Name): uses '$name' from $dep, which the dependency node table does not know")
                Write-Host "  FAIL  '$name' ($dep) is not in the dependency node table" -ForegroundColor Red
            }
            elseif ($csharp -notmatch [regex]::Escape($table[$name])) {
                $failures.Add("$($p.Name): '$name' from $dep is in the patch but NOT in the generated C# - it did not resolve")
                Write-Host "  FAIL  '$name' ($dep) did not resolve" -ForegroundColor Red
            }
            else {
                Write-Host "  ok    '$name' ($dep) resolved" -ForegroundColor DarkGray
            }
        }
    }

    # A process node built in Update instead of Create is the expensive mistake in this ecosystem.
    if ($csharp -match 'new\s+\w*\.?ReadGeoJsonNode\(\)') {
        # Two traps in one line, both of which made this report every patch as broken:
        #
        #   `public [\w\.]+ __Create__` - the FIRST "__Create__" in the file is a CALL,
        #       `return instance.__Create__(Node_Context);`, not the definition.
        #   `.*?\)\{` rather than `[^)]*\)` - the parameter list has its own parentheses,
        #       `__Create__([n3.Pin(Name = @"Node Context", ...)] n4.NodeContext Node_Context){`.
        $createBody = [regex]::Match(
            $csharp, 'public\s+[\w\.]+\s+__Create__\(.*?\)\{(?<body>.*?)\n        \}', 'Singleline')
        if ($createBody.Success -and $createBody.Groups['body'].Value -match 'new\s+\w*\.?ReadGeoJsonNode\(\)') {
            Write-Host "  ok    Read GeoJSON is constructed in Create, not Update" -ForegroundColor DarkGray
        }
        else {
            $failures.Add("$($p.Name): ReadGeoJsonNode is not constructed in __Create__")
            Write-Host "  FAIL  Read GeoJSON is not constructed in Create" -ForegroundColor Red
        }
    }
}

# A run that checked no node of ours at all proves nothing, however green: every patch would
# say "uses no node" if the patches named the package by a name this script does not match.
if ($nodesChecked -eq 0) {
    $failures.Add("no patch was found using any $PackageName node - the dependency name in the patches and the package disagree")
}

if (-not $KeepOutput -and (Test-Path $OutputDirectory)) { Remove-Item $OutputDirectory -Recurse -Force }

Write-Host ""
if ($failures.Count -gt 0) {
    foreach ($f in $failures) { Write-Host "  $f" -ForegroundColor Red }
    Write-Host "`nFAIL - $($failures.Count) problem(s)." -ForegroundColor Red
    exit 1
}

Write-Host "PASS - $($patches.Count) patch(es) compiled, every $PackageName node resolved, no NU1101." -ForegroundColor Green
Write-Host @"
Note: this proves the nodes RESOLVE. It does not prove which CATEGORY they appear under - only
the NodeBrowser does - nor that the patch computes the right value, which only running it does.
"@ -ForegroundColor Yellow

# Explicitly, because vvvvc's own exit code may still be sitting in $LASTEXITCODE. Falling off the
# end here would report it for a run that passed, and a green check reported as red gets ignored.
exit 0
