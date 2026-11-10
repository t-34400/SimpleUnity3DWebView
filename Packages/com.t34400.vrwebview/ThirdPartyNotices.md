# Third-party notices

The Vulkan bridge uses the Unity Native Plugin API headers from
Unity-Technologies/NativeRenderingPlugin, revision
522254181faf188efa8b50c3e3bf6fce720b26e4.

Their existing notices read:

> Unity Native Plugin API copyright © 2015 Unity Technologies ApS
>
> Licensed under the Unity Companion License for Unity-dependent projects:
> http://www.unity3d.com/legal/licenses/Unity_Companion_License
>
> Unless expressly provided otherwise, the Software under this license is made
> available strictly on an “AS IS” BASIS WITHOUT WARRANTY OF ANY KIND, EXPRESS
> OR IMPLIED. Please review the license for details on these and other terms
> and conditions.

The upstream repository also supplies this notice, retained here:

The MIT License (MIT)

Copyright (c) 2016, Unity Technologies

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.


## Google GameActivity compatibility artifact

The optional `Editor/GameActivityImeCompat` artifact contains modified Google
GameActivity 4.4.0 code under Apache-2.0. Its complete license and modification
notice are included in that directory. The existing VRWebView compatibility
helper and patch tooling remain MIT; this is an explicit exception to the
project's Apache-2.0 default. This includes `Android~/gameactivity-compat` and
`Tools/build_gameactivity_compat.py` in the source repository, and the helper
classes in the prebuilt AAR. Their MIT text is retained in
[Licenses/VRWebView-GameActivity-MIT.txt](Licenses/VRWebView-GameActivity-MIT.txt).
The already-tested AAR is unchanged by the project license transition.

## Android LLVM runtime

The native plugin is built with Android NDK 27.2.12479018 and a static C++ runtime.
The upstream LLVM/libc++/libc++abi notice bundle is retained in
[Licenses/Android-LLVM-NOTICE.txt](Licenses/Android-LLVM-NOTICE.txt). This upstream
bundle also includes notices for toolchain components; it does not change the
license of VRWebView's own code.

## External Unity packages

Meta XR Core SDK and Unity packages are installed as dependencies in the host
project. Their code and licenses are not changed by VRWebView's Apache-2.0 license.

## Earlier VRWebView releases

The project license transition starts with 0.1.0-preview.53. Previously published
MIT versions retain their original grant; the transition does not revoke it.
