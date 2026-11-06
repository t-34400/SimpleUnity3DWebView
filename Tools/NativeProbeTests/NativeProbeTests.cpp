// Executes production shared transport and GPU backends with fake endpoints. No GPU claim.
#include "../../Android~/webview/src/main/cpp/VulkanBackend.cpp"
#include "../../Android~/webview/src/main/cpp/GlesBackend.cpp"
#include "../../Android~/webview/src/main/cpp/GpuProbe.cpp"
using namespace vrwebview;
using namespace vrwebview::vulkan_detail;
#include <cassert>
#include <deque>
#include <iostream>

struct AHardwareBuffer { AHardwareBuffer_Desc description{}; unsigned char pixels[4] = {17, 128, 239, 64}; };
struct AImage { AHardwareBuffer buffer; bool deleted = false; };
struct AImageReader { std::deque<std::pair<AImage*, int>> queued; };
namespace test {
uint64_t current = 10, safe = 3;
int deleted = 0, readersDeleted = 0, importedDestroyed = 0, memoriesFreed = 0, copies = 0;
bool badFormat = false, failBind = false, failReader = false;
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
    static IUnityGraphicsVulkan api{};
    auto v = std::make_shared<VulkanBackend>(); v->api = &api;
    api.CommandRecordingState = recording; api.AccessTexture = target;
    v->instance.device = reinterpret_cast<VkDevice>(0x100); v->instance.queueFamilyIndex = 7;
    v->vkGetAndroidHardwareBufferPropertiesANDROID = ahb; v->vkCreateImage = createImage;
    v->vkDestroyImage = destroyImage; v->vkAllocateMemory = allocate; v->vkFreeMemory = freeMemory;
    v->vkBindImageMemory = bind; v->vkCmdPipelineBarrier = barrier; v->vkCmdCopyImage = copy;
    v->memoryProperties = memoryTypes; activeBackend = v; deviceActive = true; deviceError.clear();
}
}
extern "C" {
media_status_t AImageReader_newWithUsage(int32_t, int32_t, int32_t format, uint64_t usage, int32_t maxImages, AImageReader** out) {
    assert(format == AIMAGE_FORMAT_RGBA_8888 && maxImages >= 6);
    assert((usage & AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE) && (usage & AHARDWAREBUFFER_USAGE_GPU_COLOR_OUTPUT));
    if (test::failReader) return AMEDIA_ERROR_UNKNOWN;
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
void runGlesTests();
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
    failBind = false; current = 20; safe = 10;
    id = VrCreate(1024, 768, reinterpret_cast<void*>(1)); image = post(id); render(0, nullptr);
    assert(!image->deleted); failReader = true;
    assert(!VrCreate(1024, 768, reinterpret_cast<void*>(1)) && deviceError.empty());
    char message[1024]; VrStatus(0, message, sizeof(message));
    assert(strstr(message, "AImageReader creation failed"));
    Java_com_t34400_vrwebview_VulkanProbe_nativeStop(nullptr, nullptr, id); safe = 20; render(0, nullptr);
    assert(!VrAlive(id) && image->deleted); failReader = false;
    std::cout << "PASS failed recreation leaves old GPU generation retireable\n";
    id = VrCreate(1024, 768, reinterpret_cast<void*>(1)); image = post(id); current = 30; render(0, nullptr);
    Java_com_t34400_vrwebview_VulkanProbe_nativeStop(nullptr, nullptr, id);
    assert(VrAlive(id)); deviceEvent(kUnityGfxDeviceEventShutdown);
    assert(!VrAlive(id) && image->deleted);
    std::cout << "PASS graphics shutdown retires acknowledged generations\n";
    runGlesTests();
}

namespace gles_test {
EGLContext currentContext = reinterpret_cast<EGLContext>(0x401);
EGLDisplay currentDisplay = reinterpret_cast<EGLDisplay>(0x402);
const char* eglExtensions = "EGL_ANDROID_get_native_client_buffer EGL_ANDROID_image_native_buffer EGL_KHR_image_base EGL_KHR_fence_sync";
std::vector<const char*> glExtensions = {"GL_OES_EGL_image", "GL_EXT_copy_image"};
int minorVersion = 0;
GLint binding = 77;
GLuint nextTexture = 1000;
GLenum glError = GL_NO_ERROR;
bool gpuDone = false, fenceError = false, failImport = false, failFence = false;
int copies = 0, imagesCreated = 0, imagesDestroyed = 0, texturesDeleted = 0, syncsDestroyed = 0, finishes = 0, flushes = 0;
unsigned char destination[4] = {};
struct Image { AHardwareBuffer* buffer; };
struct Sync {};
std::unordered_map<GLuint, Image*> textures;
std::string copyProc;
EGLClientBuffer EGLAPIENTRY native(const AHardwareBuffer* buffer) { return reinterpret_cast<EGLClientBuffer>(const_cast<AHardwareBuffer*>(buffer)); }
EGLImageKHR EGLAPIENTRY image(EGLDisplay display, EGLContext ctx, EGLenum target, EGLClientBuffer buffer, const EGLint*) {
    assert(display == reinterpret_cast<EGLDisplay>(0x402) && ctx == EGL_NO_CONTEXT && target == EGL_NATIVE_BUFFER_ANDROID);
    ++imagesCreated; return reinterpret_cast<EGLImageKHR>(new Image{reinterpret_cast<AHardwareBuffer*>(buffer)});
}
EGLBoolean EGLAPIENTRY destroyImage(EGLDisplay, EGLImageKHR image) { delete reinterpret_cast<Image*>(image); ++imagesDestroyed; return EGL_TRUE; }
void GL_APIENTRY target(GLenum target, GLeglImageOES image) {
    assert(target == GL_TEXTURE_2D);
    if (failImport) { glError = GL_INVALID_OPERATION; return; }
    textures[static_cast<GLuint>(binding)] = reinterpret_cast<Image*>(image);
}
void GL_APIENTRY copy(GLuint src, GLenum srcTarget, GLint srcLevel, GLint x, GLint y, GLint z,
        GLuint dst, GLenum dstTarget, GLint dstLevel, GLint dx, GLint dy, GLint dz, GLsizei width, GLsizei height, GLsizei depth) {
    assert(srcTarget == GL_TEXTURE_2D && dstTarget == GL_TEXTURE_2D && dst == 1);
    assert(!srcLevel && !dstLevel && !x && !y && !z && !dx && !dy && !dz);
    assert(width == 1024 && height == 768 && depth == 1 && binding == 77);
    memcpy(destination, textures.at(src)->buffer->pixels, 4); ++copies;
}
EGLSyncKHR EGLAPIENTRY sync(EGLDisplay, EGLenum type, const EGLint*) {
    assert(type == EGL_SYNC_FENCE_KHR);
    return failFence ? EGL_NO_SYNC_KHR : reinterpret_cast<EGLSyncKHR>(new Sync);
}
EGLBoolean EGLAPIENTRY destroySync(EGLDisplay, EGLSyncKHR sync) { delete reinterpret_cast<Sync*>(sync); ++syncsDestroyed; return EGL_TRUE; }
EGLint EGLAPIENTRY wait(EGLDisplay, EGLSyncKHR, EGLint flags, EGLTimeKHR timeout) {
    assert(flags == 0 && timeout == 0);
    return fenceError ? EGL_FALSE : gpuDone ? EGL_CONDITION_SATISFIED_KHR : EGL_TIMEOUT_EXPIRED_KHR;
}
UnityGfxRenderer UNITY_INTERFACE_API renderer() { return kUnityGfxRendererOpenGLES30; }
void setup() {
    static IUnityGraphics api{}; api.GetRenderer = renderer; graphics = &api;
    deviceEvent(kUnityGfxDeviceEventInitialize);
    assert(activeBackend && std::string(activeBackend->Name()) == "OpenGLES3");
}
}
extern "C" {
EGLDisplay EGLAPIENTRY eglGetCurrentDisplay() { return gles_test::currentDisplay; }
EGLContext EGLAPIENTRY eglGetCurrentContext() { return gles_test::currentContext; }
const char* EGLAPIENTRY eglQueryString(EGLDisplay, EGLint name) { assert(name == EGL_EXTENSIONS); return gles_test::eglExtensions; }
__eglMustCastToProperFunctionPointerType EGLAPIENTRY eglGetProcAddress(const char* name) {
    using namespace gles_test;
#define PROC(text, fn) if (!strcmp(name, text)) return reinterpret_cast<__eglMustCastToProperFunctionPointerType>(gles_test::fn);
    PROC("eglGetNativeClientBufferANDROID", native)
    PROC("eglCreateImageKHR", image)
    PROC("eglDestroyImageKHR", destroyImage)
    PROC("eglCreateSyncKHR", sync)
    PROC("eglDestroySyncKHR", destroySync)
    PROC("eglClientWaitSyncKHR", wait)
    PROC("glEGLImageTargetTexture2DOES", target)
#undef PROC
    if (!strcmp(name, "glCopyImageSubDataEXT") || !strcmp(name, "glCopyImageSubDataOES") || !strcmp(name, "glCopyImageSubData")) {
        copyProc = name; return reinterpret_cast<__eglMustCastToProperFunctionPointerType>(copy);
    }
    return nullptr;
}
void GL_APIENTRY glGetIntegerv(GLenum name, GLint* value) {
    using namespace gles_test;
    if (name == GL_TEXTURE_BINDING_2D) *value = binding;
    else if (name == GL_MAJOR_VERSION) *value = 3;
    else if (name == GL_MINOR_VERSION) *value = minorVersion;
    else if (name == GL_NUM_EXTENSIONS) *value = static_cast<GLint>(glExtensions.size());
    else assert(false);
}
const GLubyte* GL_APIENTRY glGetStringi(GLenum name, GLuint index) {
    assert(name == GL_EXTENSIONS); return reinterpret_cast<const GLubyte*>(gles_test::glExtensions.at(index));
}
GLboolean GL_APIENTRY glIsTexture(GLuint name) { return name == 1 ? GL_TRUE : GL_FALSE; }
GLenum GL_APIENTRY glGetError() { auto error = gles_test::glError; gles_test::glError = GL_NO_ERROR; return error; }
void GL_APIENTRY glGenTextures(GLsizei count, GLuint* textures) { assert(count == 1); *textures = gles_test::nextTexture++; }
void GL_APIENTRY glBindTexture(GLenum target, GLuint texture) { assert(target == GL_TEXTURE_2D); gles_test::binding = texture; }
void GL_APIENTRY glTexParameteri(GLenum target, GLenum, GLint) { assert(target == GL_TEXTURE_2D); }
void GL_APIENTRY glDeleteTextures(GLsizei count, const GLuint* texture) {
    assert(count == 1); ++gles_test::texturesDeleted; gles_test::textures.erase(*texture);
}
void GL_APIENTRY glFlush() { ++gles_test::flushes; }
void GL_APIENTRY glFinish() { ++gles_test::finishes; }
}
void runGlesTests() {
    using namespace gles_test;
    setup();
    auto id = VrCreate(1024, 768, reinterpret_cast<void*>(1)); auto image = test::post(id);
    render(0, nullptr); assert(copies == 1 && binding == 77 && !image->deleted && flushes == 1 && finishes == 0);
    assert(copyProc == "glCopyImageSubDataEXT" && !memcmp(destination, image->buffer.pixels, 4));
    Java_com_t34400_vrwebview_VulkanProbe_nativeStop(nullptr, nullptr, id);
    render(0, nullptr); assert(VrAlive(id) && !image->deleted && texturesDeleted == 0);
    fenceError = true; render(0, nullptr); assert(VrAlive(id) && !image->deleted && find(id)->error.find("fence wait failed") != std::string::npos); fenceError = false;
    gpuDone = true; render(0, nullptr); assert(!VrAlive(id) && image->deleted && imagesDestroyed == 1 && texturesDeleted == 1 && syncsDestroyed == 1);
    std::cout << "PASS shared GLES transport, raw bytes, Unity binding preservation, asynchronous fence ownership\n";

    failImport = true; gpuDone = false;
    id = VrCreate(1024, 768, reinterpret_cast<void*>(1)); image = test::post(id); render(0, nullptr);
    assert(image->deleted && binding == 77 && copies == 1 && imagesCreated == imagesDestroyed);
    VrAbandon(id); render(0, nullptr); assert(!VrAlive(id)); failImport = false;
    failFence = true;
    id = VrCreate(1024, 768, reinterpret_cast<void*>(1)); image = test::post(id); render(0, nullptr);
    assert(!image->deleted && finishes == 1);
    VrAbandon(id); render(0, nullptr); assert(!VrAlive(id) && image->deleted); failFence = false;
    std::cout << "PASS GLES import and completion-fence failure cleanup\n";

    // A device/context change must never delete the new context's reused GL name.
    id = VrCreate(1024, 768, reinterpret_cast<void*>(1)); image = test::post(id); render(0, nullptr);
    int beforeDeletes = texturesDeleted;
    deviceEvent(kUnityGfxDeviceEventShutdown);
    assert(VrAlive(id) && !image->deleted);
    currentContext = reinterpret_cast<EGLContext>(0x499);
    Java_com_t34400_vrwebview_VulkanProbe_nativeStop(nullptr, nullptr, id);
    render(0, nullptr); assert(VrAlive(id)); gpuDone = true; render(0, nullptr);
    assert(!VrAlive(id) && image->deleted && texturesDeleted == beforeDeletes);
    textures.clear(); // old context destruction frees its GL names; never touch the replacement context
    std::cout << "PASS GLES shutdown keeps in-flight AHB; replacement context's GL names remain untouched\n";

    // Capability checks happen on the render thread, including exact extension tokens.
    currentContext = reinterpret_cast<EGLContext>(0x401);
    glExtensions = {"GL_OES_EGL_image"}; setup();
    id = VrCreate(1024, 768, reinterpret_cast<void*>(1)); test::post(id); render(0, nullptr);
    assert(find(id)->error.find("copy_image") != std::string::npos);
    VrAbandon(id); render(0, nullptr); assert(!VrAlive(id)); deviceEvent(kUnityGfxDeviceEventShutdown);
    assert(!gles_detail::extension("EGL_KHR_image_base_extra", "EGL_KHR_image_base"));
    assert(gles_detail::extension("A EGL_KHR_image_base B", "EGL_KHR_image_base"));
    std::cout << "PASS unsupported GLES capability fails closed and exact extension matching\n";

    glExtensions = {"GL_OES_EGL_image", "GL_OES_copy_image"}; setup();
    id = VrCreate(1024, 768, reinterpret_cast<void*>(1)); test::post(id); render(0, nullptr);
    assert(copyProc == "glCopyImageSubDataOES"); VrAbandon(id); render(0, nullptr); assert(!VrAlive(id)); deviceEvent(kUnityGfxDeviceEventShutdown);
    glExtensions = {"GL_OES_EGL_image"}; minorVersion = 2; setup();
    id = VrCreate(1024, 768, reinterpret_cast<void*>(1)); test::post(id); render(0, nullptr);
    assert(copyProc == "glCopyImageSubData"); VrAbandon(id); render(0, nullptr); assert(!VrAlive(id)); deviceEvent(kUnityGfxDeviceEventShutdown);
    assert(imagesCreated == imagesDestroyed && sessions.empty());
    std::cout << "PASS GLES extension/core entry point selection and all generations retired\n";
    // Recreate in a replacement Unity context without a device reinitialize event.
    gpuDone = false; setup();
    auto oldId = VrCreate(1024, 768, reinterpret_cast<void*>(1)); auto oldImage = test::post(oldId); render(0, nullptr);
    currentContext = reinterpret_cast<EGLContext>(0x499);
    auto newId = VrCreate(1024, 768, reinterpret_cast<void*>(1)); auto newImage = test::post(newId); render(0, nullptr);
    assert(!find(oldId)->error.empty() && find(newId)->error.empty());
    assert(find(newId)->copies == 1 && !oldImage->deleted && !newImage->deleted);
    beforeDeletes = texturesDeleted;
    Java_com_t34400_vrwebview_VulkanProbe_nativeStop(nullptr, nullptr, oldId);
    Java_com_t34400_vrwebview_VulkanProbe_nativeStop(nullptr, nullptr, newId);
    render(0, nullptr); assert(VrAlive(oldId) && VrAlive(newId));
    gpuDone = true; render(0, nullptr);
    assert(!VrAlive(oldId) && !VrAlive(newId) && texturesDeleted == beforeDeletes + 1);
    assert(imagesCreated == imagesDestroyed && oldImage->deleted && newImage->deleted);
    textures.clear(); deviceEvent(kUnityGfxDeviceEventShutdown);
    std::cout << "PASS recreation captures new GLES context while old backend owns pending images\n";
}
