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
        @Implementation protected void updateExtractedText(View v, int token, ExtractedText text) {
            tokens.add(token); texts.add(text.text.toString());
        }
        @Implementation protected void updateSelection(View v, int a, int b, int c, int d) { selections++; }
    }
    @Before public void setup() {
        Imm.tokens=new ArrayList<>(); Imm.texts=new ArrayList<>(); Imm.selections=0;
        Activity activity=Robolectric.buildActivity(Activity.class).setup().visible().get();
        view=new View(activity); activity.setContentView(view);
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
        ImeStateMonitor.opened(connection); extract(8,true); connection.commitText("b",1); idle();
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
}
