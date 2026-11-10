package com.t34400.vrwebview;

import android.webkit.WebView;

/** UI-thread content controller. At most one asynchronous native find is in flight. */
final class BrowserContent implements WebView.FindListener {
    interface Results { void found(long revision, int active, int count, boolean complete); }
    private final WebView web;
    private final Results results;
    private boolean running, closed;
    private String queued;
    private long revision, queuedRevision;
    BrowserContent(WebView web, Results results) { this.web = web; this.results = results; web.setFindListener(this); }
    void find(String query, long version) {
        if (closed) return;
        queued = query; queuedRevision = version;
        if (!running) start();
    }
    private void start() {
        String query = queued; revision = queuedRevision; queued = null; running = true;
        web.findAllAsync(query);
    }
    void clear() { queued = null; revision = -1; web.clearMatches(); }
    void next(boolean forward) { if (!running && !closed) web.findNext(forward); }
    @Override public void onFindResultReceived(int active, int count, boolean done) {
        if (closed) return;
        if (revision >= 0) results.found(revision, count == 0 ? 0 : active + 1, count, done);
        if (done) { running = false; if (queued != null) start(); }
    }
    void close() { closed = true; queued = null; web.setFindListener(null); }
}
