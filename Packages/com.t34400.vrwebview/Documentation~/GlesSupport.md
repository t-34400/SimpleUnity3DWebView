# Android OpenGL ES 3対応の調査と設計

## 判断

Vulkanと同じ`WebView → hardware Canvas → AImageReader / AHardwareBuffer`を使い、GPU consumerだけを差し替えられる。JavaのWindow・IME・viewport入力・producerの枚数制限を描画APIごとに複製する必要はない。この境界でOpenGL ES 3の検証実装を追加した。

これはAndroid ARM64のGLES対応。Windows/macOS/Linuxのdesktop OpenGLやEditor内WebView、GLES 2には対応しない。Questの実GPU・動画・OpenXR・IMEは未検証で、対応範囲の確定には実機比較が必要。

## 共通部分と差分

| 項目 | 共通 | Vulkan backend | GLES backend |
|---|---|---|---|
| Window / IME | 現在のUnity ActivityにattachしたWebView自身のInputConnection | 同じ | 同じ |
| 描画元 | Window draw off、同じhardware Canvas / AImageReader | 同じ | 同じ |
| producer fence | acquire fence FDをpoll(0)し、完了前にGPUへ渡さない | 同じ | 同じ |
| GPU import | backend内部 | 専用VkImage / VkDeviceMemory | AHB → EGLClientBuffer → EGLImage → GL_TEXTURE_2D |
| Unity出力 | Unity所有、単一sample、RGBA8またはsRGB8A8のRenderTexture | vkCmdCopyImage | CopyImageSubData（raw GPU copy） |
| GPU完了 | 完了するまでAImageとimport資源を保持 | Unity safeFrameNumber | EGL fenceのclient wait（timeout=0） |
| 入力 / 再生成 | 同じVRレイ・viewport座標・pause/resize/recreate | 同じ | 同じ |

nativeの`GpuBackend`がBeginFrame / Submit / Complete / Release / Shutdownを定義する。`GpuProbe.cpp`はAPI固有のVkImage・EGLImage・GL texture型を持たず、キュー・画像寿命・JNIだけを管理する。Vulkan/GLES実装は別ファイル。GLESのcontext情報はセッション単位なので、Recreateで現在のUnity contextを取得し直し、旧世代は旧backendで返却を待つ。

C#の本体は`GpuWebViewProbe`。既存の`VulkanWebViewProbe`はGUIDとserialized fieldを保つ互換クラスとして残す。nativeライブラリ名・JNIクラス・一部helper名のVulkan表記も既存の読込契約として維持するが、実行APIはUnityのrendererから選ぶ。二つの描画経路を同時に動かすものではなく、実行中にUnityのGraphics APIを切り替えない。

## GLESの条件

- OpenGL ES 3.2、またはGLES 3.0/3.1に`GL_EXT_copy_image`か`GL_OES_copy_image`。
- `GL_OES_EGL_image`。
- `EGL_ANDROID_get_native_client_buffer`、`EGL_ANDROID_image_native_buffer`、`EGL_KHR_image_base`、`EGL_KHR_fence_sync`と対応entry points。
- AHardwareBufferのRGBA8がEGLImage / GL_TEXTURE_2Dとして実際にimport可能なこと。拡張文字列の存在だけでは端末の対応を確定しない。

拡張と関数はUnityのrender callbackで確認する。GL資源をメインスレッドで作らず、Unityのcurrent contextを借りる。独自のcontext作成・makeCurrent・GL/Vulkan間の共有context・CPU pixel fallbackは行わない。

画像コピーには名前付きのCopyImageSubDataを使う。shaderやFBOを使うblitではなく、色空間変換・拡縮・上下反転をしないraw copyなので、RGBA8からsRGB8A8のUnity textureへも同じビット列を渡せる。LinearプロジェクトではそのtextureのsamplingがsRGB decodeを担当する。実機の色・alpha・向きは別途確認する。

変更するGL stateは現在unitのGL_TEXTURE_2D bindingのみで、import失敗時も復元する。active texture、FBO、shader、VAO、viewportなどは変更しない。

コピー後のEGL fenceをglFlushしてから保持し、CPU client waitのtimeout=0で返却可能か調べる。通常フレームでglFinishは使わない。fence作成が失敗した場合だけ、既に発行したcopyを完了させてから破棄するためglFinishを使う。完了を証明できないエラーではAImageを返却しない。

