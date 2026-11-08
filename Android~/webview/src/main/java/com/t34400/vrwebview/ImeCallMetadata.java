package com.t34400.vrwebview;

import java.util.ArrayList;
import java.util.List;

/** Bounded public proxy-query call sites; no exception message, file path, arguments or input text. */
final class ImeCallMetadata {
    static List<String> describe(StackTraceElement[] stack) {
        List<String> rows = new ArrayList<>();
        for (StackTraceElement frame : stack) {
            if ((frame.getClassName().equals("com.t34400.vrwebview.VulkanProbe$Host") && frame.getMethodName().equals("updateStatus"))
                    || (frame.getClassName().equals("com.t34400.vrwebview.ImeProxyProbe") && frame.getMethodName().equals("show")))
                return rows; // Our own isActive diagnostics must not exhaust the call-site budget.
        }
        for (int i = 0; i < Math.min(16, stack.length); i++) {
            StackTraceElement frame = stack[i];
            rows.add("index=" + i + " class=" + frame.getClassName() + " method=" + frame.getMethodName()
                    + " line=" + frame.getLineNumber());
        }
        return rows;
    }
}
