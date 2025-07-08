# Simple Unity 3D WebView for Android (or Meta Quest, etc.)

Simple Unity 3D WebView is a straightforward 3D WebView library designed for the Android platform, enabling seamless integration of web content within Unity projects.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![CodeFactor](https://www.codefactor.io/repository/github/t-34400/simpleunity3dwebview/badge)](https://www.codefactor.io/repository/github/t-34400/simpleunity3dwebview)

<img src="./Image/google_3d.png" width=200>   <img src="./Image/google_3d_2.png" width=200>

## 🔥 Firebase Setup

Native Firebase libraries (.so) are not included in this repo due to file size limits.
To build the project, please import the following UnityPackages (version 12.10.1):

- Analytics
- Auth
- Firestore

## 🚀 What's New (v2.0.0)

This release includes key improvements for compatibility and developer experience:
* **Support for Unity 2023.1 and Later**
  Adapted to breaking changes in `UnityPlayer` introduced in Unity 2023.1 and newer versions.
* **No More Subclassing of UnityPlayerActivity**
  The implementation no longer relies on subclassing `UnityPlayerActivity` or `UnityPlayerGameActivity`, improving cross-version compatibility.
* **Android Code Now Packaged as AAR**
  All Android-side JVM code is consolidated into a single `.aar` file, streamlining integration and enhancing build efficiency.

## Installation

### Unity 2022 or Earlier

1. Install [the package](https://github.com/t-34400/SimpleUnity3DWebView/releases) ([Official Manual](https://docs.unity3d.com/Manual/upm-ui-local.html)).
2. Go to `File` > `Build Settings`, select **Android** as the platform, and click **Switch Platform**.
3. Click **Player Settings**, then under `Publishing Settings` > `Build`, enable both `Custom Main Manifest` and `Custom Main Gradle Template`. Note the paths shown.
4. Open the generated `AndroidManifest.xml` at the specified path and make the following changes:
    - Add the following tag inside the `<manifest>` element:
      ```xml
      <uses-permission android:name="android.permission.INTERNET" />
      ```
    - Inside the `<application>` element, add:
      ```xml
      android:networkSecurityConfig="@xml/network_security_config"
      android:hardwareAccelerated="true"
      ```
5. Open the generated `mainTemplate.gradle` at the same path and modify it as follows:
    - In the `dependencies` block, add:
      ```gradle
      implementation "org.jetbrains.kotlin:kotlin-stdlib:2.0.21"
      ```

- [AndroidManifest.xml Sample](Assets/SimpleUnity3DWebView/Plugins/Android/AndroidManifest_2022_sample.xml)
- [mainTemplate.gradle Sample](Assets/SimpleUnity3DWebView/Plugins/Android/mainTemplate_sample.gradle)

---

### Unity 2023 or Later

1. Install [the package](https://github.com/t-34400/SimpleUnity3DWebView/releases) ([Official Manual](https://docs.unity3d.com/Manual/upm-ui-local.html)).
2. Go to `File` > `Build Settings`, select **Android** as the platform, and click **Switch Platform**.
3. In **Player Settings**, under `Other Settings` > `Configuration`, check `GameActivity` and uncheck `Activity`.
4. Under `Publishing Settings` > `Build`, enable both `Custom Main Manifest` and `Custom Main Gradle Template`. Note the paths shown.
5. Open the generated `AndroidManifest.xml` and make the following changes:
    - Remove the `<activity>` tag for `UnityPlayerActivity`.
    - Add the following tag inside the `<manifest>` element:
      ```xml
      <uses-permission android:name="android.permission.INTERNET" />
      ```
    - Inside the `<application>` element, add:
      ```xml
      android:networkSecurityConfig="@xml/network_security_config"
      android:hardwareAccelerated="true"
      ```
6. Open the generated `mainTemplate.gradle` and add the following line in the `dependencies` block:
    ```gradle
    implementation "org.jetbrains.kotlin:kotlin-stdlib:2.0.21"
    ```

- [AndroidManifest.xml Sample](Assets/SimpleUnity3DWebView/Plugins/Android/AndroidManifest_2023_sample.xml)
- [mainTemplate.gradle Sample](Assets/SimpleUnity3DWebView/Plugins/Android/mainTemplate_sample.gradle)


## Usage

1. Create a `RawImage` object on a suitable `Canvas`, and add the `PointerEventSource` component.
2. Add the `WebViewManager` component to a GameObject, and update the Inspector as follows:
   - Assign the previously created `RawImage` to both **Web View Image** and **Pointer Event Source**.
   - Set **Texture Width** to the width (in pixels) of the texture used to render the web content.
   - Set **Interval MSec** to the update interval of the texture in milliseconds.
   - Set **Default Url** to the initial URL to load (leave blank to skip automatic loading).
   - Set **Normalized Touch Slop** to adjust the drag sensitivity, normalized by texture size. (Note: this setting is currently not fully functional.)

3. Depending on the target device, add any required input or interaction components (e.g., XR Interaction Toolkit, Oculus Interaction SDK) to enable UI interaction.

Sample prefabs are available in `Assets/SamplePrefabs` for reference.


## Web Browser Interaction

- Click and drag on the `RawImage` to perform touch interactions.
  - Additionally, attach UI input components (e.g., XR Interaction Toolkit, Oculus Interaction SDK) appropriate to your target device to enable interaction with the web view.
    - Specifically, the system responds to Unity UI events such as `IPointerExitHandler`, `IPointerDownHandler`, `IPointerUpHandler`, and `IDragHandler` in the `UnityEngine.EventSystems` namespace.

- You can control browser behavior through the public methods of the `WebViewManager` component:
  - `void LoadUrl(string url)`: Loads the specified URL.
  - `void Reload()`: Reloads the current page.
  - `void GoBack()`: Navigates back in the browser history.
  - `void GoForward()`: Navigates forward in the browser history.
  - `void SetKeyboardInputEnabled(bool isEnabled)`: Enables or disables keyboard input.
  - `void EvaluateJavascript(string script)`: Executes JavaScript in the current page.

- To start or stop screen updates, enable or disable the `WebViewManager` component itself:
  - `webViewManager.enabled = true;` enables texture updates.
  - `webViewManager.enabled = false;` stops texture updates.


## Retrieving Values from Web Pages

The `WebViewManager` component provides UnityEvents for communicating from JavaScript running inside the WebView:

- `urlChanged`: Invoked when the current page URL changes.
  - Signature: `UnityEvent<string>` — receives the new URL as a string.

- `dataReceived`: Invoked when JavaScript calls `Android.sendJsonData(...)`.
  - Signature: `UnityEvent<ReceivedData>` — receives a structured data object from JavaScript.

### Sending Data from JavaScript

To send data from the web page, call the following in JavaScript:

```javascript
Android.sendJsonData('YOUR_TYPE', JSON.stringify({ id: 100, name: 't34400' }));
```

This will trigger the `dataReceived` event on the `WebViewManager`. The event receives a `ReceivedData` object with the following structure:

```c#
namespace WebView
{
    [System.Serializable]
    public struct ReceivedData
    {
        public string type;
        public string data;
    }
}
```

### Example Unity Component

<details>
<summary>Click to expand</summary>

```c#
using UnityEngine;
using WebView;

public class TestComponent : MonoBehaviour
{
    private const string MY_SCRIPT = 
        "const data = { id: 100, name: 't34400' };" +
        "try {" +
        "   const dataJson = JSON.stringify(data);" +
        "   Android.sendJsonData('YOUR_TYPE', dataJson);" +
        "} catch(e) {" +
        "   console.error(e.message);" +
        "}";

    [SerializeField] private WebViewManager webViewManager;

    // Register this method to WebViewManager.dataReceived in the Inspector
    public void OnDataReceived(ReceivedData received)
    {
        if (received.type == "YOUR_TYPE")
        {
            var idNameData = JsonUtility.FromJson<IdNameData>(received.data);
            Debug.Log($"ID: {idNameData.id}, Name: {idNameData.name}");
        }
    }

    private void Update()
    {
        // Call Android.sendJsonData() in JavaScript
        webViewManager.EvaluateJavascript(MY_SCRIPT);
    }

    [System.Serializable]
    public struct IdNameData
    {
        public int id;
        public string name;
    }
}
```
</details>

To handle the `urlChanged` event, define a method like `public void OnUrlChanged(string url)` and register it in the Inspector.


## Enabling Specific APIs for Non-SSL Connections

1. Open `Assets/Plugins/Android/res.androidlib/res/xml/network_security_config.xml` (cannot be displayed in the UnityEditor; open through other means).
2. Add `<domain includeSubdomains="true">[hostname]</domain>` to the <domain-config> tag.
   - By default, only localhost has this feature enabled.

## Notes

- The output from WebView's JavaScript console is logged to Logcat with the tag `WebViewConsole`.
- WebView texture transfer from Android native to Unity is simple and CPU-based, so heavy processing may occur with large texture sizes or excessively frequent updates.
- Text input with the keyboard is supported for input and textarea tags but may be somewhat unstable.
- If you are developing a project using OpenXR, uncheck `Force Remove Internet` under `Project Settings` > `OpenXR` > `Meta Quest Support`.
    - In Unity OpenXR package versions prior to 1.9.1, there is a bug where, even if this checkbox is unchecked, the Internet permission is removed. To address this issue, either update the OpenXR version in `Packages/manifest.json` to 1.9.1, or use Post Gradle during the build process to remove the OpenXR permission and then re-add it to the manifest.
<img src="./Image/openxr_force_remove_internet.png" width=400>

- When using on Meta Quest, add the following permission to `AndroidManifest.xml`:
  
   ```xml
   <uses-feature android:name="oculus.software.overlay_keyboard" 
                 android:required="true" />
   ```

## Android AAR Source Code

The Android-side implementation bundled as an `.aar` is open source and available here:

- [View AAR Source Code](https://github.com/t-34400/UnityWebViewLib)

## License

[MIT License](./LICENSE)

## Acknowledgments

The button icons used in the sample prefabs are borrowed from [Evericons](http://evericons.com) under the CC0 1.0 Universal license. We sincerely appreciate their generosity and the availability of these icons for use in our project.
