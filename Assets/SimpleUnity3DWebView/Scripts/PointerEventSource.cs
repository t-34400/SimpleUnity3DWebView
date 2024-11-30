#nullable enable

using System;

using UnityEngine;
using UnityEngine.EventSystems;

namespace WebView
{
    class PointerEventSource : MonoBehaviour, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        private RectTransform rect = default!;

        public event Action? OnPointerExit;
        public event Action<Vector2>? OnPointerDown;
        public event Action<Vector2>? OnPointerUp;
        public event Action<Vector2>? OnDrag;

#if UNITY_EDITOR
        [SerializeField] private bool showLogs = false;
#endif

        void Start()
        {
            if(transform is RectTransform rectTransform)
            {
                rect = rectTransform;
            }
            else
            {
                Debug.LogError("Component disabled: GameObject does not have a RectTransform");
                enabled = false;
            }
        }

        void IPointerExitHandler.OnPointerExit(PointerEventData eventData)
        {
            OnPointerExit?.Invoke();
#if UNITY_EDITOR
            if(showLogs) 
            { 
                Debug.Log("OnPointerExit"); 
            }
#endif
        }

        void IPointerDownHandler.OnPointerDown(PointerEventData eventData)
        {
            var clickPoint = GetClickPoint(eventData);
            OnPointerDown?.Invoke(clickPoint);
#if UNITY_EDITOR
            if(showLogs) 
            { 
                Debug.Log($"OnPointerDown: {clickPoint}");
            }
#endif
        }

        void IPointerUpHandler.OnPointerUp(PointerEventData eventData)
        {
            var clickPoint = GetClickPoint(eventData);
            OnPointerUp?.Invoke(clickPoint);
#if UNITY_EDITOR
            if(showLogs) 
            { 
                Debug.Log($"OnPointerUp: {clickPoint}");
            }
#endif
        }

        void IDragHandler.OnDrag(PointerEventData eventData)
        {
            var clickPoint = GetClickPoint(eventData);
            OnDrag?.Invoke(clickPoint);
#if UNITY_EDITOR
            if(showLogs) 
            { 
                Debug.Log($"OnDrag: {clickPoint}");
            }
#endif
        }

        private Vector2 GetClickPoint(PointerEventData eventData)
        {
            var rayCast = eventData.pointerCurrentRaycast;
            var screenPosition = eventData.pressEventCamera.WorldToScreenPoint(rayCast.worldPosition);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPosition, eventData.pressEventCamera, out var localPoint);

            var clickPoint = new Vector2(localPoint.x / rect.rect.width + 0.5f, localPoint.y / rect.rect.height + 0.5f);
            return clickPoint;
        }
    }
}