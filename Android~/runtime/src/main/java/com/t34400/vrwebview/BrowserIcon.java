package com.t34400.vrwebview;

import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.RectF;
import android.util.Base64;
import java.io.ByteArrayOutputStream;

/** Encode only icons already received by WebView, with bounded Unity-side decoding. */
final class BrowserIcon {
    static final int SIZE = 64, MAX_BYTES = 32768;
    private BrowserIcon() { }
    static String encode(Bitmap source) {
        if (source == null || source.isRecycled() || source.getWidth() < 1 || source.getHeight() < 1) return null;
        Bitmap output = null;
        try {
            output = Bitmap.createBitmap(SIZE, SIZE, Bitmap.Config.ARGB_8888);
            float scale = Math.min((float) SIZE / source.getWidth(), (float) SIZE / source.getHeight());
            float w = source.getWidth() * scale, h = source.getHeight() * scale;
            new Canvas(output).drawBitmap(source, null, new RectF((SIZE-w)/2, (SIZE-h)/2, (SIZE+w)/2, (SIZE+h)/2), new Paint(Paint.FILTER_BITMAP_FLAG));
            ByteArrayOutputStream bytes = new ByteArrayOutputStream();
            if (!output.compress(Bitmap.CompressFormat.PNG, 100, bytes) || bytes.size() > MAX_BYTES) return null;
            return Base64.encodeToString(bytes.toByteArray(), Base64.NO_WRAP);
        } catch (IllegalArgumentException error) { return null; }
        finally { if (output != null) output.recycle(); }
    }
}
