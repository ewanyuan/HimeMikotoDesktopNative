param(
    [string]$Source,
    [string]$Destination,
    [int]$HalfCycles = 8
)

$ErrorActionPreference = "Stop"
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Definition
$Source = if ([string]::IsNullOrWhiteSpace($Source)) {
    Join-Path $scriptDirectory "../assets/motions/waist-dance/waist-dance-ik.vmd"
} else {
    $Source
}
$Destination = if ([string]::IsNullOrWhiteSpace($Destination)) {
    Join-Path $scriptDirectory "../assets/motions/waist-dance/waist-dance-loop.vmd"
} else {
    $Destination
}

if ($HalfCycles -lt 2 -or ($HalfCycles % 2) -ne 0) {
    throw "HalfCycles must be an even number of at least 2 so the generated motion ends in the starting pose."
}

$sourceBytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Source))
$headerLength = 30
$modelNameLength = 20
$boneCountOffset = 50
$boneRecordLength = 111

if ($sourceBytes.Length -lt ($boneCountOffset + 4)) {
    throw "The source file is too short to be a VMD file: $Source"
}

$sourceBoneCount = [BitConverter]::ToUInt32($sourceBytes, $boneCountOffset)
$sourceBonesOffset = $boneCountOffset + 4
$sourceBonesLength = [int64]$sourceBoneCount * $boneRecordLength
$tailOffset = $sourceBonesOffset + $sourceBonesLength
if ($tailOffset -gt $sourceBytes.Length) {
    throw "The source VMD bone table is truncated: $Source"
}

$sourceRecords = [Collections.Generic.List[byte[]]]::new()
$lastFrame = 0
for ($index = 0; $index -lt $sourceBoneCount; $index++) {
    $record = [byte[]]::new($boneRecordLength)
    [Array]::Copy($sourceBytes, $sourceBonesOffset + ($index * $boneRecordLength), $record, 0, $boneRecordLength)
    $sourceRecords.Add($record)
    $frame = [BitConverter]::ToUInt32($record, 15)
    $lastFrame = [Math]::Max($lastFrame, [int]$frame)
}

if ($lastFrame -le 0) {
    throw "The source VMD has no positive animation duration: $Source"
}

$generatedRecords = [Collections.Generic.List[byte[]]]::new($sourceRecords.Count * $HalfCycles)
for ($halfCycle = 0; $halfCycle -lt $HalfCycles; $halfCycle++) {
    $reverse = ($halfCycle % 2) -eq 1
    $timeOffset = $halfCycle * $lastFrame
    foreach ($sourceRecord in $sourceRecords) {
        $record = [byte[]]::new($boneRecordLength)
        [Array]::Copy($sourceRecord, $record, $boneRecordLength)
        $sourceFrame = [BitConverter]::ToUInt32($sourceRecord, 15)
        $relativeFrame = if ($reverse) { $lastFrame - $sourceFrame } else { $sourceFrame }
        $generatedFrame = [uint32]($timeOffset + $relativeFrame)
        [Array]::Copy([BitConverter]::GetBytes($generatedFrame), 0, $record, 15, 4)
        $generatedRecords.Add($record)
    }
}

$tailLength = $sourceBytes.Length - $tailOffset
$outputBytes = [byte[]]::new($sourceBonesOffset + ($generatedRecords.Count * $boneRecordLength) + $tailLength)
[Array]::Copy($sourceBytes, 0, $outputBytes, 0, $sourceBonesOffset)
[Array]::Copy([BitConverter]::GetBytes([uint32]$generatedRecords.Count), 0, $outputBytes, $boneCountOffset, 4)

$outputOffset = $sourceBonesOffset
foreach ($record in $generatedRecords) {
    [Array]::Copy($record, 0, $outputBytes, $outputOffset, $boneRecordLength)
    $outputOffset += $boneRecordLength
}

[Array]::Copy($sourceBytes, $tailOffset, $outputBytes, $outputOffset, $tailLength)
$destinationPath = [IO.Path]::GetFullPath($Destination)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destinationPath)) | Out-Null
[IO.File]::WriteAllBytes($destinationPath, $outputBytes)
Write-Output "Generated $destinationPath ($($generatedRecords.Count) bone records, $lastFrame frames per half-cycle, $($HalfCycles * $lastFrame) frames total)."
