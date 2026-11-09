#requires -Version 7.6
# Samsung-profile repair for an explicitly audited MP4 manifest. Records full verification evidence.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Directory,
    [Parameter(Mandatory)][string]$Manifest,
    [Parameter(Mandatory)][string]$FFmpeg,
    [Parameter(Mandatory)][string]$FFprobe,
    [Parameter(Mandatory)][string]$CoreAssembly,
    [Parameter(Mandatory)][string]$ReportDirectory,
    [ValidateRange(1,100000)][int]$Limit = 100000
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($Directory).TrimEnd('\','/')
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Repair folder does not exist.' }
if ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Repair root cannot be a reparse point.' }
$FFmpeg = (Resolve-Path -LiteralPath $FFmpeg).Path
$FFprobe = (Resolve-Path -LiteralPath $FFprobe).Path
$null = [Reflection.Assembly]::LoadFrom((Resolve-Path -LiteralPath $CoreAssembly).Path)
$report = [IO.Path]::GetFullPath($ReportDirectory)
[IO.Directory]::CreateDirectory($report) | Out-Null
$journal = Join-Path $report 'journal.jsonl'
function Record($value) {
    $value | ConvertTo-Json -Depth 16 -Compress | Add-Content -LiteralPath $journal -Encoding utf8
}
function Run([string]$exe, [string[]]$options) {
    $process = [Diagnostics.Process]::new()
    try {
        $process.StartInfo = [Diagnostics.ProcessStartInfo]::new($exe)
        $process.StartInfo.UseShellExecute = $false
        $process.StartInfo.CreateNoWindow = $true
        $process.StartInfo.RedirectStandardOutput = $true
        $process.StartInfo.RedirectStandardError = $true
        foreach ($option in $options) { $process.StartInfo.ArgumentList.Add($option) }
        $process.Start() | Out-Null
        $output = $process.StandardOutput.ReadToEndAsync()
        $errorText = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(600000)) { $process.Kill($true); $process.WaitForExit(); throw 'Metadata tool timed out.' }
        $stdout = $output.GetAwaiter().GetResult()
        $stderr = $errorText.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw ($exe + ': ' + $stderr.Substring(0,[Math]::Min(2000,$stderr.Length))) }
        return $stdout
    }
    finally {
        if ($process.Id -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
}
function Probe([string]$file) {
    $json = Run $FFprobe @('-v','error','-show_data_hash','sha256','-show_entries','format=duration:format_tags:stream=index,codec_name,codec_type,profile,width,height,pix_fmt,r_frame_rate,avg_frame_rate,sample_rate,channels,channel_layout,time_base,start_time,duration,nb_frames,extradata_hash:stream_tags','-of','json',$file)
    return ($json | ConvertFrom-Json -DateKind String)
}
function HashStreams([string]$file) {
    return (Run $FFmpeg @('-nostdin','-v','error','-i',$file,'-map','0:v?','-map','0:a?','-c','copy','-f','streamhash','-hash','sha256','-')).Trim()
}
function CheckMetadata($data,[string]$title) {
    $tags = $data.format.tags
    foreach ($pair in @(@('make','Samsung'),@('model','SM-S928B'),@('software','Android 14'),@('lens_model','Galaxy S24 Ultra Main Camera 24mm f/1.7'),@('focal_length','24mm'),@('f_number','1.7'),@('title',$title))) {
        if ($tags.($pair[0]) -cne $pair[1]) { throw ('Metadata verification failed: '+$pair[0]) }
    }
}
function CheckMedia($before,$after,[string]$source,[string]$candidate) {
    if ($before.streams.Count -ne $after.streams.Count) { throw 'Stream count changed.' }
    foreach ($i in 0..($before.streams.Count-1)) {
        foreach ($property in @('codec_name','codec_type','profile','width','height','pix_fmt','r_frame_rate','avg_frame_rate','sample_rate','channels','channel_layout','time_base','extradata_hash')) {
            if ([string]$before.streams[$i].$property -cne [string]$after.streams[$i].$property) { throw "Media property changed: stream $i / $property" }
        }
        if ($before.streams[$i].nb_frames -ne $after.streams[$i].nb_frames) {
            # Some original sample tables count an empty AAC sample that the
            # demuxer never emits. Verify real packet counts, not that header estimate.
            $countBefore = (Run $FFprobe @('-v','error','-count_packets','-show_entries','stream=index,nb_read_packets','-of','json',$source)) | ConvertFrom-Json -DateKind String
            $countAfter = (Run $FFprobe @('-v','error','-count_packets','-show_entries','stream=index,nb_read_packets','-of','json',$candidate)) | ConvertFrom-Json -DateKind String
            if ($countBefore.streams[$i].nb_read_packets -ne $countAfter.streams[$i].nb_read_packets) { throw 'Actual packet count changed.' }
            Record @{ State='HeaderCountNormalized'; File=$source; Stream=$i; OriginalHeader=$before.streams[$i].nb_frames; NewHeader=$after.streams[$i].nb_frames; Packets=$countAfter.streams[$i].nb_read_packets }
        }
        foreach ($property in @('start_time','duration')) {
            $a = [double]::Parse($(if($before.streams[$i].$property){$before.streams[$i].$property}else{'0'}),[Globalization.CultureInfo]::InvariantCulture)
            $b = [double]::Parse($(if($after.streams[$i].$property){$after.streams[$i].$property}else{'0'}),[Globalization.CultureInfo]::InvariantCulture)
            if ([Math]::Abs($a-$b) -gt 0.001) { throw "Timing changed: stream $i / $property" }
        }
    }
    if ([Math]::Abs([double]::Parse($before.format.duration,[Globalization.CultureInfo]::InvariantCulture)-[double]::Parse($after.format.duration,[Globalization.CultureInfo]::InvariantCulture)) -gt 0.001) { throw 'Container duration changed.' }
}
$origins = @{}
foreach ($entry in (Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json -DateKind String)) { $origins[[IO.Path]::GetFullPath($entry.File)] = $entry }
$targets = @($origins.Keys | Sort-Object)
$completed = @{}
if (Test-Path -LiteralPath $journal) {
    foreach ($line in Get-Content -LiteralPath $journal) {
        $entry = $line | ConvertFrom-Json -DateKind String
        if ($entry.State -eq 'Completed' -and $entry.RepairVersion -eq 2) { $completed[$entry.File] = $entry }
    }
}
$changed = 0; $skipped = 0
foreach ($path in $targets) {
    $full = [IO.Path]::GetFullPath($path)
    # Exact audited folder only; never recurse or follow a link outside it.
    if (-not [string]::Equals([IO.Path]::GetDirectoryName($full),$root,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetExtension($full) -ine '.mp4') { throw 'Manifest target is outside the allowed folder.' }
    $title = [IO.Path]::GetFileNameWithoutExtension($full)
    $origin = $origins[$full]
    if ($completed.ContainsKey($full)) {
        CheckMetadata (Probe $full) $title
        $skipped++; continue
    }
    if ($changed -ge $Limit) { break }
    $guard = $null; $replaced = $false
    $temporary = $full + '.metadata-rewrite-v2.tmp.mp4'
    $backup = $full + '.metadata-rollback'
    if ([IO.File]::Exists($temporary) -or [IO.File]::Exists($backup)) { throw "Unfinished prior repair requires review: $full" }
    try {
        $info = Get-Item -LiteralPath $full
        if ($info.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Target is a reparse point.' }
        $length = $info.Length; $modified = $info.LastWriteTimeUtc; $created = $info.CreationTimeUtc
        # Deny other writers/deletes throughout remuxing and verification.
        $guard = [IO.File]::Open($full,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        $before = Probe $full
        $beforeHash = HashStreams $full
        Record @{ State='Before'; File=$full; Bytes=$length; ModifiedUtc=$modified; CreatedUtc=$created; StreamsHash=$beforeHash; Metadata=$before }
        $camera = [_855Media.Core.Editing.CameraMetadataSettings]::new()
        $camera.ProfileType = [_855Media.Core.Editing.CameraProfileType]::SamsungGalaxyS24
        $camera.InjectCurrentTimestamp = $false
        $args = @('-nostdin','-hide_banner','-v','error','-n','-i',$full,'-map','0','-c','copy') + @($camera.BuildOutputMetadataArgs($full,$title))
        if ($origin.Format.tags.creation_time) { $args += @('-metadata',('creation_time='+$origin.Format.tags.creation_time)) }
        foreach ($stream in $before.streams) {
            $originalStream = $origin.Streams[$stream.index]
            foreach ($property in @('creation_time','language','encoder')) {
                if ($originalStream.tags.$property) { $args += @(('-metadata:s:'+$stream.index),($property+'='+$originalStream.tags.$property)) }
            }
        }
        $args += $temporary
        $null = Run $FFmpeg $args
        $after = Probe $temporary
        CheckMetadata $after $title
        CheckMedia $before $after $full $temporary
        if ($origin.Format.tags.creation_time -cne $after.format.tags.creation_time) { throw 'Original creation tag changed.' }
        foreach ($stream in $after.streams) {
            $originalTime = $origin.Streams[$stream.index].tags.creation_time
            if ($originalTime -and (!$stream.tags.creation_time -or [DateTimeOffset]::Parse($originalTime,[Globalization.CultureInfo]::InvariantCulture) -ne [DateTimeOffset]::Parse($stream.tags.creation_time,[Globalization.CultureInfo]::InvariantCulture))) { throw 'Original stream creation time changed.' }
        }
        $afterHash = HashStreams $temporary
        if ($beforeHash -cne $afterHash) { throw 'Compressed video/audio hashes changed; original retained.' }
        Record @{ State='VerifiedCandidate'; File=$full; StreamsHash=$afterHash; Metadata=$after }
        $guard.Dispose(); $guard = $null
        $current = Get-Item -LiteralPath $full
        if ($current.Length -ne $length -or $current.LastWriteTimeUtc -ne $modified) { throw 'Original changed during repair.' }
        [IO.File]::Replace($temporary,$full,$backup)
        $replaced = $true
        CheckMetadata (Probe $full) $title
        [IO.File]::SetCreationTimeUtc($full,$created)
        [IO.File]::SetLastWriteTimeUtc($full,$modified)
        # Release only this verified file's rollback copy; peak disk use stays bounded.
        [IO.File]::Delete($backup)
        $changed++
        Record @{ State='Completed'; RepairVersion=2; File=$full; Bytes=[IO.FileInfo]::new($full).Length; StreamsHash=$afterHash; TimeUtc=[DateTime]::UtcNow }
        Write-Output ("Repaired {0}/{1}: {2}" -f ($changed+$skipped),$targets.Count,[IO.Path]::GetFileName($full))
        @{ Repaired=$changed; AlreadyCompleted=$skipped; Total=$targets.Count; Last=$full; TimeUtc=[DateTime]::UtcNow } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $report 'progress.json')
    }
    catch {
        if ($guard) { $guard.Dispose(); $guard = $null }
        if ($replaced -and [IO.File]::Exists($backup)) {
            [IO.File]::Replace($backup,$full,$temporary)
            [IO.File]::SetCreationTimeUtc($full,$created)
            [IO.File]::SetLastWriteTimeUtc($full,$modified)
        }
        Record @{ State='Failed'; File=$full; Error=$_.Exception.Message; TimeUtc=[DateTime]::UtcNow }
        throw
    }
    finally { if ($guard) { $guard.Dispose() } }
}
@{ Repaired=$changed; AlreadyCompleted=$skipped; Total=$targets.Count; FinishedUtc=[DateTime]::UtcNow } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $report 'summary.json')