context切替後に旧世代のGL名を新contextでdeleteしない。旧contextのGL資源はそのcontextの破棄に委ね、EGLImage / AImageは旧fenceが完了してから返す。fence/display自体が壊れて完了確認できない場合は、画像を保持してproducerを停止する。これはGPU異常時に再利用を避けるための停止で、正常時の無制限な再試行・画像確保は行わない。

## OpenXR / Meta SDKの条件

提供されたOpenXR 1.16.1 / Meta Core 207.0.1のソースも確認した。

- OpenXRProjectValidation.cs: GLES + OpenXRでは**Linear Color Space**が必要。基準プロジェクトは既にLinear。
- MetaQuestFeature.cs: Optimize Buffer Discards / Symmetric ProjectionはVulkan専用。Multiview Render Regions OptimizationもVulkanを要求する。
- MetaXRSubsampledLayout.cs: Meta XR Subsampled LayoutはVulkan専用。基準プロジェクトのAndroid設定では有効なので、GLES比較ビルドではオフにする。
- 基準プロジェクトのAndroid Meta Quest SupportはOptimize Buffer Discardsが有効。GLES比較時はオフにする。Project Validationの自動修正はVulkanへ戻すことがある。
- Meta XR FoveationやApplication SpaceWarpも基準設定では有効。最初の描画API比較ではこれらの追加描画機能を外した最小構成で確認し、必要な機能を個別に戻す。GLES対応を根拠に、Meta SDKの全機能との同時利用を保証するものではない。

現在のVulkan優先設定はパッチで変更しない。GLES対応はライブラリの追加能力であり、Vulkan専用のXR最適化をGLES上で再現するものではない。

## 根拠となる仕様

Khronos EGL-Registry revision `8e56e86e65687697ef54114cdb1afabdfac756bf`:

- [EGL_ANDROID_get_native_client_buffer](https://github.com/KhronosGroup/EGL-Registry/blob/8e56e86e65687697ef54114cdb1afabdfac756bf/extensions/ANDROID/EGL_ANDROID_get_native_client_buffer.txt): AHardwareBufferからEGLClientBufferを取得。対応formatの集合は実装依存。
- [EGL_ANDROID_image_native_buffer](https://github.com/KhronosGroup/EGL-Registry/blob/8e56e86e65687697ef54114cdb1afabdfac756bf/extensions/ANDROID/EGL_ANDROID_image_native_buffer.txt): native bufferをEGLImage sourceにする。
- [EGL_KHR_image_base](https://github.com/KhronosGroup/EGL-Registry/blob/8e56e86e65687697ef54114cdb1afabdfac756bf/extensions/KHR/EGL_KHR_image_base.txt): EGLImageの寿命。
- [EGL_KHR_fence_sync](https://github.com/KhronosGroup/EGL-Registry/blob/8e56e86e65687697ef54114cdb1afabdfac756bf/extensions/KHR/EGL_KHR_fence_sync.txt): 完了条件、timeout=0、異なるcontextからのwait前にproducerでflushする条件。

Khronos OpenGL-Registry revision `6af574a14089ccfee87efe230ebcdd8742859813`:

- [OES_EGL_image](https://github.com/KhronosGroup/OpenGL-Registry/blob/6af574a14089ccfee87efe230ebcdd8742859813/extensions/OES/OES_EGL_image.txt): EGLImageの2D textureへの参照。
- [EXT_copy_image](https://github.com/KhronosGroup/OpenGL-Registry/blob/6af574a14089ccfee87efe230ebcdd8742859813/extensions/EXT/EXT_copy_image.txt): 色空間・format変換をしないGPU copy、texture completenessとcompatible internal formats。

## 検証の境界

nativeの偽Vulkan/GL/EGL endpointsを使ったASan/UBSan/LSanテストで、既存Vulkanのfence/safe frame/破棄、GLESのraw copy呼出・binding復元・fence待ち・import/fence失敗・context切替・拡張選択を確認する。実際のGPUでの色、EGLImage format、ドライバー同期、Unity context、OpenXRやIMEの成功はこのテストからは判断できない。実機の比較順序は[GpuXrProbe.md](GpuXrProbe.md)を参照。
