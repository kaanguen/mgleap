"""Rebuild the companion JAR using the JDK/SDK bundled with Unity. No downloads."""
import argparse
import os
from pathlib import Path
import subprocess
import tempfile

parser = argparse.ArgumentParser()
parser.add_argument('--jdk', required=True, help='Unity AndroidPlayer/OpenJDK directory')
parser.add_argument('--android-jar', required=True, help='Android SDK/platforms/android-34/android.jar or newer')
parser.add_argument('--plugin-jar', required=True, help='Velorexe Plugins/Android/unityandroidble.jar at commit 55d8c12e155cc45bc26cea120d5e8c1e5f231b9f')
parser.add_argument('--output', help='Output cosinuss-gatt.jar')
a = parser.parse_args()
base = Path(__file__).resolve().parent
output = Path(a.output).resolve() if a.output else base.parent/'Assets/Plugins/Android/cosinuss-gatt.jar'
output.parent.mkdir(parents=True, exist_ok=True)
suffix = '.exe' if os.name == 'nt' else ''
with tempfile.TemporaryDirectory(prefix='cosinuss-gatt-') as temp:
    subprocess.run([str(Path(a.jdk)/'bin'/('javac'+suffix)), '-source', '8', '-target', '8',
        '-classpath', os.pathsep.join([a.android_jar, a.plugin_jar]), '-d', temp, str(base/'CosinussGatt.java')], check=True)
    subprocess.run([str(Path(a.jdk)/'bin'/('jar'+suffix)), 'cf', str(output), '-C', temp, '.'], check=True)
print(output)
