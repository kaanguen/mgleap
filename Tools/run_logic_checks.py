#!/usr/bin/env python3
"""Run the Unity-independent C# checks with an installed .NET SDK (6 or later)."""
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
from xml.sax.saxutils import escape


def main():
    root = Path(__file__).resolve().parents[1]
    dotnet = shutil.which("dotnet")
    if not dotnet:
        raise SystemExit("Install a .NET SDK (6 or later) to run these checks.")
    version = subprocess.check_output([dotnet, "--version"], text=True).strip()
    major = int(version.split(".")[0])
    if major < 6:
        raise SystemExit("The checks require .NET SDK 6 or later.")
    sources = root / "Assets/Scripts/StadionTracking"
    names = (
        "StadiumTrackGeometry", "SimpleTrackMap", "StadiumRunProgress", "StadiumPacing",
        "StadiumCalibration", "FloorMarkerPlacement", "LocomotionTiming", "GhostTestMotion",
        "RunnerSpeedInput", "RunnerPersonalBest", "LapCheckpointTracker", "LapGhostRecording", "RunnerCommandValues",
    )
    checks = sorted((root / "Tests/StadionTracking").glob("*Checks.cs"))
    json_dlls = sorted((root / "Library/PackageCache").glob(
        "com.unity.nuget.newtonsoft-json@*/Runtime/Newtonsoft.Json.dll"))
    if not json_dlls:
        raise SystemExit("Open the project in Unity once to restore its Newtonsoft.Json package.")
    with tempfile.TemporaryDirectory(prefix="markerml2-checks-") as directory:
        work = Path(directory)
        classes = []
        for check in checks:
            match = re.search(r"static class (\w+)", check.read_text())
            if not match:
                raise SystemExit(f"Cannot find the check class in {check.name}.")
            classes.append(match.group(1))
        calls = "\n".join(f"        {name}.Main();" for name in classes)
        (work / "Program.cs").write_text(
            "internal static class LogicCheckRunner\n{\n    public static void Main()\n    {\n"
            + calls + '\n        System.Console.WriteLine("PASS: all logic check suites.");\n    }\n}\n')
        files = [sources / f"{name}.cs" for name in names] + checks + [work / "Program.cs"]
        includes = "\n".join(f'    <Compile Include="{escape(str(p))}" />' for p in files)
        (work / "Checks.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net{major}.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <StartupObject>LogicCheckRunner</StartupObject>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
{includes}
    <Reference Include="Newtonsoft.Json">
      <HintPath>{escape(str(json_dlls[-1]))}</HintPath>
    </Reference>
  </ItemGroup>
</Project>
''')
        # All dependencies are local; these checks never need a package download.
        (work / "NuGet.Config").write_text(
            '<configuration><packageSources><clear /></packageSources></configuration>')
        env = dict(os.environ, DOTNET_CLI_HOME=str(work), DOTNET_NOLOGO="1",
                   DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1")
        subprocess.run([dotnet, "run", "--project", str(work / "Checks.csproj"),
                        "--verbosity", "quiet"], cwd=work, env=env, check=True)


if __name__ == "__main__":
    main()
