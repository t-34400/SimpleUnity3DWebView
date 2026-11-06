// Executes the production bridge with fake Android/Vulkan endpoints. No GPU claim.
#include "../../Android~/webview/src/main/cpp/VulkanProbe.cpp"
#include <cassert>
#include <deque>
#include <iostream>

struct AHardwareBuffer { AHardwareBuffer_Desc description{}; };
struct AImage { AHardwareBuffer buffer; bool deleted = false; };
struct AImageReader { std::deque<std::pair<AImage*, int>> queued; };
namespace test {
uint64_t current = 10, safe = 3;
int deleted = 0, readersDeleted = 0, importedDestroyed = 0, memoriesFreed = 0, copies = 0;
bool badFormat = false, failBind = false;
std::vector<VkImageMemoryBarrier> barriers;
std::vector<VkImageLayout> targetLayouts;
std::vector<std::unique_ptr<AImage>> images;
AImage* post(uint64_t id, int fence = -1) {
    assert(Java_com_t34400_vrwebview_VulkanProbe_nativeReserve(nullptr, nullptr, id));
    auto image = std::make_unique<AImage>();
    image->buffer.description.width = 1024; image->buffer.description.height = 768;
    image->buffer.description.layers = 1; image->buffer.description.format = AHARDWAREBUFFER_FORMAT_R8G8B8A8_UNORM;
    auto ptr = image.get(); images.push_back(std::move(image));
    find(id)->reader->queued.emplace_back(ptr, fence); return ptr;
}
VkResult VKAPI_CALL ahb(VkDevice, const AHardwareBuffer*, VkAndroidHardwareBufferPropertiesANDROID* p) {
    p->allocationSize = 1024 * 768 * 4; p->memoryTypeBits = 1;
    auto f = static_cast<VkAndroidHardwareBufferFormatPropertiesANDROID*>(p->pNext);
    f->format = badFormat ? VK_FORMAT_UNDEFINED : VK_FORMAT_R8G8B8A8_UNORM;
    f->formatFeatures = VK_FORMAT_FEATURE_TRANSFER_SRC_BIT; return VK_SUCCESS;
}
VkResult VKAPI_CALL createImage(VkDevice, const VkImageCreateInfo*, const VkAllocationCallbacks*, VkImage* out) {
    *out = reinterpret_cast<VkImage>(0x101); return VK_SUCCESS;
}
void VKAPI_CALL destroyImage(VkDevice, VkImage, const VkAllocationCallbacks*) { ++importedDestroyed; }
VkResult VKAPI_CALL allocate(VkDevice, const VkMemoryAllocateInfo* info, const VkAllocationCallbacks*, VkDeviceMemory* out) {
    assert(info->allocationSize == 1024 * 768 * 4);
    assert(static_cast<const VkMemoryDedicatedAllocateInfo*>(info->pNext)->image != VK_NULL_HANDLE);
    *out = reinterpret_cast<VkDeviceMemory>(0x102); return VK_SUCCESS;
}
void VKAPI_CALL freeMemory(VkDevice, VkDeviceMemory, const VkAllocationCallbacks*) { ++memoriesFreed; }
VkResult VKAPI_CALL bind(VkDevice, VkImage, VkDeviceMemory, VkDeviceSize) { return failBind ? VK_ERROR_OUT_OF_DEVICE_MEMORY : VK_SUCCESS; }
void VKAPI_CALL memoryTypes(VkPhysicalDevice, VkPhysicalDeviceMemoryProperties* p) { p->memoryTypeCount = 1; }
void VKAPI_CALL barrier(VkCommandBuffer, VkPipelineStageFlags, VkPipelineStageFlags, VkDependencyFlags,
        uint32_t, const VkMemoryBarrier*, uint32_t, const VkBufferMemoryBarrier*, uint32_t n, const VkImageMemoryBarrier* b) {
    assert(n == 1); barriers.push_back(*b);
}
void VKAPI_CALL copy(VkCommandBuffer, VkImage, VkImageLayout, VkImage, VkImageLayout, uint32_t n, const VkImageCopy* c) {
    assert(n == 1 && c->extent.width == 1024 && c->extent.height == 768); ++copies;
}
bool UNITY_INTERFACE_API recording(UnityVulkanRecordingState* state, UnityVulkanGraphicsQueueAccess) {
    state->currentFrameNumber = current; state->safeFrameNumber = safe;
    state->commandBuffer = reinterpret_cast<VkCommandBuffer>(0x103); return true;
}
bool UNITY_INTERFACE_API target(void*, const VkImageSubresource*, VkImageLayout layout, VkPipelineStageFlags,
        VkAccessFlags, UnityVulkanResourceAccessMode, UnityVulkanImage* image) {
    targetLayouts.push_back(layout); image->format = VK_FORMAT_R8G8B8A8_UNORM;
    image->extent = {1024, 768, 1}; image->samples = VK_SAMPLE_COUNT_1_BIT;
    image->usage = VK_IMAGE_USAGE_TRANSFER_DST_BIT; image->image = reinterpret_cast<VkImage>(0x104); return true;
}
bool deviceUnsupported = false, rejectAddedExtensions = false;
int createCalls = 0;
std::vector<std::string> enabledExtensions;
VkResult VKAPI_CALL enumerate(VkPhysicalDevice, const char*, uint32_t* count, VkExtensionProperties* out) {
    if (!out) { *count = deviceUnsupported ? 0 : 2; return VK_SUCCESS; }
    strcpy(out[0].extensionName, VK_ANDROID_EXTERNAL_MEMORY_ANDROID_HARDWARE_BUFFER_EXTENSION_NAME);
    strcpy(out[1].extensionName, VK_EXT_QUEUE_FAMILY_FOREIGN_EXTENSION_NAME); return VK_SUCCESS;
}
void VKAPI_CALL properties(VkPhysicalDevice, VkPhysicalDeviceProperties* out) { out->apiVersion = VK_API_VERSION_1_1; }
VkResult VKAPI_CALL device(VkPhysicalDevice, const VkDeviceCreateInfo* info, const VkAllocationCallbacks*, VkDevice* out) {
    ++createCalls; enabledExtensions.clear();
    for (uint32_t i = 0; i < info->enabledExtensionCount; ++i) enabledExtensions.push_back(info->ppEnabledExtensionNames[i]);
    if (rejectAddedExtensions && info->enabledExtensionCount > 1) return VK_ERROR_EXTENSION_NOT_PRESENT;
    *out = reinterpret_cast<VkDevice>(0x100); return VK_SUCCESS;
}
PFN_vkVoidFunction VKAPI_CALL getProc(VkInstance, const char* name) {
    if (!strcmp(name, "vkEnumerateDeviceExtensionProperties")) return reinterpret_cast<PFN_vkVoidFunction>(enumerate);
    if (!strcmp(name, "vkGetPhysicalDeviceProperties")) return reinterpret_cast<PFN_vkVoidFunction>(properties);
    return nullptr;
}
void setup() {
    static IUnityGraphicsVulkan api{}; vulkan = &api;
    api.CommandRecordingState = recording; api.AccessTexture = target;
    unity.device = reinterpret_cast<VkDevice>(0x100); unity.queueFamilyIndex = 7;
    vkGetAndroidHardwareBufferPropertiesANDROID = ahb; vkCreateImage = createImage;
    vkDestroyImage = destroyImage; vkAllocateMemory = allocate; vkFreeMemory = freeMemory;
    vkBindImageMemory = bind; vkCmdPipelineBarrier = barrier; vkCmdCopyImage = copy;
    memoryProperties = memoryTypes; deviceError.clear();
}
}
extern "C" {
media_status_t AImageReader_newWithUsage(int32_t, int32_t, int32_t format, uint64_t usage, int32_t maxImages, AImageReader** out) {
    assert(format == AIMAGE_FORMAT_RGBA_8888 && maxImages >= 6);
    assert((usage & AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE) && (usage & AHARDWAREBUFFER_USAGE_GPU_COLOR_OUTPUT));
    *out = new AImageReader; return AMEDIA_OK;
}
media_status_t AImageReader_acquireNextImageAsync(AImageReader* r, AImage** out, int* fence) {
    if (r->queued.empty()) return AMEDIA_IMGREADER_NO_BUFFER_AVAILABLE;
    *out = r->queued.front().first; *fence = r->queued.front().second; r->queued.pop_front(); return AMEDIA_OK;
}
void AImage_delete(AImage* image) { assert(!image->deleted); image->deleted = true; ++test::deleted; }
void AImage_deleteAsync(AImage* image, int fence) { if (fence >= 0) close(fence); AImage_delete(image); }
void AImageReader_delete(AImageReader* reader) {
    for (auto pair : reader->queued) AImage_deleteAsync(pair.first, pair.second);
    delete reader; ++test::readersDeleted;
}
media_status_t AImage_getHardwareBuffer(const AImage* image, AHardwareBuffer** out) { *out = const_cast<AHardwareBuffer*>(&image->buffer); return AMEDIA_OK; }
void AHardwareBuffer_describe(const AHardwareBuffer* buffer, AHardwareBuffer_Desc* out) { *out = buffer->description; }
media_status_t AImage_getTimestamp(const AImage*, int64_t* out) { *out = 1234567; return AMEDIA_OK; }
media_status_t AImageReader_getWindow(AImageReader*, ANativeWindow**) { return AMEDIA_ERROR_UNKNOWN; }
jobject ANativeWindow_toSurface(JNIEnv*, ANativeWindow*) { return nullptr; }
}
int main() {
    using namespace test;
    loader = getProc; createDevice = device;
    const char* existing[] = {VK_EXT_QUEUE_FAMILY_FOREIGN_EXTENSION_NAME};
    VkDeviceCreateInfo deviceInfo{}; deviceInfo.sType = VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO;
    deviceInfo.enabledExtensionCount = 1; deviceInfo.ppEnabledExtensionNames = existing;
    VkDevice out;
    assert(hookCreateDevice(VK_NULL_HANDLE, &deviceInfo, nullptr, &out) == VK_SUCCESS);
    assert(createCalls == 1 && enabledExtensions.size() == 2 && configuredDevice == out);
    deviceUnsupported = true; configuredDevice = VK_NULL_HANDLE;
    assert(hookCreateDevice(VK_NULL_HANDLE, &deviceInfo, nullptr, &out) == VK_SUCCESS);
    assert(createCalls == 2 && enabledExtensions.size() == 1 && configuredDevice == VK_NULL_HANDLE);
    deviceUnsupported = false; rejectAddedExtensions = true;
    assert(hookCreateDevice(VK_NULL_HANDLE, &deviceInfo, nullptr, &out) == VK_SUCCESS);
    assert(createCalls == 4 && enabledExtensions.size() == 1 && configuredDevice == VK_NULL_HANDLE);
    std::cout << "PASS extension negotiation, deduplication, unsupported device and Unity device-creation fallback\n";
    setup();
    assert(VrCreate(0, 768, reinterpret_cast<void*>(1)) == 0);
    auto id = VrCreate(1024, 768, reinterpret_cast<void*>(1)); assert(id);
    int pipeFds[2]; assert(pipe(pipeFds) == 0);
    auto image = post(id, pipeFds[0]);
    render(0, nullptr);
    assert(copies == 0 && !image->deleted && find(id)->fenceWaits == 1);
    assert(write(pipeFds[1], "x", 1) == 1); close(pipeFds[1]);
    render(0, nullptr);
    assert(copies == 1 && !image->deleted && find(id)->frames.size() == 1);
    assert(barriers.size() == 2 && barriers[0].srcQueueFamilyIndex == VK_QUEUE_FAMILY_FOREIGN_EXT);
    assert(barriers[0].dstQueueFamilyIndex == 7 && barriers[1].dstQueueFamilyIndex == VK_QUEUE_FAMILY_FOREIGN_EXT);
    assert(barriers[1].newLayout == VK_IMAGE_LAYOUT_GENERAL);
    assert(targetLayouts[0] == VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL && targetLayouts[1] == VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL);
    Java_com_t34400_vrwebview_VulkanProbe_nativeStop(nullptr, nullptr, id);
    safe = 9; render(0, nullptr); assert(VrAlive(id) && !image->deleted);
    safe = 10; render(0, nullptr); assert(!VrAlive(id) && image->deleted);
    assert(memoriesFreed == 1 && importedDestroyed == 1 && readersDeleted == 1);
    std::cout << "PASS acquire fence and Unity safe-frame ownership, transfer barriers, deferred destruction\n";

    // Pause a saturated producer before the render thread has acquired any image.
    id = VrCreate(1024, 768, reinterpret_cast<void*>(1));
    for (int i = 0; i < 6; ++i) post(id);
    assert(!Java_com_t34400_vrwebview_VulkanProbe_nativeReserve(nullptr, nullptr, id));
    Java_com_t34400_vrwebview_VulkanProbe_nativeStop(nullptr, nullptr, id);
    render(0, nullptr); assert(!VrAlive(id) && deleted == 7);
    assert(!Java_com_t34400_vrwebview_VulkanProbe_nativeReserve(nullptr, nullptr, id));
    std::cout << "PASS bounded producer queue, stop with queued frames, stale generation rejection\n";

    // Stop while waiting on an unsignaled acquire fence: hand the fence back to Android.
    id = VrCreate(1024, 768, reinterpret_cast<void*>(1)); assert(pipe(pipeFds) == 0);
    image = post(id, pipeFds[0]); render(0, nullptr);
    Java_com_t34400_vrwebview_VulkanProbe_nativeStop(nullptr, nullptr, id); render(0, nullptr);
    close(pipeFds[1]); assert(!VrAlive(id) && image->deleted && copies == 1);
    std::cout << "PASS stop during producer fence wait\n";

    badFormat = true; id = VrCreate(1024, 768, reinterpret_cast<void*>(1)); image = post(id); render(0, nullptr);
    assert(copies == 1 && image->deleted && !find(id)->error.empty());
    assert(!Java_com_t34400_vrwebview_VulkanProbe_nativeReserve(nullptr, nullptr, id));
    VrAbandon(id); render(0, nullptr); assert(!VrAlive(id)); badFormat = false;
    failBind = true; id = VrCreate(1024, 768, reinterpret_cast<void*>(1)); image = post(id); render(0, nullptr);
    assert(image->deleted && copies == 1 && importedDestroyed == 2 && memoriesFreed == 2);
    VrAbandon(id); render(0, nullptr); assert(!VrAlive(id));
    std::cout << "PASS unsupported format and failed import cleanup\n";
}
