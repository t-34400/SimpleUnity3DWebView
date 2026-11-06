"""Execute production Vulkan lifetime logic with fake endpoints; inspect the arm64 ELF."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ndk', required=True)
    parser.add_argument('--jdk', required=True)
    parser.add_argument('--cxx', default='g++')
    args = parser.parse_args()
    ndk, jdk = Path(args.ndk), Path(args.jdk)
    headers = ndk / 'toolchains/llvm/prebuilt/linux-x86_64/sysroot/usr/include'
    with tempfile.TemporaryDirectory(prefix='vrwebview-native-tests-') as temp:
        program = str(Path(temp) / 'native-probe-tests')
        subprocess.run([args.cxx, '-std=c++17', '-Wall', '-Wextra', '-Werror', '-O1', '-g',
                        '-fsanitize=address,undefined', '-fno-omit-frame-pointer',
                        '-D_Nonnull=', '-D_Nullable=', '-D__INTRODUCED_IN(x)=', '-D__DEPRECATED_IN(x)=',
                        '-idirafter', str(headers), '-I', str(jdk / 'include'), '-I', str(jdk / 'include/linux'),
                        str(ROOT / 'Tools/NativeProbeTests/NativeProbeTests.cpp'), '-o', program], check=True)
        subprocess.run([program], env={**os.environ, 'ASAN_OPTIONS': 'detect_leaks=1'}, check=True)
    package = ROOT / 'Packages/com.t34400.vrwebview'
    provenance = json.loads((package / 'Runtime/Plugins/Android/vrwebview-probe.build.json').read_text())
    native = provenance['nativeLibrary']
    library = ROOT / native['path']
    assert hashlib.sha256(library.read_bytes()).hexdigest() == native['sha256']
    readelf = ndk / 'toolchains/llvm/prebuilt/linux-x86_64/bin/llvm-readelf'
    info = subprocess.check_output([str(readelf), '-h', '-l', '--dyn-syms', str(library)], text=True)
    assert 'AArch64' in info
    for symbol in ('UnityPluginLoad', 'VrCreate', 'VrGetRenderEvent', 'VrAlive', 'VrStatus',
                   'Java_com_t34400_vrwebview_VulkanProbe_nativeSurface'):
        assert symbol in info
    for line in info.splitlines():
        if line.lstrip().startswith('LOAD '):
            assert int(line.split()[-1], 16) >= 16384
    meta = library.with_name(library.name + '.meta').read_text()
    assert 'isPreloaded: 1' in meta and 'CPU: ARM64' in meta
    print('PASS arm64 ELF exports, 16 KiB segment alignment, native provenance and Unity preload metadata')
    print('NOT RUN: real Android buffers/fences, Unity plugin loading/extension interception, Vulkan validation layers, Quest/OpenXR/IME')


if __name__ == '__main__':
    main()
