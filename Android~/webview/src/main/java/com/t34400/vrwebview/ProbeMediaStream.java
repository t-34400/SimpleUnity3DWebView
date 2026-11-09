package com.t34400.vrwebview;

import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.function.Consumer;

/** Byte-preserving diagnostic stream for bundled media, not a network transport. */
final class ProbeMediaStream extends InputStream {
    /** WebView's AndroidStreamReaderURLLoader applies the request's offset itself.
     * Supply a zero-based prefix through the requested end, not an already sliced body.
     * The prefix end also bounds readers which continue until EOF for a closed range.
     */
    static ProbeMediaStream intercepted(byte[] data, ProbeRange range, Consumer<String> trace) {
        int prefixLength = range.status == 416 ? 0 : Math.toIntExact(range.start + range.length);
        if (prefixLength < 0 || prefixLength > data.length) throw new IllegalArgumentException("Invalid resource range");
        return new ProbeMediaStream(data, 0, prefixLength, trace);
    }
    private final ByteArrayInputStream source;
    private final Consumer<String> trace;
    private final int length;
    private long readBytes, skippedBytes, lastReport;
    private int resets;
    private boolean firstRead, eof, closed;

    ProbeMediaStream(byte[] data, int start, int length, Consumer<String> trace) {
        source = new ByteArrayInputStream(data, start, length);
        this.length = length; this.trace = trace;
    }
    @Override public synchronized int read() {
        int result = source.read();
        record(result < 0 ? -1 : 1); return result;
    }
    @Override public synchronized int read(byte[] buffer, int offset, int count) {
        int result = source.read(buffer, offset, count);
        record(result); return result;
    }
    @Override public synchronized long skip(long count) {
        long result = source.skip(count); skippedBytes += result; return result;
    }
    @Override public synchronized int available() { return source.available(); }
    @Override public boolean markSupported() { return true; }
    @Override public synchronized void mark(int limit) { source.mark(limit); }
    @Override public synchronized void reset() { source.reset(); resets++; eof = false; }
    @Override public synchronized void close() throws IOException {
        source.close();
        if (!closed) { closed = true; emit("close"); }
    }
    private void record(int count) {
        if (count > 0) {
            readBytes += count;
            if (!firstRead) { firstRead = true; emit("first_read"); }
            if (source.available() == 0 || readBytes - lastReport >= 262144) {
                lastReport = readBytes; emit(source.available() == 0 ? "data_exhausted" : "read_progress");
            }
        } else if (count < 0 && !eof) { eof = true; emit("eof"); }
    }
    private void emit(String event) {
        try {
            trace.accept("event=" + event + " readBytes=" + readBytes + " skippedBytes=" + skippedBytes
                    + " remaining=" + source.available() + " streamBytes=" + length + " resets=" + resets);
        } catch (RuntimeException ignored) { /* Diagnostic failure must not interrupt delivery. */ }
    }
}
