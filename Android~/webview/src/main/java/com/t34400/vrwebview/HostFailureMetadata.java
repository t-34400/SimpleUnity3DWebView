package com.t34400.vrwebview;

import java.util.ArrayList;
import java.util.List;

/** Initialization diagnosis without Throwable messages, cause messages, or application data. */
final class HostFailureMetadata {
    static List<String> describe(Throwable failure, String stage) {
        List<String> rows = new ArrayList<>();
        rows.add("event=host_start_failed stage=" + stage + " error=" + failure.getClass().getSimpleName());
        StackTraceElement[] trace = failure.getStackTrace();
        for (int i = 0; i < Math.min(8, trace.length); i++) {
            StackTraceElement frame = trace[i];
            rows.add("event=host_failure_frame index=" + i + " class=" + frame.getClassName()
                    + " method=" + frame.getMethodName() + " line=" + frame.getLineNumber());
        }
        return rows;
    }
}
