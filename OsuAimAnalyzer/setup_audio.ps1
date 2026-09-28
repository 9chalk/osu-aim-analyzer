$ErrorActionPreference = 'Stop'
$runtimeDirectory = Join-Path $PSScriptRoot 'tools/ffmpeg'
$archivePath = Join-Path ([IO.Path]::GetTempPath()) ('aim-ffmpeg-' + [guid]::NewGuid().ToString('N') + '.zip')
$downloadUrl = 'https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.zip'
$expectedHash = '60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba'
try {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath
    if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'FFmpeg archive checksum mismatch. No runtime was installed.'
    }
    New-Item -ItemType Directory -Path $runtimeDirectory -Force | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        foreach ($name in @('ffmpeg.exe', 'ffprobe.exe', 'LICENSE', 'README.txt')) {
            $entries = @($archive.Entries | Where-Object { $_.Name -eq $name })
            if ($entries.Count -ne 1) { throw "Expected one $name in the verified archive." }
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entries[0], (Join-Path $runtimeDirectory $name), $true)
        }
    } finally { $archive.Dispose() }
    @{
        Version = '9.0.2-essentials_build'; ArchiveUrl = $downloadUrl; ArchiveSha256 = $expectedHash
        SourceUrl = 'https://github.com/FFmpeg/FFmpeg/commit/946fcce07b'
    } | ConvertTo-Json | Set-Content (Join-Path $runtimeDirectory 'distribution.json')
    Write-Output "Verified audio runtime installed in $runtimeDirectory"
} finally {
    if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath }
}
