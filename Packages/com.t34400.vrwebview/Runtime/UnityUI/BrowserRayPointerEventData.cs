using UnityEngine;
using UnityEngine.EventSystems;
namespace T34400.VRWebView
{
    public interface IBrowserSpatialDragHandler : IEventSystemHandler { }

    /// <summary>Optional world-ray information for spatial UI. XR integrations may
    /// supply this event data without depending on a particular XR SDK.</summary>
    public sealed class BrowserRayPointerEventData : PointerEventData
    {
        public Quaternion WorldRotation { get; set; }
        public bool HasWorldRotation { get; set; }
        public Ray WorldRay { get; set; }
        public bool HasWorldRay { get; set; }
        public BrowserRayPointerEventData(EventSystem system) : base(system) { }
    }
}
