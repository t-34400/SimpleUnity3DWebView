// Experimental Android -> Unity Vulkan transfer. No CPU pixel access.
#define VK_USE_PLATFORM_ANDROID_KHR
#define VK_NO_PROTOTYPES
#include "Unity/IUnityGraphics.h"
#include "Unity/IUnityGraphicsVulkan.h"
#include <android/hardware_buffer.h>
#include <android/native_window_jni.h>
#include <media/NdkImageReader.h>
#include <jni.h>
#include <poll.h>
#include <unistd.h>
#include <algorithm>
#include <atomic>
#include <cstring>
#include <cerrno>
#include <memory>
#include <mutex>
#include <sstream>
#include <unordered_map>
#include <vector>

namespace {
IUnityGraphics* graphics = nullptr;
IUnityGraphicsVulkan* vulkan = nullptr;
UnityVulkanInstance unity{};
PFN_vkGetInstanceProcAddr loader = nullptr;
PFN_vkCreateDevice createDevice = nullptr;
VkInstance loaderInstance = VK_NULL_HANDLE;
VkDevice configuredDevice = VK_NULL_HANDLE;
int eventId = 0;
std::mutex mutex;

#define DEVICE_FUNCTIONS(X) \
 X(vkGetAndroidHardwareBufferPropertiesANDROID) X(vkCreateImage) X(vkDestroyImage) \
 X(vkAllocateMemory) X(vkFreeMemory) X(vkBindImageMemory) X(vkCmdPipelineBarrier) \
 X(vkCmdCopyImage) X(vkDeviceWaitIdle)
#define DECLARE(name) PFN_##name name = nullptr;
DEVICE_FUNCTIONS(DECLARE)
#undef DECLARE
PFN_vkGetPhysicalDeviceMemoryProperties memoryProperties = nullptr;

struct Frame {
    AImage* image = nullptr;
    VkImage imported = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    uint64_t frame = 0;
};
struct Session {
    uint64_t id;
    int width, height;
    void* target;
    AImageReader* reader = nullptr;
    AImage* pending = nullptr;
    int fence = -1;
    int outstanding = 0; // reserved producer slots, including queued and GPU-owned images
    bool stopping = false;
    uint64_t copies = 0, acquired = 0, fenceWaits = 0, skipped = 0;
    int64_t timestamp = 0;
    std::string error;
    std::vector<Frame> frames;
};
std::unordered_map<uint64_t, std::unique_ptr<Session>> sessions;
uint64_t nextId = 1;
std::string deviceError = "Unity Vulkan device not initialized";

bool hasExtension(const std::vector<VkExtensionProperties>& available, const char* name) {
    return std::any_of(available.begin(), available.end(), [name](const auto& p) { return !strcmp(name, p.extensionName); });
}
VKAPI_ATTR VkResult VKAPI_CALL hookCreateDevice(VkPhysicalDevice physical, const VkDeviceCreateInfo* info,
        const VkAllocationCallbacks* alloc, VkDevice* device) {
    configuredDevice = VK_NULL_HANDLE;
    auto enumerate = reinterpret_cast<PFN_vkEnumerateDeviceExtensionProperties>(loader(loaderInstance, "vkEnumerateDeviceExtensionProperties"));
    auto properties = reinterpret_cast<PFN_vkGetPhysicalDeviceProperties>(loader(loaderInstance, "vkGetPhysicalDeviceProperties"));
    uint32_t count = 0;
    VkPhysicalDeviceProperties p{};
    if (properties) properties(physical, &p);
    std::vector<VkExtensionProperties> available;
    if (enumerate && enumerate(physical, nullptr, &count, nullptr) == VK_SUCCESS) {
        available.resize(count);
        if (enumerate(physical, nullptr, &count, available.data()) != VK_SUCCESS) available.clear();
    }
    const char* required[] = { VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME,
                              VK_EXT_QUEUE_FAMILY_FOREIGN_EXTENSION_NAME };
    bool supported = p.apiVersion >= VK_API_VERSION_1_1;
    for (auto name : required) supported = supported && hasExtension(available, name);
    VkDeviceCreateInfo changed = *info;
    std::vector<const char*> enabled;
    for (uint32_t i = 0; i < info->enabledExtensionCount; ++i) enabled.push_back(info->ppEnabledExtensionNames[i]);
    if (supported) {
        for (auto name : required)
            if (std::none_of(enabled.begin(), enabled.end(), [name](auto e) { return !strcmp(e, name); })) enabled.push_back(name);
        changed.enabledExtensionCount = static_cast<uint32_t>(enabled.size());
        changed.ppEnabledExtensionNames = enabled.data();
    }
    VkResult result = createDevice(physical, &changed, alloc, device);
    // Keep the normal Unity device path usable even when this probe cannot run.
    if (result != VK_SUCCESS && supported) {
        supported = false;
        result = createDevice(physical, info, alloc, device);
    }
    if (result == VK_SUCCESS && supported) configuredDevice = *device;
    return result;
}
VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL hookGetProc(VkInstance instance, const char* name) {
    auto result = loader(instance, name);
    if (!strcmp(name, "vkCreateDevice") && result) {
        loaderInstance = instance;
        createDevice = reinterpret_cast<PFN_vkCreateDevice>(result);
        return reinterpret_cast<PFN_vkVoidFunction>(hookCreateDevice);
    }
    return result;
}
PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API intercept(PFN_vkGetInstanceProcAddr original, void*) {
    loader = original;
    return hookGetProc;
}

void destroyFrame(Session& session, Frame& frame) {
    if (frame.imported && vkDestroyImage) vkDestroyImage(unity.device, frame.imported, nullptr);
    if (frame.memory && vkFreeMemory) vkFreeMemory(unity.device, frame.memory, nullptr);
    if (frame.image) AImage_delete(frame.image); // GPU is finished, so no release fence is necessary.
    --session.outstanding;
}
void destroyPending(Session& s) {
    if (s.pending) {
        // Returning an unread image forwards its producer fence; never close it early.
        AImage_deleteAsync(s.pending, s.fence);
        s.pending = nullptr; s.fence = -1; --s.outstanding;
    }
}
void collect(Session& s, uint64_t safe) {
    for (auto it = s.frames.begin(); it != s.frames.end();) {
        if (it->frame <= safe) { destroyFrame(s, *it); it = s.frames.erase(it); }
        else ++it;
    }
}
bool importImage(Session& s, Frame& frame) {
    AHardwareBuffer* buffer = nullptr;
    if (AImage_getHardwareBuffer(frame.image, &buffer) != AMEDIA_OK || !buffer) { s.error = "AImage has no hardware buffer"; return false; }
    AHardwareBuffer_Desc description{}; AHardwareBuffer_describe(buffer, &description);
    if (description.width != static_cast<uint32_t>(s.width) || description.height != static_cast<uint32_t>(s.height)
            || description.format != AHARDWAREBUFFER_FORMAT_R8G8B8A8_UNORM || description.layers != 1) {
        s.error = "Unsupported AHardwareBuffer extent/format/layers"; return false;
    }
    VkAndroidHardwareBufferFormatPropertiesANDROID format{}; format.sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_FORMAT_PROPERTIES_ANDROID;
    VkAndroidHardwareBufferPropertiesANDROID properties{}; properties.sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_PROPERTIES_ANDROID;
    properties.pNext = &format;
    if (vkGetAndroidHardwareBufferPropertiesANDROID(unity.device, buffer, &properties) != VK_SUCCESS
            || format.format != VK_FORMAT_R8G8B8A8_UNORM || !(format.formatFeatures & VK_FORMAT_FEATURE_TRANSFER_SRC_BIT)) {
        s.error = "AHB is not transferable RGBA8 (external-format/YUV path unsupported)"; return false;
    }
    VkExternalMemoryImageCreateInfo external{}; external.sType = VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO;
    external.handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID;
    VkImageCreateInfo image{}; image.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
    image.pNext = &external; image.imageType = VK_IMAGE_TYPE_2D; image.format = format.format;
    image.extent = {static_cast<uint32_t>(s.width), static_cast<uint32_t>(s.height), 1};
    image.mipLevels = 1; image.arrayLayers = 1; image.samples = VK_SAMPLE_COUNT_1_BIT;
    image.tiling = VK_IMAGE_TILING_OPTIMAL; image.usage = VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
    image.sharingMode = VK_SHARING_MODE_EXCLUSIVE; image.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
    if (vkCreateImage(unity.device, &image, nullptr, &frame.imported) != VK_SUCCESS) { s.error = "vkCreateImage AHB failed"; return false; }
    VkPhysicalDeviceMemoryProperties types{}; memoryProperties(unity.physicalDevice, &types);
    uint32_t type = 0;
    for (; type < types.memoryTypeCount; ++type) if (properties.memoryTypeBits & (1u << type)) break;
    if (type == types.memoryTypeCount) { s.error = "AHB memory type unavailable"; return false; }
    VkImportAndroidHardwareBufferInfoANDROID import{}; import.sType = VK_STRUCTURE_TYPE_IMPORT_ANDROID_HARDWARE_BUFFER_INFO_ANDROID; import.buffer = buffer;
    VkMemoryDedicatedAllocateInfo dedicated{}; dedicated.sType = VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO;
    dedicated.pNext = &import; dedicated.image = frame.imported;
    VkMemoryAllocateInfo allocation{}; allocation.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
    allocation.pNext = &dedicated; allocation.allocationSize = properties.allocationSize; allocation.memoryTypeIndex = type;
    if (vkAllocateMemory(unity.device, &allocation, nullptr, &frame.memory) != VK_SUCCESS
            || vkBindImageMemory(unity.device, frame.imported, frame.memory, 0) != VK_SUCCESS) {
        s.error = "AHB import/bind failed"; return false;
    }
    return true;
}
void pump(Session& s, const UnityVulkanRecordingState& before) {
    collect(s, before.safeFrameNumber);
    if (s.stopping) { destroyPending(s); return; }
    if (!s.error.empty()) return;
    if (!s.pending) {
        int fd = -1; AImage* image = nullptr;
        // acquireNext, rather than acquireLatest, preserves exact slot accounting.
        auto result = AImageReader_acquireNextImageAsync(s.reader, &image, &fd);
        if (result == AMEDIA_IMGREADER_NO_BUFFER_AVAILABLE) return;
        if (result != AMEDIA_OK) { s.error = "AImageReader acquisition failed " + std::to_string(result); return; }
        s.pending = image; s.fence = fd; ++s.acquired;
    }
    if (s.fence >= 0) {
        pollfd fd{s.fence, POLLIN, 0};
        int ready = poll(&fd, 1, 0); // never block Unity's render thread on a producer
        if (ready == 0 || (ready < 0 && errno == EINTR)) { ++s.fenceWaits; return; }
        if (ready < 0 || (fd.revents & (POLLERR | POLLNVAL)) || !(fd.revents & POLLIN)) { s.error = "AHB acquire fence failed"; return; }
        close(s.fence); s.fence = -1;
    }
    Frame frame; frame.image = s.pending; s.pending = nullptr;
    if (!importImage(s, frame)) { destroyFrame(s, frame); return; }
    UnityVulkanImage target{};
    if (!vulkan->AccessTexture(s.target, UnityVulkanWholeImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
            VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT, kUnityVulkanResourceAccess_PipelineBarrier, &target)) {
        s.error = "Unity AccessTexture failed"; destroyFrame(s, frame); return;
    }
    if ((target.format != VK_FORMAT_R8G8B8A8_UNORM && target.format != VK_FORMAT_R8G8B8A8_SRGB) || target.extent.width != static_cast<uint32_t>(s.width)
            || target.extent.height != static_cast<uint32_t>(s.height) || target.samples != VK_SAMPLE_COUNT_1_BIT
            || !(target.usage & VK_IMAGE_USAGE_TRANSFER_DST_BIT)) {
        s.error = "Unity target must be matching single-sample RGBA8 with TRANSFER_DST"; destroyFrame(s, frame); return;
    }
    UnityVulkanRecordingState state{};
    if (!vulkan->CommandRecordingState(&state, kUnityVulkanGraphicsQueueAccess_DontCare)) {
        s.error = "Unity command buffer unavailable"; destroyFrame(s, frame); return;
    }
    VkImageMemoryBarrier acquire{}; acquire.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    acquire.dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
    // Android AHB ownership contract: external producer -> foreign queue, GENERAL layout.
    acquire.oldLayout = VK_IMAGE_LAYOUT_GENERAL; acquire.newLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
    acquire.srcQueueFamilyIndex = VK_QUEUE_FAMILY_FOREIGN_EXT; acquire.dstQueueFamilyIndex = unity.queueFamilyIndex;
    acquire.image = frame.imported; acquire.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
    vkCmdPipelineBarrier(state.commandBuffer, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT,
            0, 0, nullptr, 0, nullptr, 1, &acquire);
    VkImageCopy copy{};
    copy.srcSubresource = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1}; copy.dstSubresource = copy.srcSubresource;
    copy.extent = {static_cast<uint32_t>(s.width), static_cast<uint32_t>(s.height), 1};
    vkCmdCopyImage(state.commandBuffer, frame.imported, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
            target.image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &copy);
    auto release = acquire; release.srcAccessMask = VK_ACCESS_TRANSFER_READ_BIT; release.dstAccessMask = 0;
    release.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL; release.newLayout = VK_IMAGE_LAYOUT_GENERAL;
    release.srcQueueFamilyIndex = unity.queueFamilyIndex; release.dstQueueFamilyIndex = VK_QUEUE_FAMILY_FOREIGN_EXT;
    vkCmdPipelineBarrier(state.commandBuffer, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
            0, 0, nullptr, 0, nullptr, 1, &release);
    AImage_getTimestamp(frame.image, &s.timestamp);
    frame.frame = state.currentFrameNumber; s.frames.push_back(frame); ++s.copies;
    // Tell Unity about the subsequent shader read, including the transfer-write dependency.
    if (!vulkan->AccessTexture(s.target, UnityVulkanWholeImage, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
            VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, VK_ACCESS_SHADER_READ_BIT, kUnityVulkanResourceAccess_PipelineBarrier, &target))
        s.error = "Unity shader-read transition failed";
}
void UNITY_INTERFACE_API render(int, void*) {
    std::lock_guard<std::mutex> guard(mutex);
    if (!vulkan || !unity.device || !deviceError.empty()) return;
    UnityVulkanRecordingState state{};
    if (!vulkan->CommandRecordingState(&state, kUnityVulkanGraphicsQueueAccess_DontCare)) return;
    for (auto it = sessions.begin(); it != sessions.end();) {
        auto& s = *it->second;
        pump(s, state);
        if (s.stopping && s.frames.empty()) {
            if (s.reader) AImageReader_delete(s.reader);
            it = sessions.erase(it);
        } else ++it;
    }
}
void UNITY_INTERFACE_API deviceEvent(UnityGfxDeviceEventType type) {
    std::lock_guard<std::mutex> guard(mutex);
    if (type == kUnityGfxDeviceEventInitialize) {
        if (!graphics || graphics->GetRenderer() != kUnityGfxRendererVulkan || !vulkan) { deviceError = "Vulkan graphics interface unavailable"; return; }
        unity = vulkan->Instance();
        if (!unity.device || !unity.getInstanceProcAddr) { deviceError = "Waiting for Unity Vulkan device"; return; }
        if (unity.device != configuredDevice) { deviceError = "Required extensions not enabled: restart with preloaded native plugin"; return; }
        auto get = reinterpret_cast<PFN_vkGetDeviceProcAddr>(unity.getInstanceProcAddr(unity.instance, "vkGetDeviceProcAddr"));
        if (!get) { deviceError = "Vulkan device loader unavailable"; return; }
#define LOAD(name) name = reinterpret_cast<PFN_##name>(get(unity.device, #name)); if (!name) { deviceError = "Missing " #name; return; }
        DEVICE_FUNCTIONS(LOAD)
#undef LOAD
        memoryProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceMemoryProperties>(unity.getInstanceProcAddr(unity.instance, "vkGetPhysicalDeviceMemoryProperties"));
        if (!memoryProperties) { deviceError = "Vulkan memory query unavailable"; return; }
        UnityVulkanPluginEventConfig config{};
        config.renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside;
        config.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_DontCare;
        config.flags = kUnityVulkanEventConfigFlag_EnsurePreviousFrameSubmission;
        vulkan->ConfigureEvent(eventId, &config);
        deviceError.clear();
    } else if (type == kUnityGfxDeviceEventShutdown) {
        if (unity.device && vkDeviceWaitIdle) vkDeviceWaitIdle(unity.device); // shutdown only
        for (auto& pair : sessions) {
            auto& s = *pair.second;
            for (auto& frame : s.frames) destroyFrame(s, frame);
            s.frames.clear(); destroyPending(s);
            s.error = "Vulkan device shutdown; recreate the probe";
        }
        deviceError = "Vulkan device shut down"; unity = {}; configuredDevice = VK_NULL_HANDLE;
    }
}
Session* find(uint64_t id) { auto it = sessions.find(id); return it == sessions.end() ? nullptr : it->second.get(); }
}

