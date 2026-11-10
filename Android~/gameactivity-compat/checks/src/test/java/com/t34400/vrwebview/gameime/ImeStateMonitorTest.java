package com.t34400.vrwebview.gameime;

import static org.junit.Assert.*;
import android.app.Activity;
import android.os.Looper;
import android.text.InputType;
import android.text.Selection;
import android.view.View;
import android.view.inputmethod.*;
import androidx.core.graphics.Insets;
import com.google.androidgamesdk.gametextinput.Settings;
import com.google.androidgamesdk.gametextinput.State;
import com.google.androidgamesdk.gametextinput.Listener;
import org.junit.*;
import org.junit.runner.RunWith;
import org.robolectric.*;
import org.robolectric.annotation.*;
import org.robolectric.shadows.ShadowInputMethodManager;
import java.util.*;
import java.time.Duration;

@RunWith(RobolectricTestRunner.class)
@Config(sdk=29, shadows=ImeStateMonitorTest.Imm.class)
@LooperMode(LooperMode.Mode.PAUSED)
public class ImeStateMonitorTest {
    com.google.androidgamesdk.gametextinput.InputConnection connection;
    View view;
    int delivered;
    @Implements(InputMethodManager.class)
    public static class Imm extends ShadowInputMethodManager {
        static List<Integer> tokens;
        static List<String> texts;
        static int selections;
        static List<String> activation;
        static Runnable onRestart;
        static boolean acceptShow;
        @Implementation protected void restartInput(View v) {
            activation.add("restart"); if (onRestart != null) onRestart.run();
        }
        @Implementation protected boolean isActive(View v) { return true; }
        @Implementation protected boolean showSoftInput(View v, int flags) { activation.add("show"); return acceptShow; }
        @Implementation protected boolean hideSoftInputFromWindow(android.os.IBinder token, int flags) { activation.add("hide"); return true; }
        @Implementation protected void updateExtractedText(View v, int token, ExtractedText text) {
            tokens.add(token); texts.add(text.text.toString());
        }
        @Implementation protected void updateSelection(View v, int a, int b, int c, int d) { selections++; }
    }
    @Before public void setup() {
        Imm.tokens=new ArrayList<>(); Imm.texts=new ArrayList<>(); Imm.selections=0;
        Imm.activation=new ArrayList<>(); Imm.onRestart=null; Imm.acceptShow=true;
        org.robolectric.android.controller.ActivityController<Activity> controller=Robolectric.buildActivity(Activity.class).setup();
        Activity activity=controller.get();
        view=new View(activity); activity.setContentView(view);
        controller.visible().windowFocusChanged(true);
        EditorInfo info=new EditorInfo(); info.inputType=InputType.TYPE_CLASS_TEXT;
        connection=new com.google.androidgamesdk.gametextinput.InputConnection(activity,view,new Settings(info,false));
        connection.setListener(new Listener() {
            public void stateChanged(State s, boolean dismissed) { delivered++; }
            public void onImeInsetsChanged(Insets i) { }
            public void onSoftwareKeyboardVisibilityChanged(boolean v) { }
            public void onEditorAction(int a) { }
        });
        Selection.setSelection(connection.getEditable(),0);
        idle();
    }
    void idle() { Shadows.shadowOf(Looper.getMainLooper()).idle(); }
    ExtractedText extract(int token, boolean monitor) {
        ExtractedTextRequest req=new ExtractedTextRequest(); req.token=token;
        return connection.getExtractedText(req,monitor?InputConnection.GET_EXTRACTED_TEXT_MONITOR:0);
    }
    @Test public void extractionUsesTheOriginalEditableWithoutReplacement() {
        Object editable=connection.getEditable(); connection.commitText("日本語",1); idle();
        ExtractedText result=extract(7,true);
        assertEquals("日本語",result.text.toString()); assertSame(editable,connection.getEditable());
        assertEquals(3,result.selectionStart); assertEquals(-1,result.partialStartOffset);
        assertTrue(connection instanceof ImeStateMonitor.Owner);
    }
    @Test public void monitoredCompositionUpdatesImeAndStillDeliversToListener() {
        extract(7,true); connection.beginBatchEdit(); connection.setComposingText("にほん",1);
        idle(); assertEquals(0,Imm.tokens.size());
        connection.endBatchEdit(); idle();
        assertEquals(Arrays.asList(7),Imm.tokens); assertEquals(Arrays.asList("にほん"),Imm.texts);
        assertEquals(1,Imm.selections); assertTrue(delivered>0);
        connection.commitText("日本",1); idle(); assertEquals("日本",Imm.texts.get(1));
    }
    @Test public void unmonitoredExtractionDoesNotSubscribeToFullTextUpdates() {
        assertNotNull(extract(1,false)); connection.commitText("a",1); idle();
        assertTrue(Imm.tokens.isEmpty()); assertTrue(Imm.selections>0);
    }
    @Test public void nestedBatchesFlushOnlyAfterTheOutermostEnd() {
        extract(3,true); assertTrue(connection.beginBatchEdit()); assertTrue(connection.beginBatchEdit());
        connection.commitText("a",1); assertTrue(connection.endBatchEdit()); idle();
        assertTrue(Imm.tokens.isEmpty()); assertFalse(connection.endBatchEdit()); idle();
        assertEquals(Arrays.asList("a"),Imm.texts);
    }
    @Test public void closeCancelsPendingNotificationsAndReopenDoesNotReuseTheToken() {
        extract(4,true); connection.commitText("a",1); connection.closeConnection(); idle();
        assertTrue(Imm.tokens.isEmpty()); assertNull(extract(4,true));
        ImeStateMonitor.opened(connection, connection.getEditorInfo()); extract(8,true); connection.commitText("b",1); idle();
        assertEquals(Arrays.asList(8),Imm.tokens); assertEquals(Arrays.asList("ab"),Imm.texts);
    }
    @Test public void restartInvalidatesThePreviousMonitorUntilImeResubscribes() {
        extract(4,true); connection.restartInput(); connection.commitText("a",1); idle();
        assertTrue(Imm.tokens.isEmpty()); extract(9,true); connection.commitText("b",1); idle();
        assertEquals(Arrays.asList(9),Imm.tokens);
    }
    @Test public void latestRequestTokenIsUsedAndGetterDoesNotChangeSelection() {
        connection.commitText("abc",1); idle(); connection.setSelection(1,2); idle();
        extract(4,true); assertEquals(1,Selection.getSelectionStart(connection.getEditable()));
        extract(9,true); connection.commitText("Z",1); idle();
        assertEquals(Arrays.asList(9),Imm.tokens); assertEquals(Arrays.asList("aZc"),Imm.texts);
    }
    @Test public void deletionAndSelectionOnlyChangesReachTheMonitor() {
        connection.commitText("a😀b",1); idle(); extract(4,true);
        connection.deleteSurroundingTextInCodePoints(2,0); idle();
        assertEquals("a",Imm.texts.get(0));
        connection.setSelection(0,0); idle(); assertEquals(2,Imm.tokens.size());
    }
    @Test public void unchangedStateIsNotBroadcastRepeatedly() {
        connection.commitText("a",1); idle(); extract(2,true);
        connection.setSelection(1,1); connection.finishComposingText(); idle();
        assertTrue(Imm.tokens.isEmpty());
    }
    @Test public void nativeSetStateStillUsesTheSameEditableAndInvalidatesOldMonitor() {
        Object editable=connection.getEditable(); extract(1,true);
        connection.setState(new State("日本語",3,3,-1,-1)); idle();
        assertSame(editable,connection.getEditable()); assertTrue(Imm.tokens.isEmpty());
        assertEquals("日本語",extract(2,true).text.toString());
        connection.commitText("入力",1); idle(); assertEquals(Arrays.asList("日本語入力"),Imm.texts);
    }
    @Test public void showFollowsRestartAndTheActualConnectionCallbackExactlyOnce() {
        Imm.onRestart=() -> ImeStateMonitor.opened(connection, connection.getEditorInfo());
        connection.setSoftKeyboardActive(true,0);
        assertEquals(Arrays.asList("restart"),Imm.activation);
        idle(); assertEquals(Arrays.asList("restart","show"),Imm.activation);
        ImeStateMonitor.opened(connection,connection.getEditorInfo()); idle();
        assertEquals(Arrays.asList("restart","show"),Imm.activation);
    }
    @Test public void showWaitsForAnAsynchronousConnectionCallbackWithoutPolling() {
        connection.setSoftKeyboardActive(true,0); idle();
        assertEquals(Arrays.asList("restart"),Imm.activation);
        ImeStateMonitor.opened(connection,connection.getEditorInfo()); idle();
        assertEquals(Arrays.asList("restart","show"),Imm.activation);
    }
    @Test public void typeNullConnectionCannotTriggerTheShow() {
        connection.setSoftKeyboardActive(true,0);
        EditorInfo info=new EditorInfo(); info.inputType=0;
        ImeStateMonitor.opened(connection,info); idle();
        assertEquals(Arrays.asList("restart"),Imm.activation);
        ImeStateMonitor.opened(connection,connection.getEditorInfo()); idle();
        assertEquals(Arrays.asList("restart","show"),Imm.activation);
    }
    @Test public void hideCancelsAQueuedShowAndStillRestartsTheHiddenEditor() {
        connection.setSoftKeyboardActive(true,0); connection.setSoftKeyboardActive(false,0);
        ImeStateMonitor.opened(connection,connection.getEditorInfo()); idle();
        assertEquals(Arrays.asList("restart","hide","restart"),Imm.activation);
    }
    @Test public void closeDuringRestartRetainsOnlyTheStillActiveShowIntent() {
        connection.setSoftKeyboardActive(true,0); connection.closeConnection(); idle();
        assertEquals(Arrays.asList("restart"),Imm.activation);
        ImeStateMonitor.opened(connection,connection.getEditorInfo()); idle();
        assertEquals(Arrays.asList("restart","show"),Imm.activation);
    }
    @Test public void activationReplacementDoesNotSendTheOldQueuedShow() {
        Imm.onRestart=() -> ImeStateMonitor.opened(connection,connection.getEditorInfo());
        connection.setSoftKeyboardActive(true,0); connection.setSoftKeyboardActive(true,0); idle();
        assertEquals(Arrays.asList("restart","restart","show"),Imm.activation);
    }
    @Test public void missingConnectionExpiresWithoutAnUnconditionalDelayedShow() {
        connection.setSoftKeyboardActive(true,0);
        Shadows.shadowOf(Looper.getMainLooper()).idleFor(Duration.ofMillis(2100));
        ImeStateMonitor.opened(connection,connection.getEditorInfo()); idle();
        assertEquals(Arrays.asList("restart"),Imm.activation);
    }
    @Test public void aRejectedShowIsNotRetriedByLaterCallbacks() {
        Imm.acceptShow=false;
        Imm.onRestart=() -> ImeStateMonitor.opened(connection,connection.getEditorInfo());
        connection.setSoftKeyboardActive(true,0); idle();
        ImeStateMonitor.opened(connection,connection.getEditorInfo()); idle();
        assertEquals(Arrays.asList("restart","show"),Imm.activation);
    }

