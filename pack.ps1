# Builds, packs and post-processes the NuGet package for release.
#
# The post-processing step exists because nuget.exe (observed on 7.6.0) writes
# `<Default Extension="png" ContentType="application/octet" />` into the OPC
# [Content_Types].xml. nuget.org then serves the embedded icon as
# application/octet-stream, and the XrmToolBox portal rejects the package with
# the misleading error "Logo Url is not valid". Rewriting the entry to
# image/png makes nuget.org serve a proper image content type.
#
# Usage:  powershell -ExecutionPolicy Bypass -File pack.ps1

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$nuget = 'C:\Users\CristianHernandez\nuget.exe'

# --- version lockstep check (portal rejects mismatches) ---
[xml]$csproj = Get-Content 'DataverseErdVisualizer.csproj'
[xml]$nuspec = Get-Content 'DataverseErdVisualizer.nuspec'
$asmVersion = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$pkgVersion = $nuspec.package.metadata.version
if ($asmVersion -ne $pkgVersion) {
    throw "Version mismatch: csproj=$asmVersion nuspec=$pkgVersion - keep them in lockstep."
}

dotnet build DataverseErdVisualizer.csproj -c Release -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

& $nuget pack DataverseErdVisualizer.nuspec -OutputDirectory dist
if ($LASTEXITCODE -ne 0) { throw 'Pack failed.' }

$nupkg = "dist\CasasHern.DataverseErdVisualizer.$pkgVersion.nupkg"

# --- fix the icon content type inside the OPC manifest ---
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::Open((Resolve-Path $nupkg), 'Update')
try {
    $entry = $zip.GetEntry('[Content_Types].xml')
    $reader = New-Object System.IO.StreamReader($entry.Open())
    $xml = $reader.ReadToEnd()
    $reader.Close()

    $fixed = $xml -replace 'Extension="png" ContentType="[^"]*"', 'Extension="png" ContentType="image/png"'
    if ($fixed -ne $xml) {
        $entry.Delete()
        $newEntry = $zip.CreateEntry('[Content_Types].xml')
        $writer = New-Object System.IO.StreamWriter($newEntry.Open())
        $writer.Write($fixed)
        $writer.Close()
        Write-Host 'Fixed png content type in [Content_Types].xml (application/octet -> image/png).'
    } else {
        Write-Host 'png content type already correct.'
    }
}
finally {
    $zip.Dispose()
}

# --- verify ---
$zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $nupkg))
try {
    $reader = New-Object System.IO.StreamReader($zip.GetEntry('[Content_Types].xml').Open())
    $check = $reader.ReadToEnd()
    $reader.Close()
    if ($check -notmatch 'Extension="png" ContentType="image/png"') {
        throw 'Verification failed: png content type is still wrong.'
    }
    Write-Host "OK: $nupkg is ready to push."
    $zip.Entries | Where-Object { $_.FullName -notmatch 'rels|psmdcp|Content_Types' } |
        ForEach-Object { Write-Host ("  " + $_.FullName) }
}
finally {
    $zip.Dispose()
}
