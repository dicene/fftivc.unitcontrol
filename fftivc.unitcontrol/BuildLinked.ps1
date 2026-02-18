# Set Working Directory
Split-Path $MyInvocation.MyCommand.Path | Push-Location
[Environment]::CurrentDirectory = $PWD

Remove-Item "$env:RELOADEDIIMODS/fftivc.unitcontrol/*" -Force -Recurse
dotnet publish "./fftivc.unitcontrol.csproj" -c Release -o "$env:RELOADEDIIMODS/fftivc.unitcontrol" /p:OutputPath="./bin/Release" /p:ReloadedILLink="true"

# Restore Working Directory
Pop-Location