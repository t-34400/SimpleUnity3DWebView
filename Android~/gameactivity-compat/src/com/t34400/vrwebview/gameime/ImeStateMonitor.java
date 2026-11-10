/* Copyright 2026 VRWebView contributors. SPDX-License-Identifier: MIT */
package com.t34400.vrwebview.gameime;

import android.content.Context;
import android.text.Editable;
import android.text.Selection;
import android.text.SpannableString;
import android.text.InputType;
import android.util.Log;
import android.view.View;
import android.view.inputmethod.BaseInputConnection;
import android.view.inputmethod.ExtractedText;
import android.view.inputmethod.ExtractedTextRequest;
import android.view.inputmethod.InputConnection;
import android.view.inputmethod.InputMethodManager;

/** Diagnostic addition to the SAME Google connection held by GameActivity/native. */
public final class ImeStateMonitor {
    public interface Owner { ImeStateMonitor vrwebviewImeMonitor(); }
    private static final String TAG = "VRWebViewGameIme";
    private final com.google.androidgamesdk.gametextinput.InputConnection connection;
    private final View view;
    private final InputMethodManager imm;
    private final Runnable flush = this::flushOnUiThread;
    private boolean closed, monitoring, posted, dirty;
    private int token, requestFlags, batchDepth, generation;
    private Snapshot previous;

    public ImeStateMonitor(com.google.androidgamesdk.gametextinput.InputConnection connection, View view) {
        this.connection = connection;
        this.view = view;
        imm = (InputMethodManager) view.getContext().getSystemService(Context.INPUT_METHOD_SERVICE);
        Log.i(TAG, "event=connection_patch revision=1 google=4.4.0 sameConnection=true");
    }

    /** Called on the original SurfaceView's return path; never makes a connection. */
    public static void opened(com.google.androidgamesdk.gametextinput.InputConnection connection) {
        if (connection != null) ((Owner) connection).vrwebviewImeMonitor().reopen();
    }

    public synchronized void reopen() {
        if (!closed) return;
        closed = false;
        batchDepth = 0;
        previous = null;
        Log.i(TAG, "event=connection_reopened");
    }

    public void activate(boolean active) {
        if (active) reopen(); else close();
    }

    /** restartInput invalidates a previous IME's monitoring token, not the editor. */
    public synchronized void restart() {
        monitoring = false;
        previous = null;
        generation++;
    }

    public synchronized void begin() { if (!closed) batchDepth++; }

    public synchronized boolean end() {
        if (closed) return false;
        if (batchDepth > 0) batchDepth--;
        dirty = true;
        schedule();
        return batchDepth > 0;
    }

    public synchronized void changed() {
        if (closed) return;
        dirty = true;
        schedule();
    }

    private void schedule() {
        if (closed || posted || batchDepth != 0 || !dirty) return;
        posted = true;
        if (!view.post(flush)) posted = false;
    }

    public synchronized void close() {
        closed = true;
        monitoring = posted = dirty = false;
        batchDepth = 0;
        previous = null;
        generation++;
        view.removeCallbacks(flush);
        Log.i(TAG, "event=connection_closed");
    }

    /** Framework calls use the original connection's editable, selection and spans. */
    public synchronized ExtractedText extract(ExtractedTextRequest request, int flags) {
        if (closed || request == null) return null;
        Snapshot current = snapshot();
        if (current == null) return null;
        monitoring = (flags & InputConnection.GET_EXTRACTED_TEXT_MONITOR) != 0;
        token = request.token;
        requestFlags = request.flags;
        previous = current;
        generation++;
        Log.i(TAG, "event=extract_request monitor=" + monitoring + " length=" + current.text.length());
        return current.extracted(requestFlags);
    }

    private void flushOnUiThread() {
        final Snapshot current;
        final boolean sendExtract;
        final int currentToken, currentFlags, currentGeneration;
        synchronized (this) {
            posted = false;
            if (closed || batchDepth != 0 || !dirty) return;
            dirty = false;
            if (!view.isAttachedToWindow()) { restart(); return; }
            current = snapshot();
            if (current == null || current.sameAs(previous)) return;
            previous = current;
            sendExtract = monitoring;
            currentToken = token;
            currentFlags = requestFlags;
            currentGeneration = generation;
        }
        // The framework accepts these public notifications while the overlay owns
        // XR input focus. Do not request focus or replace/restart the connection.
        synchronized (this) {
            if (closed || currentGeneration != generation || imm == null) return;
            if (sendExtract) {
                imm.updateExtractedText(view, currentToken, current.extracted(currentFlags));
                Log.i(TAG, "event=extract_update length=" + current.text.length());
            }
            imm.updateSelection(view, current.start, current.end, current.composingStart, current.composingEnd);
            Log.i(TAG, "event=selection_update length=" + current.text.length());
        }
    }

    private Snapshot snapshot() {
        Editable editable = connection.getEditable();
        if (editable == null) return null;
        int length = editable.length();
        int start = Math.max(0, Math.min(length, Selection.getSelectionStart(editable)));
        int end = Math.max(0, Math.min(length, Selection.getSelectionEnd(editable)));
        return new Snapshot(new SpannableString(editable), start, end,
            BaseInputConnection.getComposingSpanStart(editable), BaseInputConnection.getComposingSpanEnd(editable),
            (connection.getEditorInfo().inputType & InputType.TYPE_TEXT_FLAG_MULTI_LINE) == 0);
    }

    private static final class Snapshot {
        final SpannableString text;
        final int start, end, composingStart, composingEnd;
        final boolean singleLine;
        Snapshot(SpannableString text, int start, int end, int composingStart, int composingEnd, boolean singleLine) {
            this.text = text; this.start = start; this.end = end;
            this.composingStart = composingStart; this.composingEnd = composingEnd; this.singleLine = singleLine;
        }
        boolean sameAs(Snapshot other) {
            return other != null && text.toString().equals(other.text.toString())
                && start == other.start && end == other.end && composingStart == other.composingStart
                && composingEnd == other.composingEnd && singleLine == other.singleLine;
        }
        ExtractedText extracted(int flags) {
            ExtractedText result = new ExtractedText();
            result.text = (flags & InputConnection.GET_TEXT_WITH_STYLES) != 0 ? new SpannableString(text) : text.toString();
            result.startOffset = 0;
            result.partialStartOffset = result.partialEndOffset = -1; // Whole document, never a partial replacement.
            result.selectionStart = start;
            result.selectionEnd = end;
            result.flags = singleLine ? ExtractedText.FLAG_SINGLE_LINE : 0;
            return result;
        }
    }
}
