#include "GpuBackend.h"
#include <EGL/egl.h>
#include <EGL/eglext.h>
#include <GLES3/gl3.h>
#include <GLES2/gl2ext.h>
#include <cstring>
#include <limits>

namespace vrwebview {
namespace gles_detail {
bool extension(const char* list, const char* name) {
    if (!list) return false;
    size_t length = strlen(name);
    for (const char* found = strstr(list, name); found; found = strstr(found + length, name))
        if ((found == list || found[-1] == ' ') && (found[length] == 0 || found[length] == ' ')) return true;
    return false;
}
bool glExtension(const char* name) {
    GLint count = 0; glGetIntegerv(GL_NUM_EXTENSIONS, &count);
    for (GLint i = 0; i < count; ++i) if (!strcmp(reinterpret_cast<const char*>(glGetStringi(GL_EXTENSIONS, i)), name)) return true;
    return false;
}
using CopyImage = void(GL_APIENTRYP)(GLuint, GLenum, GLint, GLint, GLint, GLint,
                                   GLuint, GLenum, GLint, GLint, GLint, GLint, GLsizei, GLsizei, GLsizei);
struct GlesFrame final : GpuFrame {
    EGLImageKHR image = EGL_NO_IMAGE_KHR;
    EGLSyncKHR fence = EGL_NO_SYNC_KHR;
    GLuint texture = 0;
    bool finished = false;
};
}
class GlesBackend final : public GpuBackend {
    EGLDisplay display = EGL_NO_DISPLAY;
    EGLContext context = EGL_NO_CONTEXT;
    bool initialized = false, stopped = false;
    PFNEGLGETNATIVECLIENTBUFFERANDROIDPROC nativeBuffer = nullptr;
    PFNEGLCREATEIMAGEKHRPROC createImage = nullptr;
    PFNEGLDESTROYIMAGEKHRPROC destroyImage = nullptr;
    PFNEGLCREATESYNCKHRPROC createSync = nullptr;
    PFNEGLDESTROYSYNCKHRPROC destroySync = nullptr;
    PFNEGLCLIENTWAITSYNCKHRPROC clientWait = nullptr;
    PFNGLEGLIMAGETARGETTEXTURE2DOESPROC imageTarget = nullptr;
    gles_detail::CopyImage copyImage = nullptr;
    bool Initialize(std::string& error) {
        display = eglGetCurrentDisplay(); context = eglGetCurrentContext();
        if (display == EGL_NO_DISPLAY || context == EGL_NO_CONTEXT) { error = "Unity EGL context is not current on render callback"; return false; }
        const char* extensions = eglQueryString(display, EGL_EXTENSIONS);
        for (auto required : {"EGL_ANDROID_get_native_client_buffer", "EGL_ANDROID_image_native_buffer", "EGL_KHR_image_base", "EGL_KHR_fence_sync"})
            if (!gles_detail::extension(extensions, required)) { error = std::string("Missing ") + required; return false; }
        GLint major = 0, minor = 0; glGetIntegerv(GL_MAJOR_VERSION, &major); glGetIntegerv(GL_MINOR_VERSION, &minor);
        if (major < 3 || !gles_detail::glExtension("GL_OES_EGL_image")) { error = "OpenGL ES 3 / GL_OES_EGL_image required"; return false; }
        const char* copyName = nullptr;
        if (major > 3 || (major == 3 && minor >= 2)) copyName = "glCopyImageSubData";
        else if (gles_detail::glExtension("GL_EXT_copy_image")) copyName = "glCopyImageSubDataEXT";
        else if (gles_detail::glExtension("GL_OES_copy_image")) copyName = "glCopyImageSubDataOES";
        if (!copyName) { error = "GLES 3.2 or GL_EXT_copy_image / GL_OES_copy_image required (raw RGBA/sRGB copy)"; return false; }
#define LOAD(field, type, name) field = reinterpret_cast<type>(eglGetProcAddress(name)); if (!field) { error = "Missing " name; return false; }
        LOAD(nativeBuffer, PFNEGLGETNATIVECLIENTBUFFERANDROIDPROC, "eglGetNativeClientBufferANDROID")
        LOAD(createImage, PFNEGLCREATEIMAGEKHRPROC, "eglCreateImageKHR")
        LOAD(destroyImage, PFNEGLDESTROYIMAGEKHRPROC, "eglDestroyImageKHR")
        LOAD(createSync, PFNEGLCREATESYNCKHRPROC, "eglCreateSyncKHR")
        LOAD(destroySync, PFNEGLDESTROYSYNCKHRPROC, "eglDestroySyncKHR")
        LOAD(clientWait, PFNEGLCLIENTWAITSYNCKHRPROC, "eglClientWaitSyncKHR")
        LOAD(imageTarget, PFNGLEGLIMAGETARGETTEXTURE2DOESPROC, "glEGLImageTargetTexture2DOES")
#undef LOAD
        copyImage = reinterpret_cast<gles_detail::CopyImage>(eglGetProcAddress(copyName));
        if (!copyImage) { error = "CopyImageSubData entry point unavailable"; return false; }
        initialized = true; return true;
    }
public:
    const char* Name() const override { return "OpenGLES3"; }
    std::shared_ptr<GpuBackend> ForSession() override { return std::make_shared<GlesBackend>(); }
    bool BeginFrame(FrameContext&, std::string& error) override {
        if (stopped) { error = "GLES device stopped; recreate the probe"; return false; }
        if (!initialized && !Initialize(error)) return false;
        if (eglGetCurrentDisplay() != display || eglGetCurrentContext() != context) {
            error = "Unity EGL context changed; recreate the probe"; return false;
        }
        return true;
    }
    std::unique_ptr<GpuFrame> Submit(AHardwareBuffer* buffer, void* target, int width, int height,
            const FrameContext&, std::string& error) override {
        using namespace gles_detail;
        auto id = reinterpret_cast<uintptr_t>(target);
        if (id == 0 || id > std::numeric_limits<GLuint>::max() || !glIsTexture(static_cast<GLuint>(id))) {
            error = "Unity GLES target is not a valid GL texture name"; return nullptr;
        }
        // Do not silently consume an existing Unity/other-plugin GL error.
        if (glGetError() != GL_NO_ERROR) { error = "Pre-existing GL error before GPU import"; return nullptr; }
        auto owned = std::make_unique<GlesFrame>(); auto& frame = *owned;
        auto client = nativeBuffer(buffer);
        if (!client) { error = "AHardwareBuffer has no EGL client buffer"; return nullptr; }
        const EGLint attrs[] = {EGL_IMAGE_PRESERVED_KHR, EGL_TRUE, EGL_NONE};
        frame.image = createImage(display, EGL_NO_CONTEXT, EGL_NATIVE_BUFFER_ANDROID, client, attrs);
        if (frame.image == EGL_NO_IMAGE_KHR) { error = "EGLImage AHB import failed"; return nullptr; }
        GLint binding = 0; glGetIntegerv(GL_TEXTURE_BINDING_2D, &binding);
        glGenTextures(1, &frame.texture); glBindTexture(GL_TEXTURE_2D, frame.texture);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_BASE_LEVEL, 0);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAX_LEVEL, 0);
        imageTarget(GL_TEXTURE_2D, static_cast<GLeglImageOES>(frame.image));
        auto importError = glGetError();
        glBindTexture(GL_TEXTURE_2D, static_cast<GLuint>(binding)); // Unity state restored on every path
        if (importError != GL_NO_ERROR || !frame.texture) { error = "EGLImage GL_TEXTURE_2D import failed"; Release(frame); return nullptr; }
        copyImage(frame.texture, GL_TEXTURE_2D, 0, 0, 0, 0, static_cast<GLuint>(id), GL_TEXTURE_2D, 0, 0, 0, 0, width, height, 1);
        auto copyError = glGetError();
        frame.fence = createSync(display, EGL_SYNC_FENCE_KHR, nullptr);
        if (frame.fence == EGL_NO_SYNC_KHR) {
            // Exceptional path: a copy may already be queued. Finish before releasing its AHB.
            glFinish(); frame.finished = copyError != GL_CONTEXT_LOST_KHR && glGetError() == GL_NO_ERROR;
            error = "EGL completion fence creation failed";
        } else glFlush(); // fence remains usable for client polling even without this context current
        if (copyError != GL_NO_ERROR) error = "GPU CopyImageSubData failed " + std::to_string(copyError);
        return owned; // even errors retain AHB until completion can be established
    }
    bool Complete(GpuFrame& value, const FrameContext&, std::string& error) override {
        auto& frame = static_cast<gles_detail::GlesFrame&>(value);
        if (frame.finished) return true;
        if (frame.fence == EGL_NO_SYNC_KHR) return false;
        // timeout=0, flags=0: never wait or flush an unrelated Unity context.
        auto result = clientWait(display, frame.fence, 0, 0);
        if (result != EGL_CONDITION_SATISFIED_KHR && result != EGL_TIMEOUT_EXPIRED_KHR)
            error = "EGL completion fence wait failed; images retained";
        return result == EGL_CONDITION_SATISFIED_KHR;
    }
    void Release(GpuFrame& value) override {
        auto& frame = static_cast<gles_detail::GlesFrame&>(value);
        if (frame.fence != EGL_NO_SYNC_KHR) destroySync(display, frame.fence);
        // GL names belong to the context that created them; never delete a reused name in a new context.
        if (frame.texture && eglGetCurrentDisplay() == display && eglGetCurrentContext() == context) glDeleteTextures(1, &frame.texture);
        if (frame.image != EGL_NO_IMAGE_KHR) destroyImage(display, frame.image);
        frame.fence = EGL_NO_SYNC_KHR; frame.image = EGL_NO_IMAGE_KHR; frame.texture = 0;
    }
    void Shutdown() override { stopped = true; } // already-flushed fences remain the retirement authority
};
std::shared_ptr<GpuBackend> CreateGlesBackend() { return std::make_shared<GlesBackend>(); }
}
