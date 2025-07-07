# nullable enable

using TMPro;
using UnityEngine;

namespace WebView
{
    class AddressBarButtonHandler : MonoBehaviour
    {
        [SerializeField] private TMP_InputField addressBarInput = default!;
        [SerializeField] private WebViewManager webViewManager = default!;

        public void OnClicked()
        {
            var url = addressBarInput.text;
            if (string.IsNullOrEmpty(url))
                return;

            webViewManager.LoadUrl(url);
        }
    }
}