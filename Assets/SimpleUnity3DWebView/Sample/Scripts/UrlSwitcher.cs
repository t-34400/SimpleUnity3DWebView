#nullable enable

using UnityEngine;
using WebView;

public class UrlSwitcher : MonoBehaviour
{
    [SerializeField] private WebViewControllerClient controller = default!;

    private string targetUrl = "";

    public void SetTargetUrl(string url)
    {
        targetUrl = url;
    }

    public void SwitchUrl()
    {
        controller.LoadUrl(targetUrl);
    }
}
