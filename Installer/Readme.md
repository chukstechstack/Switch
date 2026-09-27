Usage: This installer file is a WiX source file to build an MSI that installs the published Switch app.

Build steps (example, requires WiX Toolset installed on build agent):

1. Publish both projects to a folder:
   dotnet publish Switch -c Release -o artifacts/Switch
   dotnet publish UninstallChecker -c Release -o artifacts/UninstallChecker

2. Harvest application files (heat) and compile:
   heat dir artifacts/Switch -cg AppFiles -dr INSTALLFOLDER -sreg -scom -gg -sfrag -out Installer\AppFiles.wxs
   candle -dPublishFolder=artifacts -out Installer\obj\Setup.wixobj Installer\Setup.wxs Installer\AppFiles.wxs
   light -out artifacts\SwitchInstaller.msi Installer\obj\Setup.wixobj

The provided GitHub Actions workflow automates these steps.
