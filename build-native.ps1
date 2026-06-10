$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDirectory = Join-Path $projectRoot "dist"
$exeName = "skill" + [char]0x642C + [char]0x8FD0 + [char]0x5DE5 + ".exe"
$outputPath = Join-Path $outputDirectory $exeName
$nativeBuildDirectory = Join-Path $projectRoot ".native-build"
$nativeOutputPath = Join-Path $nativeBuildDirectory "SkillMover.exe"
$windowsDirectory = if ($env:WINDIR) { $env:WINDIR } else { "C:\Windows" }
$compiler = Join-Path $windowsDirectory "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $nativeBuildDirectory -Force | Out-Null

Push-Location $projectRoot
try {
    & $compiler `
        /nologo `
        /target:winexe `
        /optimize+ `
        /platform:anycpu `
        /out:".native-build\SkillMover.exe" `
        /win32icon:"assets\app.ico" `
        /reference:System.dll `
        /reference:System.Core.dll `
        /reference:System.Drawing.dll `
        /reference:System.Windows.Forms.dll `
        /reference:System.Web.Extensions.dll `
        "SkillMover.cs"
}
finally {
    Pop-Location
}

if ($LASTEXITCODE -ne 0) {
    throw "Native EXE build failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath $nativeOutputPath -Destination $outputPath -Force

Get-Item -LiteralPath $outputPath |
    Select-Object FullName, Length, LastWriteTime
