using System;
using System.Threading;
using System.Threading.Tasks;

namespace T34400.VRWebView
{
    /// <summary>Optional trusted application-supplied HTML. Base URL follows NavigationPolicy.
    /// Completion acknowledges submission, not document load, playback or presentation.</summary>
    public interface IBrowserHtmlBackend
    {
        Task LoadHtmlAsync(string html, Uri baseUrl, CancellationToken cancellationToken);
    }
    public interface IBrowserEditingSink { void EndEditing(); }
    /// <summary>Fatal runtime failure. No page content or URLs belong in diagnostics.</summary>
    public interface IBrowserFailureSource { event Action<Exception> Failed; }
}
