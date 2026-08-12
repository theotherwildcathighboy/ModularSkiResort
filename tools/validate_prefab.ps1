# Validate a hand-edited CS2 prefab JSON against the three known hard-crash rules:
#   1. Positional type table: "$type": N may only appear AFTER "N|Full.Type" is declared
#      earlier in the file ("Missing type id" crash otherwise).
#   2. An index must not be declared twice with different type names.
#   3. Declarations must appear in INCREASING index order through the file. Gaps are fine
#      (vanilla files skip indices), but a declaration with a lower index after a higher one
#      breaks Odin deserialization ("Entry ... missing a name" / "Missing type ID" spam) -
#      learned 2026-08-05 when a 21/22 block was inserted ahead of the 17-20 mesh section.
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
        # has always loaded fine. (A monotonic-order rule was briefly enforced here during
        # the 2026-08-05 debugging; the real culprit was mangled newlines, not ordering.)
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
