$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

function Emit-Result($obj) {
    $obj | ConvertTo-Json -Compress -Depth 8
}

function Get-TypeListSafe([System.Reflection.Assembly]$asm) {
    try {
        return @($asm.GetTypes())
    }
    catch [System.Reflection.ReflectionTypeLoadException] {
        return @($_.Exception.Types | Where-Object { $null -ne $_ })
    }
}

function Get-ConstructorSummary([Type]$type) {
    $parts = @()
    foreach ($ctor in $type.GetConstructors([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance)) {
        $ps = @()
        foreach ($p in $ctor.GetParameters()) {
            $suffix = if ($p.IsOptional) { "=?" } else { "" }
            $ps += ($p.ParameterType.FullName + " " + $p.Name + $suffix)
        }
        $visibility = if ($ctor.IsPublic) { "public" } else { "nonpublic" }
        $parts += ($visibility + " " + $type.FullName + "(" + ($ps -join ", ") + ")")
    }
    if ($parts.Count -eq 0) { return "(no constructors reported)" }
    return ($parts -join "; ")
}

function New-ProcessTargetOptions([Type]$targetType) {
    # Current ProcessMemoryDataFinder versions expose a ctor beginning with the process name.
    # Build it only through reflection so this helper also works with older package revisions.
    foreach ($ctor in $targetType.GetConstructors()) {
        $params = $ctor.GetParameters()
        if ($params.Count -lt 1) { continue }
        if ($params[0].ParameterType -ne [string]) { continue }

        $args = New-Object object[] $params.Count
        $args[0] = "osu!"
        $usable = $true

        for ($i = 1; $i -lt $params.Count; $i++) {
            $p = $params[$i]
            if ($p.IsOptional) {
                $args[$i] = $p.DefaultValue
            }
            elseif ($p.ParameterType -eq [string]) {
                $args[$i] = $null
            }
            elseif ($p.ParameterType -eq [bool]) {
                $args[$i] = $false
            }
            elseif ($p.ParameterType.IsValueType) {
                try { $args[$i] = [Activator]::CreateInstance($p.ParameterType) }
                catch { $usable = $false; break }
            }
            else {
                $args[$i] = $null
            }
        }

        if ($usable) {
            try {
                $obj = $ctor.Invoke($args)
                if ($null -ne $obj) { return $obj }
            }
            catch {}
        }
    }
    return $null
}

function New-ReaderViaReflection([Type]$readerType) {
    # Some provider versions expose a singleton. Prefer it because it also knows how to
    # create the correct ProcessTargetOptions object for that version.
    try {
        $flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static
        $instanceProp = $readerType.GetProperty("Instance", $flags)
        if ($null -ne $instanceProp) {
            $instance = $instanceProp.GetValue($null, $null)
            if ($null -ne $instance) {
                return [pscustomobject]@{ Reader = $instance; Init = "static-instance" }
            }
        }
    }
    catch {}

    # FunOrange's 0.10.3 build used a parameterless constructor.
    try {
        $ctor0 = $readerType.GetConstructor([Type]::EmptyTypes)
        if ($null -ne $ctor0) {
            $instance = $ctor0.Invoke(@())
            if ($null -ne $instance) {
                return [pscustomobject]@{ Reader = $instance; Init = "parameterless-reflection" }
            }
        }
    }
    catch {}

    # Newer versions accept ProcessTargetOptions instead.
    foreach ($ctor in $readerType.GetConstructors()) {
        $params = $ctor.GetParameters()
        $args = New-Object object[] $params.Count
        $usable = $true

        for ($i = 0; $i -lt $params.Count; $i++) {
            $p = $params[$i]
            if ($p.ParameterType.Name -eq "ProcessTargetOptions" -or $p.ParameterType.FullName -like "*ProcessTargetOptions") {
                $opt = New-ProcessTargetOptions $p.ParameterType
                if ($null -eq $opt) { $usable = $false; break }
                $args[$i] = $opt
            }
            elseif ($p.ParameterType -eq [string] -and ($p.Name -match "process|name")) {
                $args[$i] = "osu!"
            }
            elseif ($p.IsOptional) {
                $args[$i] = $p.DefaultValue
            }
            else {
                $usable = $false
                break
            }
        }

        if ($usable) {
            try {
                $instance = $ctor.Invoke($args)
                if ($null -ne $instance) {
                    return [pscustomobject]@{ Reader = $instance; Init = "constructor-reflection" }
                }
            }
            catch {}
        }
    }

    return $null
}

function Invoke-GenericTryRead($reader, $readObject) {
    # TryRead<T>(T obj) is generic. PowerShell 5.1 does not reliably infer generic
    # type arguments, so construct the closed method explicitly.
    $readerType = $reader.GetType()
    $methods = @($readerType.GetMethods() | Where-Object {
        $_.Name -eq "TryRead" -and $_.IsGenericMethodDefinition -and $_.GetGenericArguments().Count -eq 1 -and $_.GetParameters().Count -eq 1
    })

    if ($methods.Count -gt 0) {
        $closed = $methods[0].MakeGenericMethod(@($readObject.GetType()))
        return [bool]$closed.Invoke($reader, @($readObject))
    }

    # Compatibility fallback in case a provider revision exposes a non-generic TryRead.
    $nonGeneric = @($readerType.GetMethods() | Where-Object {
        $_.Name -eq "TryRead" -and -not $_.IsGenericMethodDefinition -and $_.GetParameters().Count -eq 1
    } | Select-Object -First 1)
    if ($nonGeneric.Count -gt 0) {
        return [bool]$nonGeneric[0].Invoke($reader, @($readObject))
    }

    throw "StructuredOsuMemoryReader has no compatible TryRead method."
}

try {
    $base = Split-Path -Parent $MyInvocation.MyCommand.Path
    $native = Join-Path $base "native"
    $finder = Join-Path $native "ProcessMemoryDataFinder.dll"
    $provider = Join-Path $native "OsuMemoryDataProvider.dll"

    if (-not (Test-Path $finder) -or -not (Test-Path $provider)) {
        throw "Native reader DLLs are missing. Run setup.bat first."
    }

    # IMPORTANT: keep the returned Assembly objects. Type.GetType("..., AssemblyName")
    # can fail to resolve LoadFrom-context assemblies in Windows PowerShell even though
    # the DLL loaded successfully. Resolve the class from the exact loaded assembly instead.
    $finderAsm = [System.Reflection.Assembly]::LoadFrom($finder)
    $providerAsm = [System.Reflection.Assembly]::LoadFrom($provider)

    $proc = Get-Process -Name "osu!" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $proc) {
        throw "osu!stable is not running. Open osu!, go to song select, highlight a difficulty, then try again."
    }

    $readerType = $providerAsm.GetType("OsuMemoryDataProvider.StructuredOsuMemoryReader", $false, $false)

    # If a package revision ever moves the namespace, locate by class name inside the
    # already-loaded provider assembly rather than asking the runtime to bind it again.
    if ($null -eq $readerType) {
        $matches = @(Get-TypeListSafe $providerAsm | Where-Object { $_.Name -eq "StructuredOsuMemoryReader" })
        if ($matches.Count -gt 0) { $readerType = $matches[0] }
    }

    if ($null -eq $readerType) {
        $available = @(Get-TypeListSafe $providerAsm | Where-Object { $_.FullName -like "*Osu*Memory*Reader*" } | ForEach-Object { $_.FullName })
        throw ("OsuMemoryDataProvider loaded from '" + $providerAsm.Location + "', but StructuredOsuMemoryReader was not present. Candidate reader types: " + ($available -join ", "))
    }

    $created = New-ReaderViaReflection $readerType
    if ($null -eq $created -or $null -eq $created.Reader) {
        throw ("Could not create " + $readerType.FullName + ". Constructors: " + (Get-ConstructorSummary $readerType))
    }

    $reader = $created.Reader
    $readerInit = $created.Init

    $addressesProp = $readerType.GetProperty("OsuMemoryAddresses")
    if ($null -eq $addressesProp) { throw "Reader has no OsuMemoryAddresses property." }
    $addresses = $addressesProp.GetValue($reader, $null)
    if ($null -eq $addresses) { throw "OsuMemoryAddresses returned null." }

    $beatmapProp = $addresses.GetType().GetProperty("Beatmap")
    if ($null -eq $beatmapProp) { throw "OsuMemoryAddresses has no Beatmap property." }
    $beatmap = $beatmapProp.GetValue($addresses, $null)
    if ($null -eq $beatmap) { throw "Beatmap memory model returned null." }

    $readOk = Invoke-GenericTryRead $reader $beatmap

    $osuFileName = [string]$beatmap.OsuFileName
    $folderName = [string]$beatmap.FolderName

    if ([string]::IsNullOrWhiteSpace($osuFileName)) {
        throw "osu! was detected, but no selected difficulty was returned. Be on song select with a difficulty highlighted."
    }

    $processPath = $null
    try { $processPath = [string]$proc.Path } catch {}
    if ([string]::IsNullOrWhiteSpace($processPath)) {
        try { $processPath = [string]$proc.MainModule.FileName } catch {}
    }

    Emit-Result ([ordered]@{
        ok = $true
        readOk = [bool]$readOk
        readerType = $readerType.FullName
        readerInit = $readerInit
        providerAssembly = $providerAsm.FullName
        processPath = $processPath
        osuFileName = $osuFileName
        folderName = $folderName
        pid = [int]$proc.Id
    })
}
catch {
    Emit-Result ([ordered]@{
        ok = $false
        error = $_.Exception.Message
        exceptionType = $_.Exception.GetType().FullName
    })
    exit 1
}
