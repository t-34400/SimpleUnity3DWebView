package com.t34400.vrwebview;

import java.lang.reflect.InvocationTargetException;
import java.lang.reflect.Method;
import java.lang.reflect.Proxy;
import java.util.concurrent.atomic.AtomicBoolean;

/** Probe-only public-interface delegation. No Android dependency and no ownership of the source. */
final class BorrowedInputConnection {
    interface Observer {
        void onCall(long generation, String operation, String outcome);
        default void onEditorAction(long generation, int action) {}
    }
    private volatile Object source;
    final long generation;

    BorrowedInputConnection(long generation, Object source) {
        if (generation <= 0 || source == null) throw new IllegalArgumentException("Invalid connection lease");
        this.generation = generation; this.source = source;
    }
    void invalidate() { source = null; }
    boolean isValid() { return source != null; }

    <T> T create(Class<T> contract, Observer observer) {
        Object target = source;
        if (!contract.isInterface() || target == null || !contract.isInstance(target))
            throw new IllegalArgumentException("Source must implement the public interface");
        AtomicBoolean closed = new AtomicBoolean();
        Object wrapper = Proxy.newProxyInstance(contract.getClassLoader(), new Class<?>[] { contract }, (proxy, method, args) -> {
            if (method.getDeclaringClass() == Object.class) {
                if (method.getName().equals("toString")) return "VRWebView borrowed input connection";
                if (method.getName().equals("hashCode")) return System.identityHashCode(proxy);
                if (method.getName().equals("equals")) return args != null && args.length == 1 && proxy == args[0];
            }
            if (method.getName().equals("closeConnection") && method.getParameterCount() == 0) {
                closed.set(true); observe(observer, method, "borrow_closed");
                return null; // The WebView, not the proxy, owns the source connection.
            }
            Object current = source;
            if (closed.get() || current == null) {
                observe(observer, method, "stale"); return empty(method.getReturnType());
            }
            try {
                // Keep the source getHandler and calling-thread contract. Never marshal synchronously to UI.
                Object result = method.invoke(current, args);
                observe(observer, method, result instanceof Boolean ? result.toString() : result == null ? "null" : "returned");
                if (observer != null && method.getName().equals("performEditorAction")
                        && args != null && args.length == 1 && args[0] instanceof Integer) {
                    try { observer.onEditorAction(generation, (Integer) args[0]); } catch (RuntimeException ignored) {}
                }
                return result;
            } catch (InvocationTargetException | IllegalAccessException error) {
                // No exception messages, arguments, text, EditorInfo, or return objects enter diagnostics.
                observe(observer, method, "delegate_error"); return empty(method.getReturnType());
            }
        });
        return contract.cast(wrapper);
    }
    private void observe(Observer observer, Method method, String outcome) {
        if (observer == null) return;
        try { observer.onCall(generation, method.getName(), outcome); }
        catch (RuntimeException ignored) { /* Diagnostics must not change IME behavior. */ }
    }
    private static Object empty(Class<?> type) {
        if (!type.isPrimitive() || type == void.class) return null;
        if (type == boolean.class) return false;
        if (type == int.class) return 0;
        if (type == long.class) return 0L;
        if (type == float.class) return 0f;
        if (type == double.class) return 0d;
        if (type == short.class) return (short) 0;
        if (type == byte.class) return (byte) 0;
        return (char) 0;
    }
}
