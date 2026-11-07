# Validate a hand-edited CS2 prefab JSON against the known hard-crash rules:
#   1. Positional type table: "$type": N may only appear AFTER the file declares "N|Full.Type"
#      earlier ("Missing type id" crash otherwise).
#   2. An index must not be declared twice with different type names.
#   3. Declaration ORDER is NOT checked: it is unconstrained (SkiChairliftHV declares 11 after
#      17 and loads fine. Gaps are fine too). The 2026-08-05 "Entry ... missing a name" /
#      "Missing type ID" spam came from mangled newlines (see the suspicious-line check below),
#      not from ordering. See the NOTE in the loop and PIPELINE section 6.
param([Parameter(Mandatory = $true)][string]$Path)

$lines = Get-Content $Path
$declared = @{}
$errors = 0
$braces = 0
$maxDeclared = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    $braces += ([regex]::Matches($line, '{')).Count - ([regex]::Matches($line, '}')).Count
    # Shell-escaping mangles (learned 2026-08-05: a perl one-liner turned every \n into
    # literal '&n', flattening a whole component to one unparseable line). Odin then spams
    # "Entry ... is missing a name" with no asset name. Catch the shape, not the cause.
    if ($line.Length -gt 400 -or $line -match '&n |\\n"') {
        Write-Output "ERROR line $($i+1): suspicious line (len $($line.Length)) - likely escaping mangle from a shell edit"
        $errors++
    }
    if ($line -match '"\$type":\s*"(\d+)\|([^"]+)"') {
        $idx = [int]$Matches[1]; $name = $Matches[2]
        if ($declared.ContainsKey($idx) -and $declared[$idx] -ne $name) {
            Write-Output "ERROR line $($i+1): type id $idx redeclared as '$name' (was '$($declared[$idx])')"
            $errors++
        }
        # NOTE: declaration ORDER is unconstrained — SkiChairliftHV declares 11 after 17 and
        # has always loaded fine. (This script briefly enforced a monotonic-order rule here during
        # the 2026-08-05 debugging. The real culprit was mangled newlines, not ordering.)
        $maxDeclared = [math]::Max($maxDeclared, $idx)
        $declared[$idx] = $name
    }
    elseif ($line -match '"\$type":\s*(\d+)\s*,?\s*$') {
        $idx = [int]$Matches[1]
        if (-not $declared.ContainsKey($idx)) {
            Write-Output "ERROR line $($i+1): forward/undeclared type reference $idx"
            $errors++
        }
    }
}
if ($braces -ne 0) { Write-Output "ERROR: unbalanced braces (delta $braces)"; $errors++ }
if ($errors -eq 0) {
    Write-Output ("OK: {0} - {1} type ids declared, no forward refs, braces balanced" -f (Split-Path $Path -Leaf), $declared.Count)
}
exit [int]($errors -gt 0)
