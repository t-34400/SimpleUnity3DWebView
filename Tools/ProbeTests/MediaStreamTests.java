package com.t34400.vrwebview;

import java.io.ByteArrayOutputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

/** Exercise the actual bundled MP4 across a 1 MiB boundary without Android mocks. */
public final class MediaStreamTests {
    private static int checks;
    private static void check(boolean result) {
        if (!result) throw new AssertionError("Media stream check " + (checks + 1));
        checks++;
    }
    public static void main(String[] args) throws Exception {
        byte[] video = Files.readAllBytes(Path.of(args[0]));
        check(video.length > 1048576);
        for (String header : new String[]{null, "bytes=0-", "bytes=1024000-", "bytes=1048576-", "bytes=-65536"}) {
            ProbeRange range = ProbeRange.parse(header, video.length);
            List<String> events = new ArrayList<>();
            ProbeMediaStream stream = new ProbeMediaStream(video, (int)range.start, (int)range.length, events::add);
            ByteArrayOutputStream output = new ByteArrayOutputStream();
            byte[] buffer = new byte[16384]; int count;
            while ((count = stream.read(buffer)) != -1) output.write(buffer, 0, count);
            check(Arrays.equals(Arrays.copyOfRange(video, (int)range.start, (int)(range.start + range.length)), output.toByteArray()));
            check(events.stream().anyMatch(e -> e.contains("event=data_exhausted") && e.contains("remaining=0")));
            check(events.stream().anyMatch(e -> e.contains("event=eof readBytes=" + range.length + " skippedBytes=0")));
            int eofCount = events.size(); check(stream.read() == -1 && events.size() == eofCount);
            stream.close(); stream.close();
            check(events.stream().filter(e -> e.startsWith("event=close ")).count() == 1);
        }
        List<String> events = new ArrayList<>();
        ProbeMediaStream partial = new ProbeMediaStream(video, 0, video.length, events::add);
        check(partial.read() == (video[0] & 255));
        partial.close();
        check(events.get(events.size()-1).contains("remaining=" + (video.length-1)));
        check(events.stream().noneMatch(e -> e.startsWith("event=eof ")));
        // Preserve ByteArrayInputStream mark/reset/skip and close semantics.
        check(partial.markSupported()); partial.mark(10);
        check(partial.skip(10) == 10 && partial.available() == video.length - 11);
        partial.reset(); check(partial.read() == (video[1] & 255));
        ProbeMediaStream brokenLogger = new ProbeMediaStream(video, 0, video.length, e -> { throw new IllegalStateException(); });
        check(Arrays.equals(video, brokenLogger.readAllBytes())); brokenLogger.close();
        // Model the documented WebView consumer contract: it verifies available(),
        // computes the request range on that zero-based stream, then skips before reading.
        // This is a stream-contract regression, not an Android/WebView emulator.
        for (String header : new String[]{null, "bytes=0-", "bytes=1048576-", "bytes=1024000-", "bytes=-65536", "bytes=40-49", "bytes=0-0", "bytes=" + (video.length - 1) + "-"}) {
            ProbeRange declared = ProbeRange.parse(header, video.length);
            ProbeMediaStream resource = ProbeMediaStream.intercepted(video, declared, ignored -> {});
            ProbeRange consumed = ProbeRange.parse(header, resource.available());
            check(consumed.status == declared.status && consumed.start == declared.start && consumed.length == declared.length);
            check(resource.skip(consumed.start) == consumed.start);
            check(Arrays.equals(Arrays.copyOfRange(video, (int)declared.start, (int)(declared.start + declared.length)), resource.readAllBytes()));
            resource.close();
        }
        // The old pre-sliced stream fails verification at the exact continuation offset in IP2.
        ProbeRange continuation = ProbeRange.parse("bytes=1048576-", video.length);
        ProbeMediaStream oldBody = new ProbeMediaStream(video, (int)continuation.start, (int)continuation.length, ignored -> {});
        check(ProbeRange.parse("bytes=1048576-", oldBody.available()).status == 416);
        oldBody.close();
        System.out.println("PASS " + checks + " media stream checks (real MP4 byte equality, ranges, EOF/cancellation and diagnostic isolation)");
    }
}
