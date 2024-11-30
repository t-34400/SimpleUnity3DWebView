package com.t34400.webviewtexture;

import com.unity3d.player.UnityPlayer;
import com.unity3d.player.UnityPlayerActivity;

import android.graphics.Bitmap;
import android.graphics.PixelFormat;
import android.os.Bundle;
import android.os.Handler;
import android.view.inputmethod.InputMethodManager;
import android.util.Log;
import android.view.SurfaceView;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.ImageView;
import java.util.HashMap;
import java.util.Map;
import java.io.ByteArrayOutputStream;
import java.nio.ByteBuffer;

import org.json.JSONException;
import org.json.JSONObject;

public class WebViewUnityPlayerActivity extends UnityPlayerActivity {
    private static final String WEBVIEW_COUNT_BUNDLE_KEY = "WebviewCount";
    private static final String GAMEOBJECT_NAME_BUNDLE_KEY_FORMAT = "GameObjectName_%d";
    private static final String WEBVIEW_WIDTH_BUNDLE_KEY_FORMAT = "WebViewWidth_%d";
    private static final String WEBVIEW_HEIGHT_BUNDLE_KEY_FORMAT = "WebViewHeight_%d";
    private static final String OUTPUT_WIDTH_BUNDLE_KEY_FORMAT = "OutputWidth_%d";
    private static final String OUTPUT_HEIGHT_BUNDLE_KEY_FORMAT = "OutputHeight_%d";
    private static final String INTERVAL_BUNDLE_KEY_FORMAT = "Interval_%d";

    public static WebViewUnityPlayerActivity currentWebViewActivity;

    private HashMap<String, WebViewManager> webViewManagers = new HashMap<>();

    private final Object resultByteLock = new Object();
    private byte[] resultBytes = new byte[0];

    @Override
    public void onCreate(Bundle savedInstanceState) {
        Log.i("WebView", "onCreate()");
        super.onCreate(savedInstanceState);
        currentWebViewActivity = this;

        if (savedInstanceState != null) {
            int webviewCount = savedInstanceState.getInt(WEBVIEW_COUNT_BUNDLE_KEY);
            Log.i("WebView", "Webview Count = " + webviewCount);
            for (int index = 0; index < webviewCount; ++index) {
                String gameObjectName = savedInstanceState
                        .getString(String.format(GAMEOBJECT_NAME_BUNDLE_KEY_FORMAT, index));
                int webViewWidth = savedInstanceState.getInt(String.format(WEBVIEW_WIDTH_BUNDLE_KEY_FORMAT, index));
                int webViewHeight = savedInstanceState.getInt(String.format(WEBVIEW_HEIGHT_BUNDLE_KEY_FORMAT, index));
                int outputWidth = savedInstanceState.getInt(String.format(OUTPUT_WIDTH_BUNDLE_KEY_FORMAT, index));
                int outputHeight = savedInstanceState.getInt(String.format(OUTPUT_HEIGHT_BUNDLE_KEY_FORMAT, index));
                long intervalMSec = savedInstanceState.getLong(String.format(INTERVAL_BUNDLE_KEY_FORMAT, index));

                generateWebViewTextureProvider(gameObjectName, webViewWidth, webViewHeight, outputWidth, outputHeight,
                        intervalMSec);
            }

            if (webViewManagers.size() == 1) {
                WebViewManager webViewManager = webViewManagers.values().iterator().next();
                webViewManager.restoreState(savedInstanceState);
            }
        }
    }

    @Override
    public void onDestroy() {
        super.onDestroy();
        Log.i("WebView", "onDestroy()");
        webViewManagers.forEach((gameObjectName, webViewManager) -> {
            webViewManager.stopUpdate();
            webViewManager.onDestroy();
        });
        webViewManagers.clear();
    }

    @Override
    public void onPause() {
        super.onPause();
        Log.i("WebView", "onPause()");
        webViewManagers.forEach((gameObjectName, webViewManager) -> {
            webViewManager.stopUpdate();
        });
    }

    @Override
    public void onResume() {
        super.onResume();
        Log.i("WebView", "onResume()");
        webViewManagers.forEach((gameObjectName, webViewManager) -> {
            webViewManager.startUpdate();
        });
    }

