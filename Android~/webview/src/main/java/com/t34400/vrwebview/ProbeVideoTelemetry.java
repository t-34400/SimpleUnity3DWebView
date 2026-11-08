package com.t34400.vrwebview;

import android.os.SystemClock;
import android.webkit.WebView;
import org.json.JSONArray;
import org.json.JSONObject;
import java.util.function.Consumer;

/** One outstanding UI-thread metadata query. Never reads field contents or logs JS results verbatim. */
final class ProbeVideoTelemetry {
    private final Consumer<String> trace;
    private long nextPoll, epoch;
    private boolean pending, closed;
    ProbeVideoTelemetry(Consumer<String> trace) { this.trace = trace; }
    void reset() { epoch++; pending = false; nextPoll = 0; }
    void close() { closed = true; reset(); }
    void poll(WebView web) {
        long now = SystemClock.uptimeMillis();
        if (closed || pending || now < nextPoll || !ProbeAssets.PAGE.equals(web.getUrl())) return;
        nextPoll = now + 1000; pending = true; long ticket = epoch;
        web.evaluateJavascript("typeof window.vrwebviewVideoMetadata==='function'?window.vrwebviewVideoMetadata():null", result -> {
            if (closed || ticket != epoch) return;
            pending = false;
            if (result == null || result.equals("null")) return;
            try {
                JSONObject value = new JSONObject(result);
                StringBuilder line = new StringBuilder("video=bundled event=sample");
                for (String key : new String[]{"time", "duration", "ready", "network", "frames", "dropped", "pageMs"})
                    line.append(' ').append(key).append('=').append(number(value.opt(key)));
                for (String key : new String[]{"paused", "seeking", "ended", "hidden"})
                    line.append(' ').append(key).append('=').append(value.opt(key) instanceof Boolean ? value.optBoolean(key) : "unknown");
                line.append(" errorCode=").append(number(value.opt("errorCode")));
                for (String key : new String[]{"buffered", "seekable"}) {
                    line.append(' ').append(key).append('=');
                    JSONArray ranges = value.optJSONArray(key);
                    if (ranges == null) { line.append("unknown"); continue; }
                    line.append('[');
                    for (int i = 0; i < Math.min(8, ranges.length()); i++) {
                        if (i > 0) line.append(',');
                        JSONArray pair = ranges.optJSONArray(i);
                        line.append(pair == null ? "unknown" : number(pair.opt(0)) + ":" + number(pair.opt(1)));
                    }
                    line.append(']');
                }
                JSONObject counts = value.optJSONObject("events");
                for (String key : new String[]{"loadstart", "progress", "suspend", "stalled", "waiting", "playing", "pause", "seeking", "seeked", "ended", "error"})
                    line.append(' ').append(key).append('=').append(number(counts == null ? null : counts.opt(key)));
                trace.accept(line.toString());
            } catch (org.json.JSONException error) { trace.accept("video=bundled event=metadata_invalid"); }
        });
    }
    private static String number(Object value) {
        if (!(value instanceof Number)) return "unknown";
        double number = ((Number) value).doubleValue();
        return Double.isFinite(number) ? Double.toString(number) : "unknown";
    }
}
