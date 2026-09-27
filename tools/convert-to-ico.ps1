param(
	[Parameter(Mandatory=$true)] [string] $SourceIcon,
	[Parameter(Mandatory=$true)] [string] $Output = "Assets\Switch.ico"
)

if (-not (Get-Command magick -ErrorAction SilentlyContinue)) {
	Write-Error "ImageMagick 'magick' not found on PATH. Install ImageMagick or use an online converter."
	exit 1
}

# Ensure output folder exists
$dir = Split-Path $Output -Parent
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }

magick convert $SourceIcon -define icon:auto-resize=256,128,64,48,32,16 $Output
Write-Host "Created ICO: $Output"
