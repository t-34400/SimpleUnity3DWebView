// Shared Android producer, image ownership and JNI bridge. No graphics API types.
#include "GpuBackend.h"
#include <android/native_window_jni.h>
#include <media/NdkImageReader.h>
#include <jni.h>
#include <poll.h>
#include <unistd.h>
#include <algorithm>
#include <cerrno>
#include <cstring>
#include <mutex>
#include <sstream>
#include <unordered_map>
#include <vector>

namespace {
using namespace vrwebview;
IUnityInterfaces* interfaces = nullptr;
IUnityGraphics* graphics = nullptr;
std::shared_ptr<GpuBackend> activeBackend;
bool deviceActive = false;
uint64_t deviceEpoch = 0;
int eventId = 0;
std::mutex mutex;
struct Frame { AImage* image = nullptr; std::unique_ptr<GpuFrame> gpu; };
struct Session {
    uint64_t id, epoch;
    int width, height;
    void* target;
    std::shared_ptr<GpuBackend> backend;
    AImageReader* reader = nullptr;
    AImage* pending = nullptr;
    int fence = -1, outstanding = 0;
    bool stopping = false;
    uint64_t copies = 0, acquired = 0, fenceWaits = 0, skipped = 0;
    int64_t timestamp = 0;
    std::string error;
    std::vector<Frame> frames;
};
std::unordered_map<uint64_t, std::unique_ptr<Session>> sessions;
uint64_t nextId = 1;
std::string deviceError = "Unity graphics device not initialized", creationError;
Session* find(uint64_t id) { auto it = sessions.find(id); return it == sessions.end() ? nullptr : it->second.get(); }
void destroyFrame(Session& s, Frame& frame) {
    if (frame.gpu) s.backend->Release(*frame.gpu);
    if (frame.image) AImage_delete(frame.image);
    --s.outstanding;
}
void destroyPending(Session& s) {
    if (s.pending) {
        AImage_deleteAsync(s.pending, s.fence); // unread producer fence goes back to Android
        s.pending = nullptr; s.fence = -1; --s.outstanding;
    }
}
void collect(Session& s, const FrameContext& context) {
    for (auto it = s.frames.begin(); it != s.frames.end();) {
        if (s.backend->Complete(*it->gpu, context, s.error)) { destroyFrame(s, *it); it = s.frames.erase(it); }
        else ++it;
    }
}
void pump(Session& s, const FrameContext& context) {
    if (!s.pending) {
        int fd = -1; AImage* image = nullptr;
        auto result = AImageReader_acquireNextImageAsync(s.reader, &image, &fd);
        if (result == AMEDIA_IMGREADER_NO_BUFFER_AVAILABLE) return;
        if (result != AMEDIA_OK) { s.error = "AImageReader acquisition failed " + std::to_string(result); return; }
        s.pending = image; s.fence = fd; ++s.acquired;
    }
    if (s.fence >= 0) {
        pollfd fd{s.fence, POLLIN, 0}; int ready = poll(&fd, 1, 0);
        if (ready == 0 || (ready < 0 && errno == EINTR)) { ++s.fenceWaits; return; }
        if (ready < 0 || (fd.revents & (POLLERR | POLLNVAL)) || !(fd.revents & POLLIN)) { s.error = "AHB acquire fence failed"; return; }
        close(s.fence); s.fence = -1;
    }
    Frame frame; frame.image = s.pending; s.pending = nullptr;
    AHardwareBuffer* buffer = nullptr;
    if (AImage_getHardwareBuffer(frame.image, &buffer) != AMEDIA_OK || !buffer) { s.error = "AImage has no hardware buffer"; destroyFrame(s, frame); return; }
    AHardwareBuffer_Desc description{}; AHardwareBuffer_describe(buffer, &description);
    if (description.width != static_cast<uint32_t>(s.width) || description.height != static_cast<uint32_t>(s.height)
            || description.format != AHARDWAREBUFFER_FORMAT_R8G8B8A8_UNORM || description.layers != 1) {
        s.error = "Unsupported AHardwareBuffer extent/format/layers"; destroyFrame(s, frame); return;
    }
    frame.gpu = s.backend->Submit(buffer, s.target, s.width, s.height, context, s.error);
    if (!frame.gpu) { destroyFrame(s, frame); return; }
    AImage_getTimestamp(frame.image, &s.timestamp);
    s.frames.push_back(std::move(frame)); ++s.copies;
}
void UNITY_INTERFACE_API render(int, void*) {
    std::lock_guard<std::mutex> guard(mutex);
    for (auto it = sessions.begin(); it != sessions.end();) {
        auto& s = *it->second; FrameContext context;
        std::string beginError;
        bool canSubmit = deviceActive && s.epoch == deviceEpoch && s.backend->BeginFrame(context, beginError);
        if (!beginError.empty()) s.error = beginError;
        collect(s, context); // retirement still runs after errors, pause or device shutdown
        if (s.stopping) destroyPending(s);
        else if (canSubmit && s.error.empty()) pump(s, context);
        if (s.stopping && s.frames.empty()) {
            if (s.reader) AImageReader_delete(s.reader);
            it = sessions.erase(it);
        } else ++it;
    }
}
void UNITY_INTERFACE_API deviceEvent(UnityGfxDeviceEventType type) {
    std::lock_guard<std::mutex> guard(mutex);
    if (type == kUnityGfxDeviceEventInitialize) {
        if (deviceActive) return;
        deviceError.clear();
        if (!graphics) deviceError = "Unity graphics interface unavailable";
        else if (graphics->GetRenderer() == kUnityGfxRendererVulkan) activeBackend = CreateVulkanBackend(interfaces, eventId, deviceError);
        else if (graphics->GetRenderer() == kUnityGfxRendererOpenGLES30) activeBackend = CreateGlesBackend();
        else deviceError = "Only Android Vulkan and OpenGL ES 3 are supported";
        deviceActive = activeBackend != nullptr;
        if (deviceActive) ++deviceEpoch;
    } else if (type == kUnityGfxDeviceEventShutdown) {
        if (activeBackend) activeBackend->Shutdown();
        for (auto it = sessions.begin(); it != sessions.end();) {
            auto& s = *it->second;
            s.backend->Shutdown();
            collect(s, FrameContext{}); destroyPending(s);
            s.error = "Graphics device stopped; recreate the probe";
            if (s.stopping && s.frames.empty()) { if (s.reader) AImageReader_delete(s.reader); it = sessions.erase(it); }
            else ++it;
        }
        activeBackend.reset(); deviceActive = false; deviceError = "Unity graphics device stopped";
    }
}
}
extern "C" {
UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* registry) {
    interfaces = registry; graphics = interfaces->Get<IUnityGraphics>();
    if (!graphics) return;
    eventId = graphics->ReserveEventIDRange(1);
    InstallVulkanIntercept(interfaces);
    graphics->RegisterDeviceEventCallback(deviceEvent);
    deviceEvent(kUnityGfxDeviceEventInitialize);
}
UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API UnityPluginUnload() {
    if (graphics) graphics->UnregisterDeviceEventCallback(deviceEvent);
}
UNITY_INTERFACE_EXPORT UnityRenderingEventAndData VrGetRenderEvent() { return render; }
UNITY_INTERFACE_EXPORT int VrGetEventId() { return eventId; }
UNITY_INTERFACE_EXPORT uint64_t VrCreate(int width, int height, void* target) {
    std::lock_guard<std::mutex> guard(mutex); creationError.clear();
    if (!deviceActive || !activeBackend || !deviceError.empty()) return 0;
    if (width < 128 || width > 2048 || height < 128 || height > 2048 || !target) { creationError = "Invalid probe extent or target"; return 0; }
    auto s = std::make_unique<Session>(); s->id = nextId++; s->width = width; s->height = height; s->target = target; s->backend = activeBackend->ForSession(); s->epoch = deviceEpoch;
    auto result = AImageReader_newWithUsage(width, height, AIMAGE_FORMAT_RGBA_8888,
            AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE | AHARDWAREBUFFER_USAGE_GPU_COLOR_OUTPUT, 8, &s->reader);
    if (result != AMEDIA_OK) { creationError = "AImageReader creation failed " + std::to_string(result); return 0; }
    auto id = s->id; sessions.emplace(id, std::move(s)); return id;
}
UNITY_INTERFACE_EXPORT int VrAlive(uint64_t id) { std::lock_guard<std::mutex> guard(mutex); return find(id) != nullptr; }
UNITY_INTERFACE_EXPORT int VrFailed(uint64_t id) {
    std::lock_guard<std::mutex> guard(mutex); auto s = find(id);
    return !s || !s->error.empty() || !deviceError.empty();
}
UNITY_INTERFACE_EXPORT void VrAbandon(uint64_t id) {
    std::lock_guard<std::mutex> guard(mutex); if (auto s = find(id)) s->stopping = true;
}
UNITY_INTERFACE_EXPORT int VrStatus(uint64_t id, char* output, int capacity) {
    if (!output || capacity < 1) return 0;
    std::lock_guard<std::mutex> guard(mutex); std::ostringstream text; auto s = find(id);
    auto backend = s ? s->backend : activeBackend;
    text << "GPU AHB probe GX2 backend=" << (backend ? backend->Name() : "none") << "\n"
         << (deviceError.empty() ? "device selected; capabilities checked on render thread" : deviceError);
    if (graphics && graphics->GetRenderer() == kUnityGfxRendererVulkan) text << "\n" << VulkanBootstrapStatus();
    if (!s && !creationError.empty()) text << "\n" << creationError;
    if (s) text << "\nGPU copies=" << s->copies << " acquired=" << s->acquired << " inFlight=" << s->frames.size()
                << " slots=" << s->outstanding << " fenceWait=" << s->fenceWaits << " producerSkipped=" << s->skipped
                << "\ntimestamp=" << s->timestamp << " size=" << s->width << "x" << s->height
                << "\n" << (s->error.empty() ? (s->stopping ? "stopping" : "running") : s->error);
    auto result = text.str(); int length = std::min(capacity - 1, static_cast<int>(result.size()));
    memcpy(output, result.data(), length); output[length] = 0; return length;
}
JNIEXPORT jobject JNICALL Java_com_t34400_vrwebview_GpuBridge_nativeSurface(JNIEnv* env, jclass, jlong id) {
    std::lock_guard<std::mutex> guard(mutex); auto s = find(id); if (!s || s->stopping) return nullptr;
    ANativeWindow* window = nullptr;
    if (AImageReader_getWindow(s->reader, &window) != AMEDIA_OK || !window) return nullptr;
    return ANativeWindow_toSurface(env, window);
}
JNIEXPORT jboolean JNICALL Java_com_t34400_vrwebview_GpuBridge_nativeReserve(JNIEnv*, jclass, jlong id) {
    std::lock_guard<std::mutex> guard(mutex); auto s = find(id);
    if (!s || s->stopping || !s->error.empty() || !deviceError.empty()) return false;
    // Bound ALL outstanding buffers. This keeps lockHardwareCanvas away from a full queue,
    // including when Unity pauses. No ImageReader callback ever waits on the UI thread.
    if (s->outstanding >= 6) { ++s->skipped; return false; }
    ++s->outstanding; return true;
}
JNIEXPORT void JNICALL Java_com_t34400_vrwebview_GpuBridge_nativeCancelReserve(JNIEnv*, jclass, jlong id) {
    std::lock_guard<std::mutex> guard(mutex); if (auto s = find(id)) --s->outstanding;
}
JNIEXPORT void JNICALL Java_com_t34400_vrwebview_GpuBridge_nativeStop(JNIEnv*, jclass, jlong id) {
    std::lock_guard<std::mutex> guard(mutex);
    if (auto s = find(id)) {
        s->stopping = true;
        if (!deviceActive && s->frames.empty()) {
            destroyPending(*s); if (s->reader) AImageReader_delete(s->reader); sessions.erase(id);
        }
    }
}
}