extern "C" {
UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* interfaces) {
    graphics = interfaces->Get<IUnityGraphics>(); vulkan = interfaces->Get<IUnityGraphicsVulkan>();
    if (!graphics) return;
    eventId = graphics->ReserveEventIDRange(1);
    if (vulkan) vulkan->InterceptInitialization(intercept, nullptr);
    graphics->RegisterDeviceEventCallback(deviceEvent);
    // Also covers late loading, with an explicit unsupported-preload diagnostic.
    deviceEvent(kUnityGfxDeviceEventInitialize);
}
UNITY_INTERFACE_EXPORT void UNITY_INTERFACE_API UnityPluginUnload() {
    if (graphics) graphics->UnregisterDeviceEventCallback(deviceEvent);
}
UNITY_INTERFACE_EXPORT UnityRenderingEventAndData VrGetRenderEvent() { return render; }
UNITY_INTERFACE_EXPORT int VrGetEventId() { return eventId; }
UNITY_INTERFACE_EXPORT uint64_t VrCreate(int width, int height, void* target) {
    std::lock_guard<std::mutex> guard(mutex);
    if (width < 128 || width > 2048 || height < 128 || height > 2048 || !target || !deviceError.empty()) return 0;
    auto s = std::make_unique<Session>(); s->id = nextId++; s->width = width; s->height = height; s->target = target;
    auto result = AImageReader_newWithUsage(width, height, AIMAGE_FORMAT_RGBA_8888,
            AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE | AHARDWAREBUFFER_USAGE_GPU_COLOR_OUTPUT, 8, &s->reader);
    if (result != AMEDIA_OK) { deviceError = "AImageReader creation failed " + std::to_string(result); return 0; }
    auto id = s->id; sessions.emplace(id, std::move(s)); return id;
}
UNITY_INTERFACE_EXPORT int VrAlive(uint64_t id) { std::lock_guard<std::mutex> guard(mutex); return find(id) != nullptr; }
UNITY_INTERFACE_EXPORT void VrAbandon(uint64_t id) {
    // Called only if no Java producer has been created.
    std::lock_guard<std::mutex> guard(mutex); if (auto s = find(id)) s->stopping = true;
}
UNITY_INTERFACE_EXPORT int VrStatus(uint64_t id, char* output, int capacity) {
    if (!output || capacity < 1) return 0;
    std::lock_guard<std::mutex> guard(mutex);
    std::ostringstream text; auto s = find(id);
    text << "Vulkan AHB probe VU1\n" << (deviceError.empty() ? "device extensions enabled" : deviceError);
    if (s) text << "\nGPU copies=" << s->copies << " acquired=" << s->acquired << " inFlight=" << s->frames.size()
                << " slots=" << s->outstanding << " fenceWait=" << s->fenceWaits << " producerSkipped=" << s->skipped
                << "\ntimestamp=" << s->timestamp << " size=" << s->width << "x" << s->height
                << "\n" << (s->error.empty() ? (s->stopping ? "stopping" : "running") : s->error);
    auto result = text.str(); int length = std::min(capacity - 1, static_cast<int>(result.size()));
    memcpy(output, result.data(), length); output[length] = 0; return length;
}
JNIEXPORT jobject JNICALL Java_com_t34400_vrwebview_VulkanProbe_nativeSurface(JNIEnv* env, jclass, jlong id) {
    std::lock_guard<std::mutex> guard(mutex); auto s = find(id); if (!s || s->stopping) return nullptr;
    ANativeWindow* window = nullptr;
    if (AImageReader_getWindow(s->reader, &window) != AMEDIA_OK || !window) return nullptr;
    return ANativeWindow_toSurface(env, window);
}
JNIEXPORT jboolean JNICALL Java_com_t34400_vrwebview_VulkanProbe_nativeReserve(JNIEnv*, jclass, jlong id) {
    std::lock_guard<std::mutex> guard(mutex); auto s = find(id);
    if (!s || s->stopping || !s->error.empty() || !deviceError.empty()) return false;
    // Bound ALL outstanding buffers. This keeps lockHardwareCanvas away from a full queue,
    // including when Unity pauses. No ImageReader callback ever waits on the UI thread.
    if (s->outstanding >= 6) { ++s->skipped; return false; }
    ++s->outstanding; return true;
}
JNIEXPORT void JNICALL Java_com_t34400_vrwebview_VulkanProbe_nativeCancelReserve(JNIEnv*, jclass, jlong id) {
    std::lock_guard<std::mutex> guard(mutex); if (auto s = find(id)) --s->outstanding;
}
JNIEXPORT void JNICALL Java_com_t34400_vrwebview_VulkanProbe_nativeStop(JNIEnv*, jclass, jlong id) {
    std::lock_guard<std::mutex> guard(mutex);
    if (auto s = find(id)) {
        s->stopping = true;
        if (!unity.device && s->frames.empty()) {
            destroyPending(*s); if (s->reader) AImageReader_delete(s->reader); sessions.erase(id);
        }
    }
}
}
