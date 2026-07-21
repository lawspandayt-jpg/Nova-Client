# Builds client-java into dist\nova-client.jar (Java 8 bytecode).
# Dependencies are downloaded from Mojang's own library server (libraries.minecraft.net) —
# the same host the game itself uses. Requires any JDK >= 8 on PATH (or JAVA_HOME).

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$libDir = Join-Path $root "lib"
$outDir = Join-Path $root "out"
$distDir = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $libDir, $distDir | Out-Null
if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$deps = @(
    "https://libraries.minecraft.net/org/lwjgl/lwjgl/lwjgl/2.9.4-nightly-20150209/lwjgl-2.9.4-nightly-20150209.jar",
    "https://libraries.minecraft.net/net/minecraft/launchwrapper/1.12/launchwrapper-1.12.jar",
    "https://repo1.maven.org/maven2/org/ow2/asm/asm-debug-all/5.0.3/asm-debug-all-5.0.3.jar",
    "https://libraries.minecraft.net/com/google/code/gson/gson/2.2.4/gson-2.2.4.jar"
)
foreach ($url in $deps) {
    $file = Join-Path $libDir ([System.IO.Path]::GetFileName($url))
    if (-not (Test-Path $file)) {
        Write-Host "Downloading $([System.IO.Path]::GetFileName($url))..."
        Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing
    }
}

# Locate javac / jar
$javac = $null
if ($env:JAVA_HOME) { $candidate = Join-Path $env:JAVA_HOME "bin\javac.exe"; if (Test-Path $candidate) { $javac = $candidate } }
if (-not $javac) { $javac = (Get-Command javac -ErrorAction Stop).Source }
$jarTool = Join-Path (Split-Path $javac) "jar.exe"

$classpath = (Get-ChildItem $libDir -Filter *.jar | ForEach-Object { $_.FullName }) -join ";"
$sources = Get-ChildItem -Recurse (Join-Path $root "src") -Filter *.java | ForEach-Object { $_.FullName }
$sourceList = Join-Path $outDir "sources.txt"
$sources | ForEach-Object { '"' + ($_ -replace '\\', '\\') + '"' } | Set-Content -Path $sourceList -Encoding ascii

Write-Host "Compiling $($sources.Count) sources with --release 8..."
& $javac --release 8 -encoding UTF-8 -nowarn -cp $classpath -d $outDir "@$sourceList"
if ($LASTEXITCODE -ne 0) { throw "javac failed with exit code $LASTEXITCODE" }
Remove-Item $sourceList

# Bundle the Nova logo (used for the in-game window icon)
$logo = Join-Path $root "..\src\NovaClient.Launcher\Assets\logo.png"
if (Test-Path $logo) { Copy-Item $logo (Join-Path $outDir "dev\novaclient\logo.png") -Force }

# Shade ASM into the agent jar so title branding works on modern versions too
# (their classpath has no ASM; on 1.8.9/Fabric the classpath copy simply wins). BSD-3 licensed,
# notice in docs/THIRD-PARTY-NOTICES.md.
$asmJar = Get-ChildItem $libDir -Filter "asm-debug-all-*.jar" | Select-Object -First 1
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($asmJar.FullName)
foreach ($entry in $zip.Entries) {
    if ($entry.FullName.StartsWith("org/") -and $entry.FullName.EndsWith(".class")) {
        $target = Join-Path $outDir ($entry.FullName -replace '/', '\')
        New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
    }
}
$zip.Dispose()

$manifest = Join-Path $outDir "MANIFEST.MF"
@"
Premain-Class: dev.novaclient.bootstrap.NovaAgent
Can-Redefine-Classes: false
Can-Retransform-Classes: false
Implementation-Title: Nova Client
Implementation-Version: 1.0.0
"@ | Set-Content -Path $manifest -Encoding ascii

$jarPath = Join-Path $distDir "nova-client.jar"
if (Test-Path $jarPath) { Remove-Item $jarPath }
& $jarTool cfm $jarPath $manifest -C $outDir dev -C $outDir org
if ($LASTEXITCODE -ne 0) { throw "jar failed with exit code $LASTEXITCODE" }

Write-Host "Built $jarPath"