    @Test public void changedEditorMetadataRequiresANewConnectionCallback() {
        Imm.onRestart=() -> ImeStateMonitor.opened(connection,connection.getEditorInfo());
        connection.setSoftKeyboardActive(true,0);
        EditorInfo changed=new EditorInfo(); changed.inputType=InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS;
        connection.setEditorInfo(changed); idle();
        assertEquals(Arrays.asList("restart"),Imm.activation);
        connection.restartInput(); idle();
        assertEquals(Arrays.asList("restart","restart","show"),Imm.activation);
    }
    @Test public void nullAttributesLookupIsNotAConnectionReadinessSignal() {
        connection.setSoftKeyboardActive(true,0);
        ImeStateMonitor.opened(connection,null); idle();
        assertEquals(Arrays.asList("restart"),Imm.activation);
        ImeStateMonitor.opened(connection,connection.getEditorInfo()); idle();
        assertEquals(Arrays.asList("restart","show"),Imm.activation);
    }

    void activatePopulatedEditor() {
        connection.setState(new State("https://example.com/",20,20,-1,-1));
        Imm.onRestart=() -> ImeStateMonitor.opened(connection,connection.getEditorInfo());
        connection.setSoftKeyboardActive(true,0); idle();
        extract(42,true);
        // Model Android creating the new binding and then closing its old remote
        // wrapper around Google's SAME retained connection on a later restart.
        Imm.onRestart=() -> {
            ImeStateMonitor.opened(connection,connection.getEditorInfo());
            connection.closeConnection();
        };
    }
    @Test public void activeStateKeepsTheImeMonitorWhenReplacingAnExistingUrl() {
        activatePopulatedEditor(); int before=Imm.activation.size();
        String url="www.wikipedia.org";
        connection.setState(new State(url,url.length(),url.length(),-1,-1)); idle();
        assertEquals(Arrays.asList(url),Imm.texts);
        assertEquals(before,Imm.activation.size());
        assertEquals(url,connection.getEditable().toString());
    }
    @Test public void activeSelectionStateKeepsMonitoringWithoutRestarting() {
        activatePopulatedEditor(); int before=Imm.activation.size();
        String text=connection.getEditable().toString();
        connection.setState(new State(text,0,text.length(),-1,-1)); idle();
        assertEquals(Arrays.asList(text),Imm.texts);
        assertEquals(before,Imm.activation.size());
        connection.commitText("日本語",1); idle();
        assertEquals("日本語",Imm.texts.get(Imm.texts.size()-1));
    }
    @Test public void changedInputAttributesStillRestartAndInvalidateTheOldMonitor() {
        activatePopulatedEditor(); Imm.onRestart=null; int before=Imm.activation.size();
        EditorInfo changed=new EditorInfo(); changed.inputType=InputType.TYPE_CLASS_NUMBER;
        connection.setEditorInfo(changed);
        connection.setState(new State("123",3,3,-1,-1)); idle();
        assertEquals(before+1,Imm.activation.size());
        assertTrue(Imm.tokens.isEmpty());
    }
    @Test public void deactivationStillDiscardsTheMonitorBeforeTheNextEditor() {
        activatePopulatedEditor(); connection.setSoftKeyboardActive(false,0);
        connection.setState(new State("later",5,5,-1,-1)); idle();
        assertTrue(Imm.tokens.isEmpty());
    }
    @Test public void unchangedActiveStateDoesNotLoopOrRestart() {
        activatePopulatedEditor(); int before=Imm.activation.size();
        String text=connection.getEditable().toString();
        for(int i=0;i<4;i++) { connection.setState(new State(text,text.length(),text.length(),-1,-1)); idle(); }
        assertEquals(before,Imm.activation.size());
        assertTrue(Imm.tokens.isEmpty());
    }

