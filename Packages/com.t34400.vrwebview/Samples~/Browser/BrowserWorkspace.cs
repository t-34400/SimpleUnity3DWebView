using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Optional browser sample model. Create/select/close and all observer
    /// delivery run on the supplied owner context. There is no default/fake backend.</summary>
    public sealed class BrowserWorkspace : IDisposable
    {
        private readonly Func<IBrowserBackend> createBackend;
        private readonly Action<Action> dispatch;
        private readonly List<BrowserTab> tabs = new List<BrowserTab>();
        private bool disposed, creating;
        private long nextId, notificationVersion;
        public ReadOnlyCollection<BrowserTab> Tabs { get; }
        public BrowserTab ActiveTab { get; private set; }
        public Exception LastNotificationError { get; private set; }
        public event Action Changed;

        public BrowserWorkspace(Func<IBrowserBackend> backendFactory, Action<Action> dispatcher)
        {
            createBackend = backendFactory ?? throw new ArgumentNullException(nameof(backendFactory));
            dispatch = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            Tabs = tabs.AsReadOnly();
        }

        public BrowserTab CreateTab(bool activate = true)
        {
            ThrowIfDisposed();
            if (creating) throw new InvalidOperationException("Backend factory cannot reenter tab creation");
            long id = checked(nextId + 1);
            IBrowserBackend backend;
            creating = true;
            try { backend = createBackend() ?? throw new InvalidOperationException("Backend factory returned null"); }
            finally { creating = false; }
            if (disposed) { backend.Dispose(); throw new ObjectDisposedException(nameof(BrowserWorkspace)); }
            var tab = new BrowserTab(id, new BrowserSession(backend, dispatch), dispatch, TabChanged);
            nextId = id; tabs.Add(tab);
            if (activate || ActiveTab == null) ActiveTab = tab;
            Notify();
            return tab;
        }

        public bool SelectTab(long id)
        {
            ThrowIfDisposed();
            var tab = tabs.Find(value => value.Id == id);
            if (tab == null) return false;
            if (tab != ActiveTab) { ActiveTab = tab; Notify(); }
            return true;
        }

        public bool CloseTab(long id)
        {
            ThrowIfDisposed();
            int index = tabs.FindIndex(value => value.Id == id);
            if (index < 0) return false;
            var tab = tabs[index]; tabs.RemoveAt(index);
            if (ActiveTab == tab) ActiveTab = tabs.Count == 0 ? null : tabs[Math.Min(index, tabs.Count - 1)];
            try { tab.Close(); }
            finally { Notify(); }
            return true;
        }

        private void TabChanged(BrowserTab tab)
        {
            if (!disposed && !tab.IsClosed && tabs.Contains(tab)) Notify();
        }

        private void Notify()
        {
            long version = notificationVersion;
            try { dispatch(() => {
                if (version != notificationVersion) return;
                var handlers = Changed;
                if (handlers == null) return;
                foreach (Action handler in handlers.GetInvocationList()) {
                    if (version != notificationVersion) break;
                    try { handler(); }
                    catch (Exception error) { LastNotificationError = error; }
                }
            }); }
            catch (Exception error) { LastNotificationError = error; }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; ++notificationVersion;
            var owned = tabs.ToArray(); tabs.Clear(); ActiveTab = null;
            var failures = new List<Exception>();
            foreach (var tab in owned) {
                try { tab.Close(); }
                catch (Exception error) { failures.Add(error); }
            }
            Notify();
            if (failures.Count != 0) throw new AggregateException("One or more tabs failed to close", failures);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(BrowserWorkspace));
        }
    }
}
