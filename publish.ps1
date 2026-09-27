param(
	[string]$configuration = "Release",
	[string]$runtime = "win-x64"
)

# Publish self-contained executable
$publishDir = Join-Path $PSScriptRoot "publish"
Write-Host "Publishing self-contained app to $publishDir"
dotnet publish Switch -c $configuration -r $runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publishDir

Write-Host "Published to $publishDir"