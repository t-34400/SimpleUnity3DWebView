package com.t34400.vrwebview;

/** The same FIT_CENTER geometry is used by the GLES viewport and touch routing. */
final class ProbeGeometry {
    final float left, top, width, height;
    private final int sourceWidth, sourceHeight;

    ProbeGeometry(int viewWidth, int viewHeight, int sourceWidth, int sourceHeight) {
        if (viewWidth <= 0 || viewHeight <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
            throw new IllegalArgumentException("Invalid viewport dimensions");
        this.sourceWidth = sourceWidth; this.sourceHeight = sourceHeight;
        float scale = Math.min(viewWidth / (float) sourceWidth, viewHeight / (float) sourceHeight);
        width = sourceWidth * scale; height = sourceHeight * scale;
        left = (viewWidth - width) / 2; top = (viewHeight - height) / 2;
    }

    boolean contains(float x, float y) {
        return x >= left && y >= top && x < left + width && y < top + height;
    }
    float sourceX(float x) { return (x - left) * sourceWidth / width; }
    float sourceY(float y) { return (y - top) * sourceHeight / height; }
}