    @Test public void activeCompositionStatePreservesTheComposingRangeAndMonitor() {
        activatePopulatedEditor(); int before=Imm.activation.size();
        String text="日本語入力";
        connection.setState(new State(text,text.length(),text.length(),3,5)); idle();
        assertEquals(Arrays.asList(text),Imm.texts);
        assertEquals(3,BaseInputConnection.getComposingSpanStart(connection.getEditable()));
        assertEquals(5,BaseInputConnection.getComposingSpanEnd(connection.getEditable()));
        assertEquals(before,Imm.activation.size());
    }
    @Test public void changedEditorActionStillRequiresRestart() {
        activatePopulatedEditor(); Imm.onRestart=null; int before=Imm.activation.size();
        EditorInfo changed=new EditorInfo(); changed.inputType=InputType.TYPE_CLASS_TEXT; changed.actionId=77;
        connection.setEditorInfo(changed);
        connection.setState(new State("next",4,4,-1,-1)); idle();
        assertEquals(before+1,Imm.activation.size());
        assertTrue(Imm.tokens.isEmpty());
    }

    @Test public void activeKeyEventKeepsTheImeMonitorWithoutRestarting() {
        activatePopulatedEditor();
        connection.setSelection(0,connection.getEditable().length()); idle();
        Imm.texts.clear(); Imm.tokens.clear(); extract(42,true);
        int before=Imm.activation.size();
        assertTrue(connection.onKey(view,android.view.KeyEvent.KEYCODE_W,
            new android.view.KeyEvent(android.view.KeyEvent.ACTION_DOWN,android.view.KeyEvent.KEYCODE_W)));
        idle();
        String current=connection.getEditable().toString();
        assertNotEquals("https://example.com/",current);
        assertEquals(Arrays.asList(current),Imm.texts);
        assertEquals(before,Imm.activation.size());
    }

    @Test public void realGoogleAttributeCopyDoesNotBreakANonzeroLocalAction() {
        EditorInfo local=new EditorInfo(); local.inputType=InputType.TYPE_CLASS_TEXT; local.actionId=77;
        connection.setEditorInfo(local);
        Imm.onRestart=() -> {
            EditorInfo returned=new EditorInfo();
            com.google.androidgamesdk.gametextinput.GameTextInput.copyEditorInfo(connection.getEditorInfo(),returned);
            assertEquals(0,returned.actionId);
            ImeStateMonitor.opened(connection,returned);
        };
        connection.setSoftKeyboardActive(true,0); idle();
        assertEquals(Arrays.asList("restart","show"),Imm.activation);
        extract(42,true);
        connection.setState(new State("abc",3,3,-1,-1)); idle();
        assertEquals(Arrays.asList("restart","show"),Imm.activation);
        assertEquals(Arrays.asList("abc"),Imm.texts);
    }

}
