$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$base = Split-Path -Parent $MyInvocation.MyCommand.Path
$native = Join-Path $base "native"
$temp = Join-Path $env:TEMP ("osu-beatmap-merger-native-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $native | Out-Null
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    # These match the memory-reader versions used by FunOrange osu-trainer's current source.
    $packages = @(
        @{
            Name = "ProcessMemoryDataFinder"
            Version = "0.8.5"
            Url = "https://api.nuget.org/v3-flatcontainer/processmemorydatafinder/0.8.5/processmemorydatafinder.0.8.5.nupkg"
        },
        @{
            Name = "OsuMemoryDataProvider"
            Version = "0.10.3"
            Url = "https://api.nuget.org/v3-flatcontainer/osumemorydataprovider/0.10.3/osumemorydataprovider.0.10.3.nupkg"
        }
    )

    foreach ($pkg in $packages) {
        Write-Host ("Downloading " + $pkg.Name + " " + $pkg.Version + "...")
        $zip = Join-Path $temp ($pkg.Name + ".zip")
        $out = Join-Path $temp $pkg.Name
        Invoke-WebRequest -UseBasicParsing -Uri $pkg.Url -OutFile $zip
        Expand-Archive -LiteralPath $zip -DestinationPath $out -Force

        $preferred = Get-ChildItem -Path (Join-Path $out "lib") -Recurse -Filter ($pkg.Name + ".dll") |
            Where-Object { $_.FullName -match "net472|net48|net471" } |
            Select-Object -First 1
        if ($null -eq $preferred) {
            $preferred = Get-ChildItem -Path (Join-Path $out "lib") -Recurse -Filter ($pkg.Name + ".dll") | Select-Object -First 1
        }
        if ($null -eq $preferred) {
            throw ("Could not find " + $pkg.Name + ".dll inside the NuGet package.")
        }
        Copy-Item -Force $preferred.FullName (Join-Path $native ($pkg.Name + ".dll"))
    }

    Write-Host "Native osu! song-select reader installed."
}
finally {
    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
}
