<#
gen_layouts.ps1 - writes the Write Everywhere (WE) default layouts for the writable boards.
Doc: docs/research/art/assets-2026-10/build/writable-signs-we.md (mechanism, table, unverified list).

  powershell -File assets\we\gen_layouts.ps1 [-PaintedOn]

Output (repo only, nothing is deployed):
  assets\we\K45_WE\layouts\<Prefab>.wedefault.xml        the SHIPPED set = variant N: every x negated and every yaw mirrored
                                                          (the game negates FBX x; x-sign gate settled N by the 2026-10-15 playtest,
                                                          swapped in 2026-10-16 / T14)
  assets\we\xpos\K45_WE\layouts\<Prefab>.wedefault.xml   the retired variant P (old manifest frame, gx as exported): a record only
  (assets\we\xneg\... is the pre-swap copy of the N set, byte-equal to the shipped set; no longer written)
  assets\we\optional\SkiResortGate.header-overlay.xml    opt-in patch + text over the baked "SKI AREA" header (not shipped)

SkiResortGate.wedefault.xml (the plank name) is hand-kept and asserted by tests\test_civic_sign.py: this script never
writes it, it only copies it into the xpos record set (every x in it is 0).

-PaintedOn : the cover patch + text of the PAINTED boards (GRAND LODGE, SKI SCHOOL) start drawn (Show when = Always);
             default is off, so a WE player sees the painted board until they switch the node on.
             The node is named "<board>: patch" (child "<board>: text"): WE keeps only 29 UTF-8 bytes of an item name, so the
             longer "...: cover patch" showed as "...cover patc" (writable-signs-we-check.md item 12). Longest name now: 24 bytes.

Fonts: the painted boards name the face of their painted lettering as WE registers a font installed from C:\Windows\Fonts
(file stem: BOOKOSB = Bookman Old Style Bold, FRADMCN = Franklin Gothic Demi Cond). Not installed = WE falls back to its embedded
Source Sans Pro (WEPostRendererSystem.cs:226), so naming it costs nothing. Nothing is shipped.

Numbers: face centre / normal / size come from the sign-set and host manifests (we_boards); the text size from the WE
2.0.0.15 font chain (cap height = 0.952 m x scale.y, Source Sans Pro Regular; width of "GRAND LODGE" = 8.71 m x scale.x).
#>
param([switch]$PaintedOn)

