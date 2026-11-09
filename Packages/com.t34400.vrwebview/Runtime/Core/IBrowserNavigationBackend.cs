using System;
using System.Threading;
using System.Threading.Tasks;
namespace T34400.VRWebView
{
    /// <summary>Page observations, independent from host readiness or command acceptance.</summary>
    public sealed class BrowserPageState
    {
        public static readonly BrowserPageState Empty = new BrowserPageState("", "", false, false, false, 0, 0);
        public string Address { get; }
        public string Title { get; }
        public bool CanGoBack { get; }
        public bool CanGoForward { get; }
        public bool IsLoading { get; }
        public int Progress { get; }
        public int ErrorCode { get; }
        public BrowserPageState(string address, string title, bool back, bool forward, bool loading, int progress, int errorCode)
        { Address = address ?? ""; Title = title ?? ""; CanGoBack = back; CanGoForward = forward; IsLoading = loading; Progress = Math.Max(0, Math.Min(100, progress)); ErrorCode = errorCode; }
    }
    public enum BrowserPageCommand { Back, Forward, Reload, Stop }
    public interface IBrowserNavigationBackend
    {
        BrowserPageState PageState { get; }
        event Action<BrowserPageState> PageStateChanged;
        Task ExecuteAsync(BrowserPageCommand command, CancellationToken token);
    }
    public interface IBrowserAsyncEditingSink : IBrowserEditingSink
    {
        /// <summary>Completes after native editing ownership has been relinquished.</summary>
        Task EndEditingAsync(CancellationToken token);
    }
}
