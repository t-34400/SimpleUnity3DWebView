package com.t34400.webviewtexture;

import android.app.Activity;
import android.app.Presentation;
import android.content.Context;
import android.graphics.Bitmap;
import android.graphics.PixelFormat;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.media.Image;
import android.media.ImageReader;
import android.os.Bundle;
import android.os.Handler;
import android.os.SystemClock;
import android.util.DisplayMetrics;
import android.util.Log;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.ConsoleMessage;
import android.webkit.WebBackForwardList;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebView;
import android.webkit.WebViewClient;

import java.io.ByteArrayOutputStream;
import java.nio.ByteBuffer;

public class WebViewManager
{
    public interface WebViewBitmapListener {
        void onWebViewUpdated(byte[] bytes);
    }

    public final int webViewWidth;
    public final int webViewHeight;
    public final int outputWidth;
    public final int outputHeight;

    private ImageReader imageReader;
    private VirtualDisplay virtualDisplay;
    private Presentation presentation;

    private WebView webView;
    private WebAppInterface webAppInterface;

    private final Handler mainHandler;

    private boolean isRunning = true;
    private long downTime = 0;

    public WebViewManager(Activity activity, int _webViewWidth, int _webViewHeight, int _outputWidth, int _outputHeight, long intervalMSec,
                          WebViewBitmapListener listener, WebAppInterface.WebViewDataListener webViewDataListener,
                          ViewGroup rootView, View defaultFocusView)
    {
        webViewWidth = _webViewWidth;
        webViewHeight = _webViewHeight;
        outputWidth = _outputWidth;
        outputHeight = _outputHeight;

        mainHandler = new Handler(activity.getMainLooper());

        activity.runOnUiThread( () -> {
            WebView.enableSlowWholeDocumentDraw();
            
            imageReader = ImageReader.newInstance(outputWidth, outputHeight, PixelFormat.RGBA_8888, 2);
            imageReader.setOnImageAvailableListener(imageReader -> {
                synchronized (this)
                {
                    if (!isRunning)
                    {
                        return;
                    }
                }

                Image image = imageReader.acquireNextImage();

                Bitmap bitmap = convertToBitmap(image);

                image.close();

                ByteArrayOutputStream stream = new ByteArrayOutputStream();
                if(bitmap.compress(Bitmap.CompressFormat.JPEG, 100, stream))
                {
                    byte[] bitmapArray = stream.toByteArray();
                    listener.onWebViewUpdated(bitmapArray);
                }
            }, mainHandler);

            DisplayManager displayManager = (DisplayManager)activity.getSystemService(Context.DISPLAY_SERVICE);

            int flags = DisplayManager.VIRTUAL_DISPLAY_FLAG_OWN_CONTENT_ONLY;
            int density = DisplayMetrics.DENSITY_DEFAULT;
            virtualDisplay = displayManager.createVirtualDisplay("WebViewVirtualDisplay",
                    outputWidth, outputHeight, density, imageReader.getSurface(), flags);

            presentation = new Presentation(activity, virtualDisplay.getDisplay());

            webView = new WebView(activity);
            
            presentation.setContentView(webView, new ViewGroup.LayoutParams(webViewWidth, webViewHeight));

            webAppInterface = new WebAppInterface(activity, rootView, defaultFocusView, webView, webViewDataListener);
            webView.addJavascriptInterface(webAppInterface, WebViewJavaScriptConstants.ANDROID_INTERFACE_INSTANCE_NAME);

            webView.setPivotX(0);
            webView.setPivotY(0);
            webView.setScaleX((float)outputWidth / webViewWidth);
            webView.setScaleY((float)outputHeight / webViewHeight);

            webView.setFocusable(false);
            webView.setFocusableInTouchMode(false);

            webView.setSystemUiVisibility(View.SYSTEM_UI_FLAG_FULLSCREEN);

            webView.getSettings().setJavaScriptEnabled(true);
            webView.getSettings().setDomStorageEnabled(true);
            webView.getSettings().setMediaPlaybackRequiresUserGesture(false);
            webView.setWebViewClient(new WebViewClient() {
                @Override
                public void onLoadResource (WebView view, String url){
                }

                @Override
                public void onPageFinished(WebView view, String url) {
                    webAppInterface.Reset();
                    webView.evaluateJavascript(WebViewJavaScriptConstants.SCRIPT__ADD_INPUT_FOCUS_LISTENER, null);
                }
                @Override
                public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
                    Log.e("WebViewError", "Error: " + error.getDescription());
                }
            });
            webView.setWebChromeClient(new WebChromeClient(){
                public boolean onConsoleMessage(ConsoleMessage consoleMessage) {
                    switch (consoleMessage.messageLevel()) {
                        case ERROR:
                            Log.e("WebViewConsole", consoleMessage.message());
                            break;
                        case WARNING:
                            Log.w("WebViewConsole", consoleMessage.message());
                            break;
                        default:
                            Log.d("WebViewConsole", consoleMessage.message());
                            break;
                    }
                    return true;
                }
            });

            presentation.show();
        });
    }

    public void onDestroy() {
        virtualDisplay.release();
        presentation.dismiss();
        imageReader.close();
        webAppInterface.onDestroy();
    }

    public void startUpdate() {
        synchronized (this) {
            if (!isRunning) {
                isRunning = true;
            }
        }
    }

    public void stopUpdate() {
        synchronized (this) {
            if (isRunning) {
                isRunning = false;
            }
        }
    }

    public void loadUrl(String url) {
        mainHandler.post(() -> webView.loadUrl(url));
    }

    public void reload() {
        mainHandler.post(() -> webView.reload());
    }

    public void goBack() {
        mainHandler.post(() -> {
            if(webView.canGoBack()) {
                webView.goBack();
            }
        });
    }

    public void goForward() {
        mainHandler.post(() -> {
            if(webView.canGoForward()) {
                webView.goForward();
            }
        });
    }

    public void invokeDownEvent(float normalizedX, float normalizedY) {
        invokeMotionEvent(normalizedX, normalizedY, MotionEvent.ACTION_DOWN);
    }

    public void invokeMoveEvent(float normalizedX, float normalizedY) {
        invokeMotionEvent(normalizedX, normalizedY, MotionEvent.ACTION_MOVE);
    }

    public void invokeUpEvent(float normalizedX, float normalizedY) {
        invokeMotionEvent(normalizedX, normalizedY, MotionEvent.ACTION_UP);
    }

    public void evaluateJavascript(String script) {
        mainHandler.post( () -> {
            webView.evaluateJavascript(script, null);
        });
    }

    private void invokeMotionEvent(float normalizedX, float normalizedY, int action) {
        mainHandler.post( () -> {
            if(action == MotionEvent.ACTION_DOWN) {
                downTime = SystemClock.uptimeMillis();
            }

            int x = Math.round(normalizedX * webView.getWidth());
            int y = Math.round(normalizedY * webView.getHeight());
            long eventTime = SystemClock.uptimeMillis();

            MotionEvent motionEvent = MotionEvent.obtain(downTime, eventTime, action, x, y, 0);
            webView.dispatchTouchEvent(motionEvent);

            motionEvent.recycle();
        });
    }

    public WebBackForwardList saveState (Bundle outState) {
        return webView.saveState(outState);
    }

    public WebBackForwardList restoreState (Bundle inState) {
        WebBackForwardList webBackForwardList = webView.restoreState(inState);
        mainHandler.post( () -> {
            webView.removeJavascriptInterface(WebViewJavaScriptConstants.ANDROID_INTERFACE_INSTANCE_NAME);
            webView.addJavascriptInterface(webAppInterface, WebViewJavaScriptConstants.ANDROID_INTERFACE_INSTANCE_NAME);
        });
        return webBackForwardList;
    }

    private static Bitmap convertToBitmap(Image image) {
        Image.Plane[] planes = image.getPlanes();

        ByteBuffer buffer = planes[0].getBuffer();

        int pixelStride = planes[0].getPixelStride();
        int rowStride = planes[0].getRowStride();
        int rowPadding = rowStride - pixelStride * image.getWidth();

        Bitmap bitmap = Bitmap.createBitmap(
                image.getWidth() + rowPadding / pixelStride,
                image.getHeight(),
                Bitmap.Config.ARGB_8888);
        bitmap.copyPixelsFromBuffer(buffer);

        return bitmap;
    }
}