$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture
function F([double]$v) { if ([math]::Abs($v) -lt 0.00005) { return '0' }; return $v.ToString('0.####', $inv) }
function Esc([string]$s) { return $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;') }

$STANDOFF = 0.004                       # patch / text stand 4 mm in front of the painted face (the child text adds 1 mm itself)
$on = if ($PaintedOn) { 1 } else { 0 }

# ---- the boards: prefab -> nodes (frame = host prefab local, the OLD +x frame of the pre-2026-10-16 manifests' we_boards;
#      NodeXml negates x and mirrors the yaw for the shipped N set; the regenerated manifests' we_boards are already N) ---
# Kind: painted = cover patch (board ground colour) + text child, opt-in; blank = text node only, empty default.
$sets = [ordered]@{
  'SkiLodgeGrand' = @(
    @{ Kind='painted'; Name='GRAND LODGE board'; Centre=@(0,5.0,31.145); N=@(0,1); Size=@(5.0,0.8); Inset=0.07
       Ground='33241AFF'; Ink='EDE3C7FF'; Text='GRAND LODGE'; Font='BOOKOSB'; Scale=0.38; MaxW=4.2 } )
  'SkiSchool' = @(
    @{ Kind='painted'; Name='SKI SCHOOL board'; Centre=@(1.0,3.75,16.08); N=@(0,1); Size=@(4.2,0.9); Inset=0.02
       Ground='33241AFF'; Ink='EBEBEBFF'; Text='SKI SCHOOL'; Font='FRADMCN'; Scale=0.45; MaxW=3.8 } )
  'SkiLodgeMid' = @(
    @{ Kind='blank'; Name='Label: arrival board';   Centre=@(-12.0,3.8,13.08); N=@(0,1);  Size=@(4.0,0.6); Ink='EDE3C7FF'; Scale=0.35; MaxW=3.5 }
    @{ Kind='blank'; Name='Label: podium board';    Centre=@(5.08,2.9,-3.0);   N=@(1,0);  Size=@(4.0,0.6); Ink='EDE3C7FF'; Scale=0.35; MaxW=3.5 }
    @{ Kind='blank'; Name='Label: kiosk board';     Centre=@(9.75,1.65,-12.12); N=@(0,1); Size=@(1.6,0.5); Ink='EDE3C7FF'; Scale=0.29; MaxW=1.3 } )
  'SkiLodgeSummit' = @(
    @{ Kind='blank'; Name='Label: forecourt board'; Centre=@(-12.5,0.875,19.16); N=@(0,1); Size=@(1.6,0.85); Ink='3A3A3DFF'; Scale=0.42; MaxW=1.3 } )
  'SkiLodgeHut' = @(
    @{ Kind='blank'; Name='Label: entry board';     Centre=@(0.0,4.15,8.08);   N=@(0,1);  Size=@(2.4,0.6); Ink='EDE3C7FF'; Scale=0.35; MaxW=2.0 } )
}

# optional: the baked "SKI AREA" header lettering panel of the welcome sign (sign set SkiSignSetGate, face sign_gate_face)
$headerOverlay = @{ Kind='painted'; Name='SKI AREA header'; Centre=@(0,4.15,-0.35); N=@(0,1); Size=@(2.6,0.6); Inset=0.02
                    Ground='557867FF'; Ink='EDE3C7FF'; Text='SKI AREA'; Font='BOOKOSB'; Scale=0.33; MaxW=2.3 }

# ---- XML pieces --------------------------------------------------------------------------------------------------
function Transform($pos, [double]$yaw, $scale, [bool]$gated, [int]$draw) {
  $g = if ($gated) { ' useFormulaeToCheckIfDraw="true"' } else { '' }
@"
<transform alignment="Left"$g arrayAxisOrder="XYZ">
  <offsetPosition x="$(F $pos[0])" y="$(F $pos[1])" z="$(F $pos[2])" />
  <offsetRotation x="0" y="$(F $yaw)" z="0" />
  <scale x="$(F $scale[0])" y="$(F $scale[1])" z="1" />
  <mustDraw>$draw</mustDraw>
  <instanceCount>-1</instanceCount>
  <arraySpacingData />
  <arrayInstancesData>
    <defaultValue x="1" y="1" z="1" />
  </arrayInstancesData>
</transform>
"@
}
function PatchStyle([string]$rgba) {
@"
<defaultStyle decalFlags="0">
  <color>$rgba</color>
  <emissiveColor>00000000</emissiveColor>
  <metallic>0</metallic>
  <smoothness>0.1</smoothness>
  <emissiveIntensity>0</emissiveIntensity>
  <emissiveExposureWeight>0</emissiveExposureWeight>
  <coatStrength>0</coatStrength>
  <colorMask1>000000</colorMask1>
  <colorMask2>000000</colorMask2>
  <colorMask3>000000</colorMask3>
</defaultStyle>
"@
}
function TextStyle([string]$rgba) {
@"
<defaultStyle>
  <color>$rgba</color>
  <emissiveColor>$rgba</emissiveColor>
  <metallic>0</metallic>
  <smoothness>0.2</smoothness>
  <emissiveIntensity>0</emissiveIntensity>
  <emissiveExposureWeight>0</emissiveExposureWeight>
  <coatStrength>0</coatStrength>
  <colorMask1>FFFFFF</colorMask1>
  <colorMask2>FFFFFF</colorMask2>
  <colorMask3>FFFFFF</colorMask3>
</defaultStyle>
"@
}
function TextMesh([string]$text, [double]$maxW, [string]$font = '') {
@"
<textMesh fontName="$font" rescaleHeightOnTextOverflow="true">
  <text>$(Esc $text)</text>
  <MaxWidthMeters>$(F $maxW)</MaxWidthMeters>
</textMesh>
"@
}

# yaw (degrees) that turns a +Z-facing WE node to face (nx, nz): Unity yaw, rotation 0 faces +Z, +90 faces +X
function Yaw($n) { return [math]::Round([math]::Atan2($n[0], $n[1]) * 180.0 / [math]::PI, 4) }

function Ind([string]$s, [string]$pad) {
  return (($s -split "`r?`n" | Where-Object { $_ -ne '' } | ForEach-Object { $pad + $_ }) -join "`n")
}

function NodeXml($b, [bool]$neg) {
  $c = @($b.Centre[0], $b.Centre[1], $b.Centre[2]); $n = @($b.N[0], $b.N[1])
  if ($neg) { $c[0] = -$c[0]; $n[0] = -$n[0] }
  $yaw = Yaw $n
  # 4 mm in front of the face along its outward normal (normal in the xz plane)
  $pos = @(($c[0] + $n[0] * $STANDOFF), $c[1], ($c[2] + $n[1] * $STANDOFF))
  $sc = @($b.Scale, $b.Scale)
  $out = @()
  if ($b.Kind -eq 'painted') {
    $psz = @(($b.Size[0] - 2 * $b.Inset), ($b.Size[1] - 2 * $b.Inset))
    $name = Esc $b.Name
    $out += '  <children>'
    $out += ('    <self itemName="' + $name + ': patch">')
    $out += Ind (Transform $pos $yaw $psz $true $on) '      '
    $out += '      <whiteMesh />'
    $out += Ind (PatchStyle $b.Ground) '      '
    $out += '    </self>'
    $out += '    <children>'
    $out += ('      <self itemName="' + $name + ': text">')
    $out += Ind (Transform @(0,0,0) 0 $sc $false 1) '        '
    $out += Ind (TextMesh $b.Text $b.MaxW $b.Font) '        '
    $out += Ind (TextStyle $b.Ink) '        '
    $out += '      </self>'
    $out += '    </children>'
    $out += '  </children>'
  } else {
    $out += '  <children>'
    $out += ('    <self itemName="' + (Esc $b.Name) + '">')
    $out += Ind (Transform $pos $yaw $sc $false 1) '      '
    $out += Ind (TextMesh '' $b.MaxW) '      '
    $out += Ind (TextStyle $b.Ink) '      '
    $out += '    </self>'
    $out += '  </children>'
  }
  return ($out -join "`n")
}

function FileXml([string]$prefab, $nodes, [bool]$neg, [string]$extra = '') {
  $frame = if ($neg) { 'VARIANT N: x negated, yaw mirrored (the game negates FBX x)' } else { 'VARIANT P: manifest frame, gx as exported' }
  $head = @"
<?xml version="1.0" encoding="utf-8"?>
<!-- Write Everywhere default layout for $prefab (optional: the mod works without WE). Generated by assets/we/gen_layouts.ps1,
     $frame. Doc: docs/research/art/assets-2026-10/build/writable-signs-we.md.
     Frame = prefab-local game metres, offset = painted face centre + 4 mm along its normal, rotation y 0 faces +Z.
     Never add hideMesh.$extra -->
<WELayout>
"@
  $body = ($nodes | ForEach-Object { NodeXml $_ $neg }) -join "`n"
  return ($head + "`n" + $body + "`n</WELayout>`n")
}

function WriteFile([string]$path, [string]$text) {
  New-Item -ItemType Directory -Force -Path (Split-Path $path) | Out-Null
  [IO.File]::WriteAllText($path, ($text -replace "`r`n", "`n"), (New-Object Text.UTF8Encoding($false)))
  Write-Host ("wrote " + $path)
}

$root = $PSScriptRoot
foreach ($p in $sets.Keys) {
  WriteFile (Join-Path $root "K45_WE\layouts\$p.wedefault.xml") (FileXml $p $sets[$p] $true)
  WriteFile (Join-Path $root "xpos\K45_WE\layouts\$p.wedefault.xml") (FileXml $p $sets[$p] $false)
}
# the welcome sign plank layout is hand-kept; every x in it is 0, so the P record takes it verbatim
$gate = Join-Path $root 'K45_WE\layouts\SkiResortGate.wedefault.xml'
if (Test-Path $gate) {
  $dst = Join-Path $root 'xpos\K45_WE\layouts\SkiResortGate.wedefault.xml'
  New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
  Copy-Item $gate $dst -Force; Write-Host ("copied " + $dst)
}
# opt-in header overlay (x = 0, frame-free): paste its <children> block into the shipped SkiResortGate layout to use it
$hx = FileXml 'SkiResortGate' @($headerOverlay) $false (("`n     OPTIONAL, NOT SHIPPED: paste the <children> block below into K45_WE/layouts/SkiResortGate.wedefault.xml to make the baked SKI AREA header writable") + "`n     (the shipped file has exactly one textMesh, tests/test_civic_sign.py asserts it: update the test in the same change).")
WriteFile (Join-Path $root 'optional\SkiResortGate.header-overlay.xml') $hx
