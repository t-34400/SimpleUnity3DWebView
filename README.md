# Simple Unity 3D WebView for Android (or Meta Quest, etc.)

Simple Unity 3D WebView is a straightforward 3D WebView library designed for the Android platform, enabling seamless integration of web content within Unity projects.

<img src="./Image/google_3d.png" width=200>   <img src="./Image/google_3d_2.png" width=200>

## Installation

1. [Install the package](https://docs.unity3d.com/Manual/upm-ui-local.html).
2. Select Android from File > Build Settings > Platform and press the Switch Platform button.
3. Press the Player Settings button, check Custom Main Manifest under Publishing Settings > Build, and verify the path below.
4. Open the generated `AndroidManifest.xml` at the confirmed path and make the following changes:
   - Add `<uses-permission android:name="android.permission.INTERNET" />` tag inside the manifest tag.
   - Add `android:networkSecurityConfig="@xml/network_security_config"` and `android:hardwareAccelerated="true"` inside the application tag.
   - Change the android:name of the activity tag to `com.t34400.webviewtexture.WebViewUnityPlayerActivity`.
   - Sample:
     ```xml
     <?xml version="1.0" encoding="utf-8"?>
     <manifest
         xmlns:android="http://schemas.android.com/apk/res/android"
         package="com.unity3d.player"
         xmlns:tools="http://schemas.android.com/tools">
         <uses-permission android:name="android.permission.INTERNET" />
         <application android:networkSecurityConfig="@xml/network_security_config"
                     android:hardwareAccelerated="true">
             <activity android:name="com.t34400.webviewtexture.WebViewUnityPlayerActivity"
                     android:theme="@style/UnityThemeSelector">
                 <intent-filter>
                     <action android:name="android.intent.action.MAIN" />
                     <category android:name="android.intent.category.LAUNCHER" />
                 </intent-filter>
                 <meta-data android:name="unityplayer.UnityActivity" android:value="true" />
             </activity>
         </application>
     </manifest>
     ```

## Usage

1. Create a `RawImage` object on a suitable `Canvas` and add the `PointerEventSource` component.
2. Add the `WebViewBitmapReceiver` component to a GameObject and update the inspector as follows:
   - Attach the previously created `RawImage` to Image and Pointer Event Source.
   - Specify the screen size (in pixels) of the web browser in Web View Size (affecting the layout of the website).
   - Specify the size (in pixels) of the texture-mapped web page in Texture Size.
   - Set the highest frequency of texture updates in Interval Sec.
3. Add the `WebViewControllerClient` component to a GameObject and update the inspector as follows:
   - Attach the previously created GameObject to Web View Bitmap Receiver.
   - Specify the initial URL in Load Url.
4. Depending on the device, add the necessary components for UI interaction (XR Interaction Toolkit, Oculus Interaction SDK, etc.).

Sample prefabs are located at `Assets/SamplePrefabs` for reference.

## Web Browser Interaction

- Click and drag on the `RawImage` to perform touch operations.
  - Additionally, add UI operation components (e.g., XR Interaction Toolkit, Oculus Interaction SDK) compatible with the device to operate based on those UI interactions.
    - To be more specific, UI operations corresponding to `IPointerExitHandler`, `IPointerDownHandler`, `IPointerUpHandler`, and `IDragHandler` in the `UnityEngine.EventSystems` namespace allow control of the browser.
- Various operations such as loading URLs, reloading, going back or forward, executing JavaScript, and starting/stopping screen updates can be performed through the methods of `WebViewControllerClient`.
  - `void LoadUrl(string url)`: Load a URL.
  - `void Reload()`: Reload the page.
  - `void GoBack()`: Go back.
  - `void GoForward()`: Go forward.
  - `void EvaluateJavascript(string script)`: Execute JavaScript.
  - `void StartUpdate()`: Start screen updates.
  - `void StopUpdate()`: Stop screen updates.

## Retrieving Values from Web Pages

- Call `Android.sendJsonData('type string', 'data string')` in JavaScript to trigger the `UnityEvent<string, string> dataReceived` event in `WebViewBitmapReceiver`.
  - The event arguments correspond to the arguments sent with `Android.sendJsonData('type string', 'data string')`.
  - Define a method in the component handling the event in the format `public void DoSomething(string type, string dataString)`; register it as a listener for `dataReceived` in the inspector of `WebViewBitmapReceiver`.
  - Sample:
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
        
        [SerializeField] private WebViewControllerClient webViewControllerClient;

        // Process received data (register this method as a listener for `WebViewBitmapReceiver`'s `dataReceived`)
        public void DataReceived(string type, string data)
        {
            if(type.Equals("YOUR_TYPE"))
            {
                var idNameData = JsonUtility.FromJson<IdNameData>(data);
                Debug.Log($"ID: {idNameData.id}, Name: {idNameData.name}");
            }
        }

        private void Update()
        {
            // Call Android.sendJsonData() in JavaScript
            webViewControllerClient.EvaluateJavascript(MY_SCRIPT);
        }

        [System.Serializable]
        public struct IdNameData
        {
            public int id;
            public string name;
        }
    }
    ```

## Enabling Specific APIs for Non-SSL Connections

1. Open `Assets/Plugins/Android/res.androidlib/res/xml/network_security_config.xml` (cannot be displayed in the UnityEditor; open through other means).
2. Add `<domain includeSubdomains="true">[hostname]</domain>` to the <domain-config> tag.
   - By default, only localhost has this feature enabled.

## Notes

- WebView texture transfer from Android native to Unity is simple and CPU-based, so heavy processing may occur with large texture sizes or excessively frequent updates.
- Text input with the keyboard is supported for input and textarea tags but may be somewhat unstable.
- Messages from the Native side of Android are sent to the GameObject name to which the WebViewBitmapReceiver is attached, so the name must be different from other objects in the scene.
    - If multiple WebViews are placed in the same scene, use different names for each.
- If you are developing a project using OpenXR, uncheck `Force Remove Internet` under `Project Settings` > `OpenXR` > `Meta Quest Support`.
    - In Unity OpenXR package versions prior to 1.9.1, there is a bug where, even if this checkbox is unchecked, the Internet permission is removed. To address this issue, either update the OpenXR version in `Packages/manifest.json` to 1.9.1, or use Post Gradle during the build process to remove the OpenXR permission and then re-add it to the manifest.
<img src="./Image/openxr_force_remove_internet.png" width=400>

- When using on Meta Quest, add the following permission to `AndroidManifest.xml.
  
   ```xml
   <uses-feature android:name="oculus.software.overlay_keyboard" 
                 android:required="true" />
   ```

## License

[MIT License](./LICENSE)

## Acknowledgments

The button icons used in the sample prefabs are borrowed from [Evericons](http://evericons.com) under the CC0 1.0 Universal license. We sincerely appreciate their generosity and the availability of these icons for use in our project.
