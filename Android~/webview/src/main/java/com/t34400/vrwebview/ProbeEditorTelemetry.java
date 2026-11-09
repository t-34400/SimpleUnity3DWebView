package com.t34400.vrwebview;

import android.os.SystemClock;
import android.webkit.WebView;
import java.util.function.Consumer;
import org.json.JSONObject;

/** Counters from the fixed probe page only. Never reads or forwards field contents. */
final class ProbeEditorTelemetry {
    private final Consumer<String> trace;
    private long nextPoll, epoch;
    private boolean polling, closed;
    private String last;

    ProbeEditorTelemetry(Consumer<String> trace) { this.trace = trace; }

    void poll(WebView web) {
        long now = SystemClock.uptimeMillis();
        if (closed || polling || now < nextPoll) return;
        nextPoll = now + 1000; polling = true;
        long ticket = epoch;
        web.evaluateJavascript("typeof window.vrwebviewProxyMetadata==='function'?window.vrwebviewProxyMetadata():null", result -> {
            if (closed || ticket != epoch) return;
            polling = false;
            try {
                JSONObject metadata = new JSONObject(result);
                String target = metadata.optString("pointerTarget", "none");
                if (!"none".equals(target) && !"editor".equals(target) && !"media".equals(target) && !"other".equals(target))
                    target = "unknown";
                String value = "input=" + metadata.getLong("input")
                        + " compositionStart=" + metadata.getLong("compositionstart")
                        + " compositionUpdate=" + metadata.getLong("compositionupdate")
                        + " compositionEnd=" + metadata.getLong("compositionend")
                        + " selectionChanges=" + metadata.getLong("selectionchange")
                        + " editable=" + metadata.getBoolean("editable")
                        + " documentFocused=" + metadata.optBoolean("documentFocused", false)
                        + " pointerPolicy=" + metadata.optLong("pointerPolicy", 0)
                        + " pointerTarget=" + target
                        + " pointerDown=" + metadata.optLong("pointerDown", 0)
                        + " editorPointerDown=" + metadata.optLong("editorPointerDown", 0)
                        + " nonEditorPointerDown=" + metadata.optLong("nonEditorPointerDown", 0)
                        + " blurredEditors=" + metadata.optLong("blurredEditors", 0)
                        + " endedEditors=" + metadata.optLong("endedEditors", 0);
                if (!value.equals(last)) { trace.accept("probe=WF1 event=dom route=direct " + value); last = value; }
            } catch (Exception ignored) { /* Not loaded; never log the raw JS response or exception. */ }
        });
    }
    void reset() { epoch++; polling = false; nextPoll = 0; last = null; }
    void close() { closed = true; reset(); }
}
