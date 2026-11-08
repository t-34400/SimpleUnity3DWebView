package com.t34400.vrwebview;

import java.util.List;

public final class ImeCallMetadataTests {
    private static int checks;
    private static void check(boolean value) {
        if (!value) throw new AssertionError("IME call metadata check " + (checks + 1));
        checks++;
    }
    public static void main(String[] args) {
        StackTraceElement imm = new StackTraceElement("android.view.inputmethod.InputMethodManager", "isActive", "/private/path/DO_NOT_LOG", 1794);
        StackTraceElement chromium = new StackTraceElement("org.chromium.content.browser.input.ImeAdapterImpl", "hideKeyboard", "DO_NOT_LOG", 765);
        List<String> rows = ImeCallMetadata.describe(new StackTraceElement[]{imm, chromium});
        check(rows.size() == 2);
        check(rows.get(1).contains("method=hideKeyboard"));
        check(rows.stream().noneMatch(r -> r.contains("DO_NOT_LOG") || r.contains("/private/path")));
        check(ImeCallMetadata.describe(new StackTraceElement[]{imm,
                new StackTraceElement("com.t34400.vrwebview.VulkanProbe$Host", "updateStatus", "ignored", 0)}).isEmpty());
        check(ImeCallMetadata.describe(new StackTraceElement[]{imm,
                new StackTraceElement("com.t34400.vrwebview.ImeProxyProbe", "show", "ignored", 0)}).isEmpty());
        StackTraceElement[] longStack = new StackTraceElement[100];
        java.util.Arrays.fill(longStack, imm);
        check(ImeCallMetadata.describe(longStack).size() == 16);
        check(ImeCallMetadata.describe(new StackTraceElement[0]).isEmpty());
        System.out.println("PASS " + checks + " IME call metadata checks (caller preservation, diagnostic filtering, path exclusion and bounds)");
    }
}
