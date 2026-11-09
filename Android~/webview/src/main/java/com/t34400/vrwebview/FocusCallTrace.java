package com.t34400.vrwebview;

import java.util.function.Consumer;

/** UI-thread focus event stacks. Observes existing callbacks; never changes focus or Window callbacks. */
final class FocusCallTrace {
    private final Consumer<String> output;
    private final int maxEvents, maxFrames;
    private int events;
    FocusCallTrace(Consumer<String> output) { this(output, 64, 48); }
    FocusCallTrace(Consumer<String> output, int maxEvents, int maxFrames) {
        if (maxEvents < 0 || maxFrames < 0) throw new IllegalArgumentException("Invalid trace bounds");
        this.output = output; this.maxEvents = maxEvents; this.maxFrames = maxFrames;
    }
    void record(String kind, String metadata, StackTraceElement[] stack) {
        if (events >= maxEvents) return;
        int sequence = ++events;
        emit("probe=FO1 event=focus_call seq=" + sequence + " kind=" + kind + " " + metadata);
        for (int i = 0; i < Math.min(maxFrames, stack.length); i++) {
            StackTraceElement frame = stack[i];
            emit("probe=FO1 event=focus_frame seq=" + sequence + " index=" + i
                    + " class=" + frame.getClassName() + " method=" + frame.getMethodName() + " line=" + frame.getLineNumber());
        }
    }
    private void emit(String line) {
        try { output.accept(line); } catch (RuntimeException ignored) { /* Diagnostics must not interrupt focus dispatch. */ }
    }
}
