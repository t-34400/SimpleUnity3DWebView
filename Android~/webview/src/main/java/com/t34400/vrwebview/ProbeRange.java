package com.t34400.vrwebview;

/** Single HTTP byte range for the bundled video; no network or filesystem paths. */
final class ProbeRange {
    final int status;
    final long start, length;
    private ProbeRange(int status, long start, long length) {
        this.status = status; this.start = start; this.length = length;
    }

    static ProbeRange parse(String header, long size) {
        if (size < 0) throw new IllegalArgumentException("Invalid asset size");
        if (header == null) return new ProbeRange(200, 0, size);
        try {
            if (!header.matches("bytes=(?:[0-9]+-[0-9]*|-[0-9]+)") || size == 0)
                return new ProbeRange(416, 0, 0);
            String[] parts = header.substring(6).split("-", -1);
            long start, end;
            if (parts[0].isEmpty()) {
                long suffix = Long.parseLong(parts[1]);
                if (suffix == 0) return new ProbeRange(416, 0, 0);
                start = Math.max(0, size - suffix); end = size - 1;
            } else {
                start = Long.parseLong(parts[0]);
                end = parts[1].isEmpty() ? size - 1 : Math.min(Long.parseLong(parts[1]), size - 1);
            }
            if (start >= size || end < start) return new ProbeRange(416, 0, 0);
            return new ProbeRange(206, start, end - start + 1);
        } catch (NumberFormatException error) { return new ProbeRange(416, 0, 0); }
    }
}
