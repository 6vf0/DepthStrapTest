set windows-shell := ["powershell.exe", "-c"]

build:
    dotnet build DepthStrap.slnx -c Release

publish:
    dotnet publish ./Bloxstrap/Bloxstrap.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/publish
    ./Scripts/package-release.ps1

clean:
    dotnet clean DepthStrap.slnx -c Release
