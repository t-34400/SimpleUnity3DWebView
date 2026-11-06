// Experimental Android -> Unity Vulkan transfer. No CPU pixel access.
#define VK_USE_PLATFORM_ANDROID_KHR
#define VK_NO_PROTOTYPES
#include "GpuBackend.h"
#include "Unity/IUnityGraphicsVulkan.h"
#include <algorithm>
#include <cstring>
#include <vector>

namespace vrwebview {
namespace vulkan_detail {
PFN_vkGetInstanceProcAddr loader = nullptr;
PFN_vkCreateDevice createDevice = nullptr;
VkInstance loaderInstance = VK_NULL_HANDLE;
VkDevice configuredDevice = VK_NULL_HANDLE;
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

}
struct VulkanFrame final : GpuFrame {
    VkImage imported = VK_NULL_HANDLE;
    VkDeviceMemory memory = VK_NULL_HANDLE;
    uint64_t frame = 0;
};
class VulkanBackend final : public GpuBackend {
public:
    IUnityGraphicsVulkan* api = nullptr;
    UnityVulkanInstance instance{};
    bool finished = false;
#define DECLARE(name) PFN_##name name = nullptr;
#define DEVICE_FUNCTIONS(X) \
 X(vkGetAndroidHardwareBufferPropertiesANDROID) X(vkCreateImage) X(vkDestroyImage) \
 X(vkAllocateMemory) X(vkFreeMemory) X(vkBindImageMemory) X(vkCmdPipelineBarrier) \
 X(vkCmdCopyImage) X(vkDeviceWaitIdle)
    DEVICE_FUNCTIONS(DECLARE)
#undef DECLARE
    PFN_vkGetPhysicalDeviceMemoryProperties memoryProperties = nullptr;
    const char* Name() const override { return "Vulkan"; }
    bool Initialize(IUnityGraphicsVulkan* graphics, int eventId, std::string& error) {
        api = graphics;
        if (!api) { error = "Vulkan graphics interface unavailable"; return false; }
        instance = api->Instance();
        if (!instance.device || !instance.getInstanceProcAddr) { error = "Waiting for Unity Vulkan device"; return false; }
        if (instance.device != vulkan_detail::configuredDevice) { error = "Required extensions not enabled: restart with preloaded native plugin"; return false; }
        auto get = reinterpret_cast<PFN_vkGetDeviceProcAddr>(instance.getInstanceProcAddr(instance.instance, "vkGetDeviceProcAddr"));
        if (!get) { error = "Vulkan device loader unavailable"; return false; }
#define LOAD(name) name = reinterpret_cast<PFN_##name>(get(instance.device, #name)); if (!name) { error = "Missing " #name; return false; }
        DEVICE_FUNCTIONS(LOAD)
#undef LOAD
        memoryProperties = reinterpret_cast<PFN_vkGetPhysicalDeviceMemoryProperties>(instance.getInstanceProcAddr(instance.instance, "vkGetPhysicalDeviceMemoryProperties"));
        if (!memoryProperties) { error = "Vulkan memory query unavailable"; return false; }
        UnityVulkanPluginEventConfig config{};
        config.renderPassPrecondition = kUnityVulkanRenderPass_EnsureOutside;
        config.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_DontCare;
        config.flags = kUnityVulkanEventConfigFlag_EnsurePreviousFrameSubmission;
        api->ConfigureEvent(eventId, &config);
        return true;
    }
    bool BeginFrame(FrameContext& frame, std::string& error) override {
        if (finished) { error = "Vulkan device stopped; recreate the probe"; return false; }
        UnityVulkanRecordingState state{};
        if (!api->CommandRecordingState(&state, kUnityVulkanGraphicsQueueAccess_DontCare)) return false;
        frame.current = state.currentFrameNumber; frame.safe = state.safeFrameNumber; return true;
    }
    bool Import(AHardwareBuffer* buffer, int width, int height, VulkanFrame& frame, std::string& error) {
        VkAndroidHardwareBufferFormatPropertiesANDROID format{}; format.sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_FORMAT_PROPERTIES_ANDROID;
        VkAndroidHardwareBufferPropertiesANDROID properties{}; properties.sType = VK_STRUCTURE_TYPE_ANDROID_HARDWARE_BUFFER_PROPERTIES_ANDROID;
        properties.pNext = &format;
        if (vkGetAndroidHardwareBufferPropertiesANDROID(instance.device, buffer, &properties) != VK_SUCCESS
                || format.format != VK_FORMAT_R8G8B8A8_UNORM || !(format.formatFeatures & VK_FORMAT_FEATURE_TRANSFER_SRC_BIT)) {
            error = "AHB is not transferable RGBA8 (external-format/YUV path unsupported)"; return false;
        }
        VkExternalMemoryImageCreateInfo external{}; external.sType = VK_STRUCTURE_TYPE_EXTERNAL_MEMORY_IMAGE_CREATE_INFO;
        external.handleTypes = VK_EXTERNAL_MEMORY_HANDLE_TYPE_ANDROID_HARDWARE_BUFFER_BIT_ANDROID;
        VkImageCreateInfo image{}; image.sType = VK_STRUCTURE_TYPE_IMAGE_CREATE_INFO;
        image.pNext = &external; image.imageType = VK_IMAGE_TYPE_2D; image.format = format.format;
        image.extent = {static_cast<uint32_t>(width), static_cast<uint32_t>(height), 1};
        image.mipLevels = 1; image.arrayLayers = 1; image.samples = VK_SAMPLE_COUNT_1_BIT;
        image.tiling = VK_IMAGE_TILING_OPTIMAL; image.usage = VK_IMAGE_USAGE_TRANSFER_SRC_BIT;
        image.sharingMode = VK_SHARING_MODE_EXCLUSIVE; image.initialLayout = VK_IMAGE_LAYOUT_UNDEFINED;
        if (vkCreateImage(instance.device, &image, nullptr, &frame.imported) != VK_SUCCESS) { error = "vkCreateImage AHB failed"; return false; }
        VkPhysicalDeviceMemoryProperties types{}; memoryProperties(instance.physicalDevice, &types);
        uint32_t type = 0;
        for (; type < types.memoryTypeCount; ++type) if (properties.memoryTypeBits & (1u << type)) break;
        if (type == types.memoryTypeCount) { error = "AHB memory type unavailable"; return false; }
        VkImportAndroidHardwareBufferInfoANDROID import{}; import.sType = VK_STRUCTURE_TYPE_IMPORT_ANDROID_HARDWARE_BUFFER_INFO_ANDROID; import.buffer = buffer;
        VkMemoryDedicatedAllocateInfo dedicated{}; dedicated.sType = VK_STRUCTURE_TYPE_MEMORY_DEDICATED_ALLOCATE_INFO;
        dedicated.pNext = &import; dedicated.image = frame.imported;
        VkMemoryAllocateInfo allocation{}; allocation.sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO;
        allocation.pNext = &dedicated; allocation.allocationSize = properties.allocationSize; allocation.memoryTypeIndex = type;
        if (vkAllocateMemory(instance.device, &allocation, nullptr, &frame.memory) != VK_SUCCESS
                || vkBindImageMemory(instance.device, frame.imported, frame.memory, 0) != VK_SUCCESS) {
            error = "AHB import/bind failed"; return false;
        }
        return true;
    }
    std::unique_ptr<GpuFrame> Submit(AHardwareBuffer* buffer, void* targetHandle, int width, int height,
            const FrameContext&, std::string& error) override {
        auto owned = std::make_unique<VulkanFrame>(); auto& frame = *owned;
        if (!Import(buffer, width, height, frame, error)) { Release(frame); return nullptr; }
        UnityVulkanImage target{};
        if (!api->AccessTexture(targetHandle, UnityVulkanWholeImage, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL,
                VK_PIPELINE_STAGE_TRANSFER_BIT, VK_ACCESS_TRANSFER_WRITE_BIT, kUnityVulkanResourceAccess_PipelineBarrier, &target)) {
            error = "Unity AccessTexture failed"; Release(frame); return nullptr;
        }
        if ((target.format != VK_FORMAT_R8G8B8A8_UNORM && target.format != VK_FORMAT_R8G8B8A8_SRGB) || target.extent.width != static_cast<uint32_t>(width)
                || target.extent.height != static_cast<uint32_t>(height) || target.samples != VK_SAMPLE_COUNT_1_BIT
                || !(target.usage & VK_IMAGE_USAGE_TRANSFER_DST_BIT)) {
            error = "Unity target must be matching single-sample RGBA8 with TRANSFER_DST"; Release(frame); return nullptr;
        }
        UnityVulkanRecordingState state{};
        if (!api->CommandRecordingState(&state, kUnityVulkanGraphicsQueueAccess_DontCare)) {
            error = "Unity command buffer unavailable"; Release(frame); return nullptr;
        }
        VkImageMemoryBarrier acquire{}; acquire.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
        acquire.dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT;
        // Android AHB ownership contract: external producer -> foreign queue, GENERAL layout.
        acquire.oldLayout = VK_IMAGE_LAYOUT_GENERAL; acquire.newLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL;
        acquire.srcQueueFamilyIndex = VK_QUEUE_FAMILY_FOREIGN_EXT; acquire.dstQueueFamilyIndex = instance.queueFamilyIndex;
        acquire.image = frame.imported; acquire.subresourceRange = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 1, 0, 1};
        vkCmdPipelineBarrier(state.commandBuffer, VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT, VK_PIPELINE_STAGE_TRANSFER_BIT,
                0, 0, nullptr, 0, nullptr, 1, &acquire);
        VkImageCopy copy{};
        copy.srcSubresource = {VK_IMAGE_ASPECT_COLOR_BIT, 0, 0, 1}; copy.dstSubresource = copy.srcSubresource;
        copy.extent = {static_cast<uint32_t>(width), static_cast<uint32_t>(height), 1};
        vkCmdCopyImage(state.commandBuffer, frame.imported, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                target.image, VK_IMAGE_LAYOUT_TRANSFER_DST_OPTIMAL, 1, &copy);
        auto release = acquire; release.srcAccessMask = VK_ACCESS_TRANSFER_READ_BIT; release.dstAccessMask = 0;
        release.oldLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL; release.newLayout = VK_IMAGE_LAYOUT_GENERAL;
        release.srcQueueFamilyIndex = instance.queueFamilyIndex; release.dstQueueFamilyIndex = VK_QUEUE_FAMILY_FOREIGN_EXT;
        vkCmdPipelineBarrier(state.commandBuffer, VK_PIPELINE_STAGE_TRANSFER_BIT, VK_PIPELINE_STAGE_BOTTOM_OF_PIPE_BIT,
                0, 0, nullptr, 0, nullptr, 1, &release);
        frame.frame = state.currentFrameNumber;
        // Tell Unity about the subsequent shader read, including the transfer-write dependency.
        if (!api->AccessTexture(targetHandle, UnityVulkanWholeImage, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL,
                VK_PIPELINE_STAGE_FRAGMENT_SHADER_BIT, VK_ACCESS_SHADER_READ_BIT, kUnityVulkanResourceAccess_PipelineBarrier, &target))
            error = "Unity shader-read transition failed";
        return owned;
    }
    bool Complete(GpuFrame& value, const FrameContext& context, std::string&) override {
        return finished || static_cast<VulkanFrame&>(value).frame <= context.safe;
    }
    void Release(GpuFrame& value) override {
        auto& frame = static_cast<VulkanFrame&>(value);
        if (frame.imported) vkDestroyImage(instance.device, frame.imported, nullptr);
        if (frame.memory) vkFreeMemory(instance.device, frame.memory, nullptr);
        frame.imported = VK_NULL_HANDLE; frame.memory = VK_NULL_HANDLE;
    }
    void Shutdown() override {
        if (!finished && instance.device && vkDeviceWaitIdle) vkDeviceWaitIdle(instance.device);
        finished = true;
    }
};
std::shared_ptr<GpuBackend> CreateVulkanBackend(IUnityInterfaces* interfaces, int eventId, std::string& error) {
    auto backend = std::make_shared<VulkanBackend>();
    if (!backend->Initialize(interfaces->Get<IUnityGraphicsVulkan>(), eventId, error)) return nullptr;
    return backend;
}
void InstallVulkanIntercept(IUnityInterfaces* interfaces) {
    if (auto api = interfaces->Get<IUnityGraphicsVulkan>()) api->InterceptInitialization(vulkan_detail::intercept, nullptr);
}
}