    @Override
    protected void onSaveInstanceState(Bundle outState) {
        super.onSaveInstanceState(outState);
        Log.i("WebView", "onSaveInstanceState()");

        int webviewCount = webViewManagers.size();
        if (webviewCount == 1) {
            WebViewManager webViewManager = webViewManagers.values().iterator().next();
            webViewManager.saveState(outState);
        }
        outState.putInt(WEBVIEW_COUNT_BUNDLE_KEY, webviewCount);

        int index = 0;
        for (Map.Entry<String, WebViewManager> entry : webViewManagers.entrySet()) {
            String gameObjectName = entry.getKey();
            WebViewManager webViewManager = entry.getValue();

            outState.putString(String.format(GAMEOBJECT_NAME_BUNDLE_KEY_FORMAT, index), gameObjectName);
            outState.putInt(String.format(WEBVIEW_WIDTH_BUNDLE_KEY_FORMAT, index), webViewManager.webViewWidth);
            outState.putInt(String.format(WEBVIEW_HEIGHT_BUNDLE_KEY_FORMAT, index), webViewManager.webViewHeight);
            outState.putInt(String.format(OUTPUT_WIDTH_BUNDLE_KEY_FORMAT, index), webViewManager.outputWidth);
            outState.putInt(String.format(OUTPUT_HEIGHT_BUNDLE_KEY_FORMAT, index), webViewManager.outputHeight);
            outState.putLong(String.format(INTERVAL_BUNDLE_KEY_FORMAT, index), webViewManager.outputHeight);
            ++index;
        }
    }

    public WebViewManager generateWebViewTextureProvider(String gameObjectName, int webViewWidth, int webViewHeight,
            int outputWidth, int outputHeight, long intervalMSec) {
        Log.i("WebView", "generateWebViewTextureProvider: GameObject Name = " + gameObjectName);
        if (webViewManagers.containsKey(gameObjectName)) {
            Log.i("WebView", "generateWebViewTextureProvider: WebViewManager already exists.");
            return webViewManagers.get(gameObjectName);
        } else {
            Log.i("WebView", "generateWebViewTextureProvider: WebViewManager not exists.");
            WebViewManager webViewManager = new WebViewManager(
                    this, webViewWidth, webViewHeight, outputWidth, outputHeight, intervalMSec,
                    (bitmapBytes) -> sendUpdateCallbackMessage(gameObjectName, bitmapBytes),
                    (url) -> sendUrlChangedCallbackMessage(gameObjectName, url),
                    (dataType, data) -> sendJsonData(gameObjectName, dataType, data),
                    (ViewGroup) getWindow().getDecorView().getRootView(), mUnityPlayer);
            webViewManagers.put(gameObjectName, webViewManager);
            mUnityPlayer.requestFocus();
            return webViewManager;
        }
    }

    public void removeWebViewTextureProvider(String gameObjectName) {
        if (webViewManagers.containsKey(gameObjectName)) {
            webViewManagers.remove(gameObjectName);
        }
    }

    public byte[] getBitmapBytes() {
        synchronized (resultByteLock) {
            return resultBytes;
        }
    }

    private void sendUpdateCallbackMessage(String gameObjectName, byte[] bitmapBytes) {
        synchronized (resultByteLock) {
            resultBytes = bitmapBytes;
        }
        Log.v("WebView", "sendUpdateCallbackMessage: GameObject Name = " + gameObjectName + ", Bitmap Size = "
                + bitmapBytes.length);

        UnityPlayer.UnitySendMessage(gameObjectName, "ReceiveUpdateCallback", "");
    }

    private void sendUrlChangedCallbackMessage(String gameObjectName, String url) {
        UnityPlayer.UnitySendMessage(gameObjectName, "ReceiveNewUrl", url);
    }

    private void sendJsonData(String gameObjectName, String dataType, String data) {
        try {
            JSONObject jsonObject = new JSONObject();
            jsonObject.put("type", dataType);
            jsonObject.put("data", data);
            UnityPlayer.UnitySendMessage(gameObjectName, "ReceiveJsonData", jsonObject.toString());
        } catch (JSONException e) {
            Log.e("JsonSender", e.toString());
        }
    }
}