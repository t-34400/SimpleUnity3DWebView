package com.t34400.vrwebview;

import android.content.res.AssetManager;
import android.net.Uri;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.util.Log;
import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.HashMap;
import java.util.Map;

/** Fixed, same-origin test resources. Range responses make video seeking testable. */
final class ProbeAssets extends WebViewClient {
    private static final java.util.concurrent.atomic.AtomicLong REQUESTS = new java.util.concurrent.atomic.AtomicLong();
    static final String PAGE = "https://vrwebview.invalid/probe/index.html";
    private final byte[] html, video;

    ProbeAssets(AssetManager assets) throws IOException { this(assets, false); }
    ProbeAssets(AssetManager assets, boolean proxyTrial) throws IOException {
        html = read(assets, proxyTrial ? "vrwebview-proxy-probe.html" : "vrwebview-gpu-probe.html");
        video = read(assets, "vrwebview-probe-video.mp4");
    }

    private static byte[] read(AssetManager assets, String name) throws IOException {
        try (InputStream input = assets.open(name); ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[16384]; int count;
            while ((count = input.read(buffer)) != -1) output.write(buffer, 0, count);
            return output.toByteArray();
        }
    }

    @Override public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
        Uri uri = request.getUrl();
        if (!"https".equals(uri.getScheme()) || !"vrwebview.invalid".equals(uri.getHost())
                || uri.getPort() != -1 || !"GET".equals(request.getMethod())) return blocked();
        boolean isVideo = "/probe/video.mp4".equals(uri.getPath());
        if (!isVideo && !"/probe/index.html".equals(uri.getPath())) return blocked();
        byte[] data = isVideo ? video : html;
        String header = null;
        for (Map.Entry<String, String> item : request.getRequestHeaders().entrySet())
            if ("range".equalsIgnoreCase(item.getKey())) header = item.getValue();
        ProbeRange range = ProbeRange.parse(header, data.length);
        Map<String, String> headers = new HashMap<>();
        headers.put("Accept-Ranges", "bytes"); headers.put("Cache-Control", "no-store");
        headers.put("Content-Length", Long.toString(range.length));
        headers.put("X-Content-Type-Options", "nosniff");
        if (range.status == 206)
            headers.put("Content-Range", "bytes " + range.start + "-" + (range.start + range.length - 1) + "/" + data.length);
        if (range.status == 416) headers.put("Content-Range", "bytes */" + data.length);
        String reason = range.status == 200 ? "OK" : range.status == 206 ? "Partial Content" : "Range Not Satisfiable";
        InputStream stream;
        if (isVideo) {
            long id = REQUESTS.incrementAndGet();
            // Never log URLs or raw request headers. IDs distinguish cancelled preloads/reloads.
            String prefix = "[VRWebViewFocus] video=bundled request=" + id + " ";
            Log.i("VRWebViewFocus", prefix + "event=response status=" + range.status + " start=" + range.start
                    + " responseBytes=" + range.length + " totalBytes=" + data.length + " rangePresent=" + (header != null)
                    + " streamStartsAt=0 rangeOwner=webview");
            stream = ProbeMediaStream.intercepted(data, range,
                    event -> Log.i("VRWebViewFocus", prefix + event));
        } else stream = ProbeMediaStream.intercepted(data, range, ignored -> {});
        return new WebResourceResponse(isVideo ? "video/mp4" : "text/html", isVideo ? null : "UTF-8",
                range.status, reason, headers, stream);
    }

    @Override public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
        return !PAGE.equals(request.getUrl().toString());
    }

    private static WebResourceResponse blocked() {
        return new WebResourceResponse("text/plain", "UTF-8", 403, "Forbidden", new HashMap<>(),
                new ByteArrayInputStream(new byte[0]));
    }
}
