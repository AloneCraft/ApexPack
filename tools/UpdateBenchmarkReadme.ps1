param(
    [Parameter(Mandatory)] [string] $ComparisonCsv,
    [string] $Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture
$rows = @(Import-Csv -LiteralPath $ComparisonCsv)
$groups = @($rows | Group-Object Runtime | Sort-Object { [version]$_.Name })
if (($groups | ForEach-Object { ([version]$_.Name).Major }) -join ',' -ne '9,10,11') {
    throw 'The report must include .NET 9, 10, and 11.'
}

$headline = [ordered]@{
    'Compare_Collection_LinkedList_Int32_1024' = '`LinkedList<int>` (1,024 items)'
    'Compare_Collection_SortedDictionary_Int32_1024' = '`SortedDictionary<int, int>` (1,024 entries)'
    'Compare_Graph_Shared_1000' = 'Shared references (1,000 nodes)'
    'Compare_Graph_Cycle_1000' = 'Cycle (1,000 nodes)'
    'Compare_Nullability_RequiredText' = 'Required string, all values present'
    'Compare_Nullability_OptionalText' = 'Nullable string, all values present'
    'Compare_Nullability_RequiredChild' = 'Required child object, all values present'
    'Compare_Nullability_OptionalChild' = 'Nullable child object, all values present'
}
$timings = 'ApexSerializeNs', 'MemoryPackSerializeNs', 'ApexDeserializeNs', 'MemoryPackDeserializeNs'
$bytes = 'ApexOutputBytes', 'MemoryPackOutputBytes',
    'ApexSerializeAllocatedBytesPerOp', 'MemoryPackSerializeAllocatedBytesPerOp',
    'ApexDeserializeAllocatedBytesPerOp', 'MemoryPackDeserializeAllocatedBytesPerOp'

function Read-Number($Row, [string] $Column, [bool] $AllowZero) {
    $value = 0.0
    if (![double]::TryParse($Row.$Column, [Globalization.NumberStyles]::Float, $culture, [ref]$value) -or
        ![double]::IsFinite($value) -or $value -lt 0 -or (!$AllowZero -and $value -eq 0)) {
        throw "Missing or invalid value for $($Row.ScenarioId) / $Column"
    }
    return $value
}

function Format-Pair([double] $Apex, [double] $Memory, [string] $Format) {
    return "$($Apex.ToString($Format, $culture)) / $($Memory.ToString($Format, $culture))"
}

$table = foreach ($group in $groups) {
    $byId = @{}
    foreach ($row in $group.Group) {
        $byId.Add($row.ScenarioId, $row)
        foreach ($column in $timings) { $null = Read-Number $row $column $false }
        foreach ($column in $bytes) { $null = Read-Number $row $column $true }
    }
    foreach ($key in $headline.Keys) {
        if (!$byId.ContainsKey($key)) { throw "Missing scenario $key on .NET $($group.Name)" }
        $row = $byId[$key]
        $write = Format-Pair (Read-Number $row $timings[0] $false) (Read-Number $row $timings[1] $false) 'N1'
        $read = Format-Pair (Read-Number $row $timings[2] $false) (Read-Number $row $timings[3] $false) 'N1'
        $output = Format-Pair (Read-Number $row $bytes[0] $true) (Read-Number $row $bytes[1] $true) 'N0'
        $writeAlloc = Format-Pair (Read-Number $row $bytes[2] $true) (Read-Number $row $bytes[3] $true) 'N0'
        $readAlloc = Format-Pair (Read-Number $row $bytes[4] $true) (Read-Number $row $bytes[5] $true) 'N0'
        "| .NET $($group.Name) | $($headline[$key]) | $write | $read | $output | $writeAlloc | $readAlloc |"
    }
}

$begin = '<!-- benchmark-results:begin -->'
$end = '<!-- benchmark-results:end -->'
$section = @(
    $begin
    '## Selected benchmark measurements'
    ''
    'Each cell shows **ApexPack / MemoryPack**. Times and memory allocations are per operation.'
    ''
    '| Runtime | Scenario | Write time (ns) | Read time (ns) | Output (bytes) | Write allocation (bytes) | Read allocation (bytes) |'
    '| --- | --- | ---: | ---: | ---: | ---: | ---: |'
    $table
    ''
    '[All raw measurements (CSV)](docs/data/runtime-benchmarks.csv) · [How the benchmarks were run](docs/performance.md#how-the-benchmarks-were-run)'
    $end
) -join "`n"

$readmePath = Join-Path $Root 'README.md'
$readme = [IO.File]::ReadAllText($readmePath)
$pattern = [regex]::Escape($begin) + '[\s\S]*?' + [regex]::Escape($end)
if ([regex]::Matches($readme, $pattern).Count -eq 1) {
    $readme = [regex]::Replace($readme, $pattern, { param($match) $section })
} elseif (!$readme.Contains($begin) -and !$readme.Contains($end) -and $readme.Contains('## Publish the documentation')) {
    $readme = $readme.Replace('## Publish the documentation', "$section`n`n## Publish the documentation")
} else {
    throw 'The README has no unique benchmark section.'
}
Copy-Item -LiteralPath $ComparisonCsv -Destination (Join-Path $Root 'docs/data/runtime-benchmarks.csv')
[IO.File]::WriteAllText($readmePath, $readme)
