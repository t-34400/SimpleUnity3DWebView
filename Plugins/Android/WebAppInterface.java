package com.t34400.webviewtexture;

import android.app.Activity;
import android.content.Context;
import android.text.Editable;
import android.text.InputType;
import android.text.TextWatcher;
import android.util.Log;
import android.view.View;
import android.view.ViewGroup;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputMethodManager;
import android.webkit.JavascriptInterface;
import android.webkit.WebView;
import android.widget.EditText;

public class WebAppInterface {
    public interface WebViewDataListener {
        void sendJsonData(String dataType, String data);
    }
    
    private static InputMethodManager imm;

    private Activity activity;

    private View defaultFocusView;
    private ViewGroup rootView;
    private WebView webView;

    private EditText editText;

    private WebViewDataListener webViewDataListener;

    public WebAppInterface(Activity _activity, ViewGroup _rootView, View _defaultFocusView, WebView _webView, WebViewDataListener _webViewDataListener) {
        activity = _activity;

        rootView = _rootView;
        defaultFocusView = _defaultFocusView;
        webView = _webView;

        webViewDataListener = _webViewDataListener;
    }

    public void onDestroy() {
        activity = null;
        rootView = null;
        defaultFocusView = null;
        webView = null;
        editText = null;
    }

    public void Reset() {
        if(editText != null) {
            rootView.removeView(editText);
            editText = null;
            defaultFocusView.requestFocus();
        }
        defaultFocusView.requestFocus();
    }

    @JavascriptInterface
    public void onInputFocusAcquired(String type, String currentValue) {
        activity.runOnUiThread( () -> {
            Log.d("WebView", String.format("onInputFocusAcquired(type: %s, value: %s)", type, currentValue));

            if(editText != null) {
                rootView.removeView(editText);
                editText = null;
                defaultFocusView.requestFocus();
            }

            int inputType = 0;
            String inputValue = currentValue;

            switch (type)
            {
                case "text":
                    inputType = InputType.TYPE_CLASS_TEXT;
                    break;
                case "url":
                    inputType = InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI;
                    break;
                case "email":
                    inputType = InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_WEB_EMAIL_ADDRESS;
                    break;
                case "number":
                    inputType = InputType.TYPE_CLASS_NUMBER;
                    break;
                case "password":
                    inputType = InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_WEB_PASSWORD;
                    inputValue = "";
                    break;
                case "search":
                    inputType = InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_WEB_EDIT_TEXT;
                    break;
                case "tel":
                    inputType = InputType.TYPE_CLASS_PHONE;
                    break;
                default:
                    return;
            }

            Log.d("WebView", "Open Keyboard: " + inputType + ", " + inputValue);

            editText = new EditText(activity);
            editText.setInputType(inputType);
            editText.setText(inputValue);
            editText.addTextChangedListener(new TextWatcher()
            {
                @Override
                public void beforeTextChanged(CharSequence charSequence, int start, int before, int count) {
                }

                @Override
                public void onTextChanged(CharSequence charSequence, int start, int before, int count) {
                }

                @Override
                public void afterTextChanged(Editable editable) {
                    webView.getHandler().post( () ->
                            webView.evaluateJavascript(String.format(WebViewJavaScriptConstants.SCRIPT__SET_INPUT_VALUE, editable.toString()), null)
                    );
                }
            });
            editText.setOnEditorActionListener((v, actionId, event) -> {
                boolean handled = false;
                if (actionId == EditorInfo.IME_ACTION_DONE) {
                    webView.getHandler().post( () ->
                            webView.evaluateJavascript(WebViewJavaScriptConstants.SCRIPT__REMOVE_FOCUS, null)
                        );
                    handled = true;
                }
                return handled;
            });

            rootView.addView(editText);
            editText.setElevation(-1);
            editText.requestFocus();
            
            if(imm == null) {
                imm = (InputMethodManager) activity.getSystemService(Context.INPUT_METHOD_SERVICE);
            }
            imm.showSoftInput(editText, InputMethodManager.SHOW_IMPLICIT);
        });
    }

    @JavascriptInterface
    public void onInputFocusLost() {
        activity.runOnUiThread( () -> {
            Log.d("WebView", String.format("onInputFocusLost(); EditText is null?: %b", editText == null));
            if (editText != null)
            {
                editText.clearFocus();
                defaultFocusView.requestFocus();

                if(imm == null) {
                    imm = (InputMethodManager) activity.getSystemService(Context.INPUT_METHOD_SERVICE);
                }
                imm.hideSoftInputFromWindow(editText.getWindowToken(), 0);

                rootView.removeView(editText);
                editText = null;
            }
        });
    }

    @JavascriptInterface
    public void sendJsonData(String type, String data) {
        webViewDataListener.sendJsonData(type, data);
    }
}