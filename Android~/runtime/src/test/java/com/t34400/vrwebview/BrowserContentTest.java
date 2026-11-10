package com.t34400.vrwebview;

import android.app.Activity;
import android.content.Context;
import android.os.Looper;
import android.webkit.WebView;
import java.util.ArrayList;
import java.util.List;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.Robolectric;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.RuntimeEnvironment;
import org.robolectric.annotation.Config;
import org.robolectric.annotation.LooperMode;
import org.robolectric.android.controller.ActivityController;
import static org.junit.Assert.*;
import static org.robolectric.Shadows.shadowOf;

@RunWith(RobolectricTestRunner.class) @Config(sdk = 29) @LooperMode(LooperMode.Mode.PAUSED)
public class BrowserContentTest {
    private static final class RecordingWebView extends WebView {
        final List<String> queries = new ArrayList<>(); int next, clear;
        RecordingWebView(Context context) { super(context); }
        @Override public void findAllAsync(String query) { queries.add(query); }
        @Override public void findNext(boolean forward) { next += forward ? 1 : -1; }
        @Override public void clearMatches() { clear++; }
    }
    @Test public void queriesAreSerializedAndOnlyLatestQueuedQueryStarts() {
        RecordingWebView web = new RecordingWebView(RuntimeEnvironment.getApplication());
        List<String> replies = new ArrayList<>();
        BrowserContent content = new BrowserContent(web, (revision, active, count, done) -> replies.add(revision + ":" + active + ":" + count));
        content.find("first", 1); content.find("obsolete", 2); content.find("latest", 3);
        assertEquals(1, web.queries.size()); content.onFindResultReceived(0, 2, false);
        assertEquals("1:1:2", replies.get(0)); content.onFindResultReceived(1, 2, true);
        assertEquals(2, web.queries.size()); assertEquals("latest", web.queries.get(1));
        content.onFindResultReceived(0, 0, true); assertEquals("3:0:0", replies.get(replies.size() - 1));
        content.next(true); assertEquals(1, web.next); content.close(); web.destroy();
    }
    @Test public void clearedAndClosedFindCannotPublishObsoleteMatches() {
        RecordingWebView web = new RecordingWebView(RuntimeEnvironment.getApplication());
        List<Long> replies = new ArrayList<>(); BrowserContent content = new BrowserContent(web, (revision, active, count, done) -> replies.add(revision));
        content.find("first", 1); content.find("second", 2); content.clear();
        content.onFindResultReceived(0, 1, true); assertTrue(replies.isEmpty()); assertEquals(1, web.queries.size());
        content.find("third", 3); content.close(); content.onFindResultReceived(0, 1, true);
        assertTrue(replies.isEmpty()); assertEquals(1, web.clear); web.destroy();
    }
    @Test public void newFindAfterClearDoesNotWaitForCancelledSearchCompletion() {
        RecordingWebView web = new RecordingWebView(RuntimeEnvironment.getApplication());
        List<Long> replies = new ArrayList<>(); BrowserContent content = new BrowserContent(web, (revision, active, count, done) -> replies.add(revision));
        content.find("old", 1); content.clear(); content.find("new", 2);
        assertEquals(2, web.queries.size()); content.onFindResultReceived(0, 1, true);
        assertEquals(Long.valueOf(2), replies.get(0)); content.close(); web.destroy();
    }
    @Test public void pageWithoutGpuOutputAcceptsContentCommandsAndReportsInvalidBounds() {
        try (ActivityController<Activity> activity = Robolectric.buildActivity(Activity.class).setup().visible()) {
            Result callback = new Result(); BrowserHost.openPage(activity.get(), 411, 128, 128, true, false, callback); idle();
            assertEquals(1, callback.ready); assertEquals(0, callback.failed);
            BrowserHost.textZoom(411, 1, 140); idle(); assertEquals(1, callback.ok);
            WebView web = findWebView(activity.get().getWindow().getDecorView()); assertNotNull(web); assertEquals(140, web.getSettings().getTextZoom());
            BrowserHost.evaluate(411, 5, "document.title"); idle();
            assertEquals("document.title", shadowOf(web).getLastEvaluatedJavascript());
            shadowOf(web).getLastEvaluatedJavascriptCallback().onReceiveValue("\"日本語\"");
            assertEquals("\"日本語\"", callback.scriptResult);
            BrowserHost.textZoom(411, 2, 201); BrowserHost.find(411, 3, null, 2); BrowserHost.evaluate(411, 4, null); idle();
            assertEquals(2, callback.bad); assertEquals(1, callback.badScript); assertEquals(140, web.getSettings().getTextZoom());
            BrowserHost.close(411); idle();
        }
    }
    private static WebView findWebView(android.view.View view) {
        if (view instanceof WebView) return (WebView) view;
        if (view instanceof android.view.ViewGroup) { android.view.ViewGroup group = (android.view.ViewGroup) view;
            for (int i = 0; i < group.getChildCount(); i++) { WebView result = findWebView(group.getChildAt(i)); if (result != null) return result; }
        } return null;
    }
    private static void idle() { shadowOf(Looper.getMainLooper()).idle(); }
    private static final class Result implements BrowserHost.Callback {
        int ready, failed, ok, bad, badScript; String scriptResult;
        public void ready() { ready++; }
        public void failed(String code) { failed++; }
        public void completed(long operation, boolean success, String code) { if (success) ok++; else bad++; }
        public void pageState(String address, String title, boolean back, boolean forward, boolean loading, int progress, int error) { }
        public void evaluated(long operation, String result, boolean success) { if (!success) badScript++; else scriptResult = result; }
        public void found(long revision, int active, int count, boolean complete) { }
        public void context(String address, boolean image) { }
    }
}
