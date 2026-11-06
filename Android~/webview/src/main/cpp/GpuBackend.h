#pragma once
#include "Unity/IUnityGraphics.h"
#include <android/hardware_buffer.h>
#include <cstdint>
#include <memory>
#include <string>

namespace vrwebview {
struct FrameContext { uint64_t current = 0, safe = 0; };
struct GpuFrame { virtual ~GpuFrame() = default; };

// All methods except construction/Name/ForSession run on Unity's render thread.
// Submit returns ownership even after a post-submission error: callers must keep
// the Android image until Complete confirms that the GPU has finished with it.
class GpuBackend : public std::enable_shared_from_this<GpuBackend> {
public:
    virtual ~GpuBackend() = default;
    virtual const char* Name() const = 0;
    virtual std::shared_ptr<GpuBackend> ForSession() { return shared_from_this(); }
    virtual bool BeginFrame(FrameContext& frame, std::string& error) = 0;
    virtual std::unique_ptr<GpuFrame> Submit(AHardwareBuffer* buffer, void* target,
            int width, int height, const FrameContext& frame, std::string& error) = 0;
    virtual bool Complete(GpuFrame& frame, const FrameContext& context, std::string& error) = 0;
    virtual void Release(GpuFrame& frame) = 0;
    virtual void Shutdown() = 0;
};
std::shared_ptr<GpuBackend> CreateVulkanBackend(IUnityInterfaces* interfaces, int eventId, std::string& error);
std::shared_ptr<GpuBackend> CreateGlesBackend();
void InstallVulkanIntercept(IUnityInterfaces* interfaces);
std::string VulkanBootstrapStatus();
}
