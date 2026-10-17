param(
    [string]$SvgRevision = '5a8b6115d1c0cf56f2ebaac1a96fa5ae3074d178',
    [string]$MetadataRevision = 'ef62766bbd7ffb294c7184f9b8bfe8ed0f18320e'
)
$ErrorActionPreference = 'Stop'
$destination = Join-Path $PSScriptRoot '../wwwroot/maps'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$metadataUrl = "https://raw.githubusercontent.com/the-hideout/tarkov-dev/$MetadataRevision/src/data/maps.json"
$metadata = Invoke-RestMethod $metadataUrl
$ids = @{
    'customs' = @('bigmap'); 'factory' = @('factory4_day','factory4_night');
    'ground-zero' = @('sandbox','sandbox_high'); 'interchange' = @('interchange');
    'lighthouse' = @('lighthouse'); 'reserve' = @('rezervbase');
    'shoreline' = @('shoreline'); 'streets-of-tarkov' = @('tarkovstreets'); 'woods' = @('woods')
}
function Extent($bounds) {
    $xs = @([double]$bounds[0][0], [double]$bounds[1][0])
    $zs = @([double]$bounds[0][1], [double]$bounds[1][1])
    return @{MinX=($xs | Measure-Object -Minimum).Minimum; MinZ=($zs | Measure-Object -Minimum).Minimum; MaxX=($xs | Measure-Object -Maximum).Maximum; MaxZ=($zs | Measure-Object -Maximum).Maximum}
}
function Volume($bounds, $height) {
    $e = Extent $bounds
    return @{MinX=$e.MinX; MinZ=$e.MinZ; MaxX=$e.MaxX; MaxZ=$e.MaxZ; MinHeight=[double]$height[0]; MaxHeight=[double]$height[1]}
}
$index = @()
$provenance = @()
foreach ($entry in $metadata) {
    if (-not $ids.ContainsKey($entry.normalizedName)) { continue }
    $map = $entry.maps | Where-Object svgPath | Select-Object -First 1
    if (-not $map) { continue }
    $key = $entry.normalizedName
    $folder = Join-Path $destination $key
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $file = [IO.Path]::GetFileName(([uri]$map.svgPath).AbsolutePath)
    $sourceUrl = "https://raw.githubusercontent.com/the-hideout/tarkov-dev-svg-maps/$SvgRevision/$file"
    $source = (Invoke-WebRequest $sourceUrl).Content
    $original = [xml]$source
    $bounds = Extent $(if ($map.svgBounds) {$map.svgBounds} else {$map.bounds})
    $baseName = $map.svgLayer
    $definitions = @(@{name='Ground level'; svgLayer=$baseName; heightRange=$map.heightRange; extents=@(); level=0})
    $next = 1
    foreach ($layer in $map.layers) {
        if (-not $layer.svgLayer) { continue }
        $level = if($layer.name -match 'Underground|Bunker|Basement|Tunnel|Technical'){-1}else{$next}
        if($level -ge 0){$next++}
        $definitions += @{name=$layer.name; svgLayer=$layer.svgLayer; heightRange=$layer.heightRange; extents=$layer.extents; level=$level}
    }
    $floors = @()
    foreach ($layer in $definitions) {
        $group = $original.DocumentElement.ChildNodes | Where-Object { $_.LocalName -eq 'g' -and $_.id -eq $layer.svgLayer } | Select-Object -First 1
        if (-not $group) { continue }
        $svg = [xml]$source
        foreach ($node in @($svg.DocumentElement.ChildNodes)) {
            if ($node.LocalName -ne 'g') { continue }
            if ($node.id -eq $layer.svgLayer) { $node.RemoveAttribute('display'); $node.SetAttribute('style','display:inline') }
            elseif ($node.id -eq $baseName) { $node.SetAttribute('style','display:inline;opacity:0.18') }
            else { [void]$svg.DocumentElement.RemoveChild($node) }
        }
        $svg.Save((Join-Path $folder ($layer.svgLayer+'.svg')))
        $volumes = @()
        foreach ($ext in $layer.extents) {
            $height = $(if($ext.height){$ext.height}else{@(-1000,1000)})
            if ($ext.bounds) { foreach($b in $ext.bounds) { $volumes += Volume $b $height } }
            else { $volumes += Volume $map.bounds $height }
        }
        if($volumes.Count -eq 0){$volumes += Volume $map.bounds $(if($layer.heightRange){$layer.heightRange}else{@(-1000,1000)})}
        $floors += @{Name=$layer.name; Level=$layer.level; Bounds=$bounds; ImagePath="$key/$($layer.svgLayer).svg"; Volumes=$volumes}
    }
    $labels = @($map.labels | Where-Object {$_.position -and $_.text} | ForEach-Object {@{Name=$_.text; X=[double]$_.position[0]; Z=[double]$_.position[1]}})
    $index += @{Id=$key; Name=$key; Author=$map.author; Maps=$ids[$key]; Rotation=$map.coordinateRotation; DefaultLevel=0; Floors=$floors; Landmarks=$labels}
    $provenance += @{Map=$key; Author=$map.author; AuthorLink=$map.authorLink; Source=$sourceUrl; SvgRevision=$SvgRevision; MetadataSource=$metadataUrl; Changes='Extracted floor groups; retained a faint ground layer behind upper and underground floors. Artwork remains CC BY-NC-SA 4.0.'}
}
$index | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $destination 'index.json') -Encoding utf8
$provenance | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $destination 'provenance.json') -Encoding utf8
Invoke-WebRequest "https://raw.githubusercontent.com/the-hideout/tarkov-dev-svg-maps/$SvgRevision/LICENSE.md" -OutFile (Join-Path $destination 'LICENSE-CC-BY-NC-SA-4.0.md')
Invoke-WebRequest "https://raw.githubusercontent.com/the-hideout/tarkov-dev-svg-maps/$SvgRevision/README.md" -OutFile (Join-Path $destination 'UPSTREAM-README.md')
Invoke-WebRequest "https://raw.githubusercontent.com/the-hideout/tarkov-dev/$MetadataRevision/LICENSE" -OutFile (Join-Path $destination 'LICENSE-metadata-MIT.txt')
Write-Output "Imported $($index.Count) Tarkov.dev maps."
