#!/usr/bin/env python3
"""Compile production identity logic against isolated doubles; use only a temporary save."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
version = (root / 'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0].split(': ')[1]
unity = Path(os.environ.get('UNITY_APP', str(Path.home() / 'Unity' / version / 'Unity.app')))
scripting = unity / 'Contents/Resources/Scripting'
mono = scripting / 'MonoBleedingEdge'
references = mono / 'lib/mono/4.8-api'
newtonsoft = next((root / 'Library/PackageCache').glob('com.unity.nuget.newtonsoft-json@*/Runtime/Newtonsoft.Json.dll'))
common_sources = [root / 'Assets/SourceFiles/Scripts/Online' / name for name in
    ['OnlineService.cs', 'OnlineService.Identity.cs', 'SupabaseHttp.cs', 'SupabaseSession.cs']]
fixtures = {
    'IdentityChecks': [],
    'RecoveryChecks': [root / 'Assets/SourceFiles/Scripts' / name for name in
        ['Core/ProgressStore.cs', 'Core/XpSystem.cs', 'Online/AttemptsSync.cs', 'Shop/AttemptsService.cs']],
}

for fixture, extra_sources in fixtures.items():
    sources = [root / 'Tools/IdentityChecks' / f'{fixture}.cs.txt'] + common_sources + extra_sources
    with tempfile.TemporaryDirectory(prefix='hh-identity-checks-') as temp:
        folder = Path(temp)
        output = folder / f'{fixture}.exe'
        args = ['-nologo', '-target:exe', '-langversion:latest', '-nostdlib+', '-nowarn:0649,0067', f'-out:{output}']
        args += [f'-r:{references / name}' for name in ['mscorlib.dll', 'System.dll', 'System.Core.dll']]
        args += [f'-r:{references / "Facades/netstandard.dll"}', f'-r:{newtonsoft}']
        args += list(map(str, sources))
        subprocess.run([str(scripting / 'NetCoreRuntime/dotnet'), str(scripting / 'DotNetSdkRoslyn/csc.dll')] + args, check=True, cwd=root)
        shutil.copy2(newtonsoft, folder / 'Newtonsoft.Json.dll')
        subprocess.run([str(mono / 'bin/mono'), str(output), str(folder / 'save')], check=True, cwd=root)
