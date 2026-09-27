To add a proper application icon:

1. Create or obtain a multi-size ICO file (recommended sizes: 16, 32, 48, 256).
2. Place it at Assets\Switch.ico (overwrite the placeholder file).
3. Re-open the solution and rebuild. If you want the icon embedded in the EXE, uncomment or add the following in Switch.csproj inside a <PropertyGroup>:

<ApplicationIcon>Assets\Switch.ico</ApplicationIcon>
<Win32Icon>Assets\Switch.ico</Win32Icon>

PowerShell helper (requires ImageMagick "magick" on PATH):

# Example usage:
# .\tools\convert-to-ico.ps1 -SourceIcon icon.png -Output Assets\Switch.ico

param(
	[Parameter(Mandatory=$true)] [string] $SourceIcon,
	[Parameter(Mandatory=$true)] [string] $Output
)

if (-not (Get-Command magick -ErrorAction SilentlyContinue)) {
	Write-Error "ImageMagick 'magick' not found on PATH. Install ImageMagick or use an online converter."
	exit 1
}

magick convert $SourceIcon -define icon:auto-resize=256,128,64,48,32,16 $Output

Write-Host "Created ICO: $Output"
