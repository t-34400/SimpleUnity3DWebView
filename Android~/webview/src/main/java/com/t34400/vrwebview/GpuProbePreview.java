package com.t34400.vrwebview;

import android.content.Context;
import android.graphics.SurfaceTexture;
import android.opengl.GLES11Ext;
import android.opengl.GLES20;
import android.opengl.GLSurfaceView;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.Looper;
import android.view.Surface;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.FloatBuffer;
import java.util.concurrent.atomic.AtomicBoolean;
import javax.microedition.khronos.egl.EGLConfig;
import javax.microedition.khronos.opengles.GL10;

/** Android-side GPU consumer, deliberately independent of Unity's graphics backend. */
final class GpuProbePreview extends GLSurfaceView implements GLSurfaceView.Renderer {
    interface Listener {
        void onOutput(Output output);
        void onFailure(String stage, String kind);
    }

    static final class Output {
        final SurfaceTexture texture;
        final Surface surface;
        private final AtomicBoolean released = new AtomicBoolean();
        Output(SurfaceTexture texture) { this.texture = texture; surface = new Surface(texture); }
        boolean valid() { return !released.get() && surface.isValid(); }
        void resize(int width, int height) { texture.setDefaultBufferSize(width, height); }
        void release() {
            if (released.compareAndSet(false, true)) {
                texture.setOnFrameAvailableListener(null);
                surface.release(); texture.release();
            }
        }
    }

    private final Listener listener;
    private final Handler ui = new Handler(Looper.getMainLooper());
    private final HandlerThread notifications = new HandlerThread("VRWebViewFrames");
    private final Handler frameHandler;
    private final AtomicBoolean pending = new AtomicBoolean();
    private final FloatBuffer vertices = buffer(-1, -1, 1, -1, -1, 1, 1, 1);
    private final FloatBuffer coordinates = buffer(0, 0, 1, 0, 0, 1, 1, 1);
    private final float[] matrix = new float[16];
    private volatile Output output;
    private volatile boolean closed;
    private int program, textureName, positionAttribute, uvAttribute, matrixUniform, samplerUniform;
    private int viewWidth, viewHeight;
    private boolean hasImage;
    volatile int sourceWidth = 1024, sourceHeight = 768;
    volatile long acquiredFrames, textureTimestamp;
    volatile String graphics = "waiting for GLES context";

    GpuProbePreview(Context context, Listener listener) {
        super(context); this.listener = listener;
        notifications.start(); frameHandler = new Handler(notifications.getLooper());
        setEGLContextClientVersion(3);
        setEGLConfigChooser(8, 8, 8, 8, 0, 0);
        setPreserveEGLContextOnPause(true);
        setRenderer(this); setRenderMode(RENDERMODE_WHEN_DIRTY);
    }

    private static FloatBuffer buffer(float... data) {
        FloatBuffer result = ByteBuffer.allocateDirect(data.length * 4).order(ByteOrder.nativeOrder()).asFloatBuffer();
        result.put(data).position(0); return result;
    }

