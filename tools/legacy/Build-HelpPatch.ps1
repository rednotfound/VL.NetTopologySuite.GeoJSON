# RETIRED - do not run this.
#
# This built the six help patches from scratch, which was the right way to get them working: the
# token substitution below is what makes a Link endpoint correct by construction instead of by
# proofreading sixty 22-character identifiers, and it made one structural refactor (removing the
# FeatureCollection node from four patches at once) tractable.
#
# That job is done. The patches under help\VL.GeoJSON\ are now the source of truth.
#
# vl-mapsui reached this same point by losing a patch: its generators sit in tools\legacy\ with the
# note "Running this again would overwrite it - which already happened once, discarding node
# positions someone had arranged by hand to make the patch readable." Their rule reverses an earlier
# one - "regenerate, never edit in place" - which had been written when hand-patching produced
# thirteen duplicated nodes and six colliding IDs. Regenerating turned out to be the more
# destructive of the two ONCE A PATCH WAS IN USE.
#
# These patches are in use now. So the mitigation moved from "avoid hand-editing" to "validate every
# edit": anchor each insertion on a match that occurs exactly once and fail loudly otherwise, then
# run tools\Test-VLPatch.ps1, tools\Test-VLPackage.ps1 and tools\Compile-HelpPatches.ps1.
#
# The .vltemplate files beside this script are a snapshot of the initial generation. They have NOT
# tracked the .vl files since - the fontsize and annotation-box fixes went into the .vl only - so
# running this with -Force would silently roll those back. That divergence is the whole reason both
# are here rather than in help\.
#
# Read docs\VL-PATCH-XML.md before editing a .vl by hand.

<#
.SYNOPSIS
    RETIRED. Turned a .vltemplate into a .vl, substituting a fresh valid VL Id for every %%name%% token.

.DESCRIPTION
    A .vl needs an Id on nearly every element: 22 characters, first [A-V], rest [0-9A-Za-z],
    unique within the document. Hand-writing sixty of those per patch and keeping the Link
    endpoints pointing at the right ones is exactly the kind of bookkeeping that produced
    thirteen duplicated nodes and six colliding IDs in a sibling repository.

    So a template names things instead:

        <Pad Id="%%pad_status%%" ... />
        <Link Id="%%link_1%%" Ids="%%pin_status_out%%,%%pad_status%%" />

    Every distinct token gets one fresh Id, and a token used twice resolves to the same Id -
    which is what makes a Link endpoint correct by construction rather than by proofreading.

    THIS IS A GENERATOR, AND GENERATORS ARE DANGEROUS HERE. The sibling repositories both
    arrived at "the checked-in .vl is the source of truth" after regenerating discarded a
    layout arranged by hand in the GUI. So:

      - Run this ONCE to create a patch, then edit the .vl in place from then on.
      - It REFUSES to overwrite an existing .vl unless -Force is given.

    The output is UTF-8 WITH BOM, because vvvv will not load a .vl without one.

.EXAMPLE
    .\tools\Build-HelpPatch.ps1 -Template .\help\VL.GeoJSON\_templates\Explanation.vltemplate
#>
param(
    [Parameter(Mandatory = $true)][string]$Template,
    [string]$OutputPath = '',
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

throw @"
Build-HelpPatch.ps1 is RETIRED - see the header of this file.

The checked-in .vl under help\VL.GeoJSON\ is the source of truth, and the .vltemplate files here
have not tracked it since the patches were first generated. Regenerating would roll back every
edit made since, including the annotation-box and fontsize fixes.

Edit the .vl in place, or in vvvv, then run:
  .\tools\Test-VLPatch.ps1
  .\tools\Test-VLPackage.ps1
  .\pack.ps1 ; .\tools\Compile-HelpPatches.ps1

If you genuinely mean to start a patch over from a template, delete this throw and know why.
"@

if (-not (Test-Path $Template)) { throw "Template not found: $Template" }

if (-not $OutputPath) {
    $OutputPath = Join-Path (Split-Path (Split-Path $Template -Parent) -Parent) `
                            ((Split-Path $Template -Leaf) -replace '\.vltemplate$', '.vl')
}

if ((Test-Path $OutputPath) -and -not $Force) {
    throw @"
$OutputPath already exists.

The checked-in .vl is the source of truth - regenerating it would discard any layout arranged
in the GUI, which is how a sibling repository lost one. Edit the .vl in place, or pass -Force
if you genuinely mean to start over.
"@
}

$firstChars = [char[]]'ABCDEFGHIJKLMNOPQRSTUV'
$restChars  = [char[]]'0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz'

function New-VLId {
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $bytes = [byte[]]::new(4)
        $sb = [System.Text.StringBuilder]::new(22)
        function Get-Index([int]$modulo) {
            # Rejection sampling, so the distribution is not skewed by the modulo. Overkill for
            # an identifier, but a biased generator is a collision generator at scale.
            $limit = [uint32]::MaxValue - ([uint32]::MaxValue % $modulo) - 1
            while ($true) {
                $rng.GetBytes($bytes)
                $value = [BitConverter]::ToUInt32($bytes, 0)
                if ($value -le $limit) { return [int]($value % $modulo) }
            }
        }
        [void]$sb.Append($firstChars[(Get-Index $firstChars.Length)])
        for ($i = 1; $i -lt 22; $i++) { [void]$sb.Append($restChars[(Get-Index $restChars.Length)]) }
        $sb.ToString()
    }
    finally { $rng.Dispose() }
}

$text = [IO.File]::ReadAllText((Resolve-Path $Template))

$tokens = [regex]::Matches($text, '%%([A-Za-z0-9_]+)%%') |
    ForEach-Object { $_.Groups[1].Value } |
    Sort-Object -Unique

if ($tokens.Count -eq 0) { throw "No %%token%% placeholders found in $Template" }

$map = @{}
foreach ($token in $tokens) {
    do { $id = New-VLId } while ($map.Values -contains $id)   # uniqueness within the document
    $map[$token] = $id
}

foreach ($token in $tokens) {
    $text = $text.Replace("%%$token%%", $map[$token])
}

if ($text -match '%%') { throw "Unsubstituted placeholder left in output - check the token syntax" }

# UTF-8 WITH BOM. Without it vvvv will not load the document at all.
$dir = Split-Path $OutputPath -Parent
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force $dir | Out-Null }
[IO.File]::WriteAllText($OutputPath, $text, (New-Object System.Text.UTF8Encoding($true)))

# Parse it before claiming success. A malformed template produces a file vvvv rejects silently.
try { [xml](Get-Content $OutputPath -Raw) | Out-Null }
catch { Remove-Item $OutputPath -Force; throw "Generated document is not valid XML: $_" }

Write-Host "  wrote $OutputPath  ($($tokens.Count) ids)" -ForegroundColor Green
