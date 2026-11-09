<#
.SYNOPSIS
  Turns Gameface live reload off or on for bridge runs, or shows its state (tools/testbridge/README.md, "Turning live reload off").

.DESCRIPTION
  With --uiDeveloperMode, UIManager sets liveReload = developerMode. About 6 to 10 s after every mode change a "media" reload
  (UILiveReload, "Reloading media 0" in UI.log) loads the page again. The silent exits of 2026-11-08 came in that window (UNVERIFIED
  cause, a correlation). UIManager reads a "UIManager Settings" section of FallbackSettings.coc after its defaults (UIManager.cs:191),
  so this section turns live reload off:

      UIManager Settings
      {
          "liveReload": false
      }

  The game keeps the section when it rewrites the .coc files on exit (measured, round 622 icons2-pass). Bridge runs keep it OFF by
  default. Turn it on only to test a UI file edit in a running game (deploys happen with the game closed anyway).

  -Off    adds the section at the end (or sets "liveReload": false in an existing one).
  -On     removes the liveReload key, and the section when nothing else is in it.
  -Status prints the state (the default).
  Every edit first copies the file to FallbackSettings.coc.live-reload-<yyyyMMdd-HHmmss-fff>.bak beside it. Only the section changes:
  every other byte stays (the game's file mixes LF and CRLF line ends; the section uses CRLF). UTF-8 without BOM. An edit refuses to
  run while Cities2 runs: the game rewrites the file on exit. -Off then -On gives back the same bytes.

  Exit codes: 0 done (or state printed), 1 the file is missing, 2 Cities2 is running (edit refused).

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\testbridge\live-reload.ps1 -Off
#>
[CmdletBinding(DefaultParameterSetName = "Status")]
param(
  [Parameter(ParameterSetName = "Off")][switch]$Off,
  [Parameter(ParameterSetName = "On")][switch]$On,
  [Parameter(ParameterSetName = "Status")][switch]$Status,
  # For tests: another file than the game's.
  [string]$Path = (Join-Path $env:USERPROFILE "AppData\LocalLow\Colossal Order\Cities Skylines II\FallbackSettings.coc")
)
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Path)) { Write-Output "live-reload: FAIL $Path is missing"; exit 1 }
$text = [IO.File]::ReadAllText($Path)

# The whole section, from its name line to its closing brace line (with that line's end). Group "body" = the lines between the braces.
$secRe = [regex]'(?m)^UIManager Settings[ \t]*\r?\n\{[ \t]*\r?\n(?<body>(?:(?!\}[ \t]*(?:\r?\n|$)).*\r?\n)*?)\}[ \t]*(?:\r?\n|$)'
$keyRe = [regex]'(?m)^[ \t]*"liveReload"[ \t]*:[ \t]*(?:true|false)[ \t]*,?[ \t]*\r?\n'

function State([string]$t) {
  $m = $secRe.Match($t)
  if (-not $m.Success) { return "ON (no UIManager Settings section: the default, liveReload = developerMode)" }
  $body = $m.Groups["body"].Value
  if ($body -match '"liveReload"\s*:\s*false') { return "OFF (UIManager Settings has `"liveReload`": false)" }
  if ($body -match '"liveReload"\s*:\s*true') { return "ON (UIManager Settings has `"liveReload`": true)" }
  return "ON (UIManager Settings has no liveReload key: the default)"
}

if (-not ($Off -or $On)) { Write-Output "live-reload: $(State $text)  [$Path]"; exit 0 }

if (Get-Process -Name Cities2 -ErrorAction SilentlyContinue) {
  Write-Output "live-reload: REFUSED Cities2 is running; close the game first (it rewrites the file on exit)"
  exit 2
}

$m = $secRe.Match($text)
$out = $text
if ($Off) {
  if (-not $m.Success) {
    $lead = if ($text.Length -gt 0 -and -not $text.EndsWith("`n")) { "`r`n" } else { "" }
    $out = $text + $lead + "UIManager Settings`r`n{`r`n    `"liveReload`": false`r`n}`r`n"
  } else {
    $body = $m.Groups["body"].Value
    if ($body -match '"liveReload"\s*:') {
      $nb = [regex]::Replace($body, '"liveReload"(\s*):(\s*)true', '"liveReload"$1:$2false')
    } else {
      # First entry; a comma when another entry follows.
      $nb = "    `"liveReload`": false" + $(if ($body.Trim()) { "," } else { "" }) + "`r`n" + $body
    }
    $g = $m.Groups["body"]
    $out = $text.Substring(0, $g.Index) + $nb + $text.Substring($g.Index + $g.Length)
  }
} elseif ($m.Success) {
  $body = $m.Groups["body"].Value
  $rest = $keyRe.Replace($body, "")
  if (-not $rest.Trim()) {
    $out = $text.Substring(0, $m.Index) + $text.Substring($m.Index + $m.Length)
  } else {
    # The entry that is now last must not end with a comma.
    $rest = [regex]::Replace($rest, ',([ \t]*\r?\n)$', '$1')
    $g = $m.Groups["body"]
    $out = $text.Substring(0, $g.Index) + $rest + $text.Substring($g.Index + $g.Length)
  }
}

if ($out -eq $text) { Write-Output "live-reload: unchanged, $(State $text)"; exit 0 }
$bak = "$Path.live-reload-$(Get-Date -Format yyyyMMdd-HHmmss-fff).bak"
Copy-Item -LiteralPath $Path -Destination $bak
[IO.File]::WriteAllText($Path, $out, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "live-reload: $(State $out)  [backup $bak]"
exit 0