    @Override public void onSurfaceCreated(GL10 unused, EGLConfig config) {
        if (closed) return;
        try {
            Output previous = output; if (previous != null) previous.release();
            pending.set(false); hasImage = false;
            graphics = GLES20.glGetString(GLES20.GL_VERSION) + " / " + GLES20.glGetString(GLES20.GL_RENDERER);
            program = createProgram();
            positionAttribute = GLES20.glGetAttribLocation(program, "aPosition");
            uvAttribute = GLES20.glGetAttribLocation(program, "aUv");
            matrixUniform = GLES20.glGetUniformLocation(program, "uTransform");
            samplerUniform = GLES20.glGetUniformLocation(program, "uImage");
            int[] names = new int[1]; GLES20.glGenTextures(1, names, 0); textureName = names[0];
            GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, textureName);
            GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_MIN_FILTER, GLES20.GL_LINEAR);
            GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_MAG_FILTER, GLES20.GL_LINEAR);
            GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_WRAP_S, GLES20.GL_CLAMP_TO_EDGE);
            GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_WRAP_T, GLES20.GL_CLAMP_TO_EDGE);
            SurfaceTexture texture = new SurfaceTexture(textureName);
            texture.setDefaultBufferSize(sourceWidth, sourceHeight);
            Output created = new Output(texture); output = created;
            // Never depend on the UI thread to drain the producer's buffer queue.
            texture.setOnFrameAvailableListener(ignored -> {
                if (!closed && output == created) { pending.set(true); requestRender(); }
            }, frameHandler);
            ui.post(() -> { if (!closed && output == created) listener.onOutput(created); else created.release(); });
        } catch (RuntimeException error) { fail("GPU create", error); }
    }

    @Override public void onSurfaceChanged(GL10 unused, int width, int height) {
        viewWidth = width; viewHeight = height;
    }

    @Override public void onDrawFrame(GL10 unused) {
        GLES20.glViewport(0, 0, viewWidth, viewHeight);
        GLES20.glClearColor(0.04f, 0.04f, 0.04f, 1); GLES20.glClear(GLES20.GL_COLOR_BUFFER_BIT);
        Output current = output;
        if (closed || current == null || !current.valid()) return;
        try {
            if (pending.getAndSet(false)) {
                current.texture.updateTexImage(); current.texture.getTransformMatrix(matrix);
                textureTimestamp = current.texture.getTimestamp(); acquiredFrames++; hasImage = true;
            }
            if (!hasImage || viewWidth <= 0 || viewHeight <= 0) return;
            ProbeGeometry fit = new ProbeGeometry(viewWidth, viewHeight, sourceWidth, sourceHeight);
            GLES20.glViewport(Math.round(fit.left), Math.round(fit.top), Math.round(fit.width), Math.round(fit.height));
            GLES20.glUseProgram(program);
            GLES20.glActiveTexture(GLES20.GL_TEXTURE0);
            GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, textureName);
            GLES20.glUniform1i(samplerUniform, 0);
            GLES20.glUniformMatrix4fv(matrixUniform, 1, false, matrix, 0);
            GLES20.glEnableVertexAttribArray(positionAttribute); GLES20.glEnableVertexAttribArray(uvAttribute);
            GLES20.glVertexAttribPointer(positionAttribute, 2, GLES20.GL_FLOAT, false, 0, vertices);
            GLES20.glVertexAttribPointer(uvAttribute, 2, GLES20.GL_FLOAT, false, 0, coordinates);
            GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP, 0, 4);
            GLES20.glDisableVertexAttribArray(positionAttribute); GLES20.glDisableVertexAttribArray(uvAttribute);
            int error = GLES20.glGetError();
            if (error != GLES20.GL_NO_ERROR) throw new IllegalStateException("GLES error " + error);
        } catch (RuntimeException error) { fail("GPU consume", error); }
    }

    void resizeSource(int width, int height) {
        sourceWidth = width; sourceHeight = height;
        Output current = output; if (current != null && current.valid()) current.resize(width, height);
        requestRender();
    }

    void dispose() {
        if (closed) return;
        closed = true;
        Output current = output; if (current != null) current.release();
        notifications.quitSafely();
        queueEvent(() -> {
            if (program != 0) GLES20.glDeleteProgram(program);
            if (textureName != 0) GLES20.glDeleteTextures(1, new int[] {textureName}, 0);
            program = 0; textureName = 0;
        });
    }

    private void fail(String stage, RuntimeException error) {
        ui.post(() -> { if (!closed) listener.onFailure(stage, error.getClass().getSimpleName()); });
    }

    private static int shader(int type, String source) {
        int shader = GLES20.glCreateShader(type); GLES20.glShaderSource(shader, source); GLES20.glCompileShader(shader);
        int[] status = new int[1]; GLES20.glGetShaderiv(shader, GLES20.GL_COMPILE_STATUS, status, 0);
        if (status[0] == 0) { GLES20.glDeleteShader(shader); throw new IllegalStateException("Shader compilation failed"); }
        return shader;
    }

    private static int createProgram() {
        int vertex = shader(GLES20.GL_VERTEX_SHADER, "attribute vec2 aPosition; attribute vec2 aUv; uniform mat4 uTransform;"
                + "varying vec2 vUv; void main(){gl_Position=vec4(aPosition,0.,1.);vUv=(uTransform*vec4(aUv,0.,1.)).xy;}");
        int fragment = shader(GLES20.GL_FRAGMENT_SHADER, "#extension GL_OES_EGL_image_external : require\n"
                + "precision mediump float; uniform samplerExternalOES uImage; varying vec2 vUv;"
                + "void main(){gl_FragColor=texture2D(uImage,vUv);}");
        int result = GLES20.glCreateProgram();
        GLES20.glAttachShader(result, vertex); GLES20.glAttachShader(result, fragment); GLES20.glLinkProgram(result);
        GLES20.glDeleteShader(vertex); GLES20.glDeleteShader(fragment);
        int[] status = new int[1]; GLES20.glGetProgramiv(result, GLES20.GL_LINK_STATUS, status, 0);
        if (status[0] == 0) { GLES20.glDeleteProgram(result); throw new IllegalStateException("Shader linking failed"); }
        return result;
    }
}
