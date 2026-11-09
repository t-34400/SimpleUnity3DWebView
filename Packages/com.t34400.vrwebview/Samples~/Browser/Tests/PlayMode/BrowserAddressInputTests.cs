using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace T34400.VRWebView.Samples.Browser.Tests
{
    public sealed class BrowserAddressInputTests
    {
        private GameObject canvas, events;
        private EventSystem previous;
        private BrowserAddressInput field;
        private TaskCompletionSource<bool> handoff;
        [SetUp] public void Setup() {
            previous = EventSystem.current; if (previous != null) previous.enabled = false;
            events = new GameObject("Events", typeof(EventSystem));
            canvas = new GameObject("Canvas", typeof(Canvas)); canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var node = new GameObject("Address", typeof(RectTransform), typeof(Image), typeof(BrowserAddressInput)); node.transform.SetParent(canvas.transform, false);
            var text = new GameObject("Text", typeof(RectTransform), typeof(Text)); text.transform.SetParent(node.transform, false);
            text.GetComponent<Text>().font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            field = node.GetComponent<BrowserAddressInput>(); field.textComponent = text.GetComponent<Text>();
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            handoff = new TaskCompletionSource<bool>(); field.PrepareEditing = () => handoff.Task;
        }
        [TearDown] public void Cleanup() {
            Object.DestroyImmediate(canvas); Object.DestroyImmediate(events); if (previous != null) previous.enabled = true;
        }
        [UnityTest] public IEnumerator XRClickExplicitlySelectsBeforeWaitingForNativeEditing() {
            field.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
            Assert.AreSame(field.gameObject, EventSystem.current.currentSelectedGameObject);
            Assert.IsFalse(field.isFocused); yield return null; Assert.IsFalse(field.isFocused);
        }
        [UnityTest] public IEnumerator CompletingAnOldHandoffCannotStealAnotherSelection() {
            field.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
            var other = new GameObject("Other", typeof(RectTransform), typeof(Button)); other.transform.SetParent(canvas.transform, false);
            EventSystem.current.SetSelectedGameObject(other); handoff.SetResult(true);
            yield return null; yield return null;
            Assert.AreSame(other, EventSystem.current.currentSelectedGameObject); Assert.IsFalse(field.isFocused);
        }
    }
}
