using System;
using System.Threading;
using System.Threading.Tasks;
namespace T34400.VRWebView
{
    public static class BrowserContentPolicy {
        public static void ValidateScript(string script) { if (script == null || script.Length > 1024 * 1024) throw new ArgumentException("Script must be at most one million UTF-16 code units", nameof(script)); }
        public static void ValidateTextZoom(int percent) { if (percent < 50 || percent > 200) throw new ArgumentOutOfRangeException(nameof(percent)); }
        public static void ValidateFind(string query, long revision) { if (query == null || query.Length > 4096 || revision < 0) throw new ArgumentException("Invalid find query or revision"); }
    }
    public sealed class BrowserFindResult {
        public long Revision { get; }
        public int ActiveMatch { get; }
        public int MatchCount { get; }
        public bool IsComplete { get; }
        public BrowserFindResult(long revision, int active, int count, bool complete) { Revision = revision; ActiveMatch = active; MatchCount = count; IsComplete = complete; }
    }
    public sealed class BrowserContextTarget {
        public string Address { get; }
        public bool IsImage { get; }
        public BrowserContextTarget(string address, bool image) { Address = NavigationPolicy.Parse(address).AbsoluteUri; IsImage = image; }
    }
    /// <summary>Optional ready-page content API. Call on the backend owner context.
    /// Events use that context. Script results are JSON encoded values (including null);
    /// they do not await Promises or imply successful JavaScript evaluation.
    /// Cancellation abandons the result, not already submitted JavaScript effects.</summary>
    public interface IBrowserContentBackend {
        Task<string> EvaluateJavaScriptAsync(string script, CancellationToken token = default);
        Task SetTextZoomAsync(int percent, CancellationToken token = default);
        Task FindAsync(string query, long revision, CancellationToken token = default);
        Task FindNextAsync(bool forward, CancellationToken token = default);
        Task ClearFindAsync(CancellationToken token = default);
        event Action<BrowserFindResult> FindResultChanged;
        event Action<BrowserContextTarget> ContextRequested;
    }
}
