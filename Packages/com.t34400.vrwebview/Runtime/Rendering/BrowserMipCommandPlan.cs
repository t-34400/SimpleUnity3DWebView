using System;
using System.Collections.Generic;
namespace T34400.VRWebView
{
    /// <summary>Owner-thread command ordering and target membership, independent of Unity/GPU execution.</summary>
    internal sealed class BrowserMipCommandPlan<T> where T : class
    {
        private readonly Dictionary<ulong, T> targets = new Dictionary<ulong, T>();
        private readonly List<KeyValuePair<ulong, T>> frame = new List<KeyValuePair<ulong, T>>();
        private bool recording;
        internal int Count => targets.Count;
        internal void Add(ulong generation, T target) {
            if (generation == 0 || target == null) throw new ArgumentException("A live generation and target are required");
            targets.Add(generation, target);
        }
        internal void Remove(ulong generation) { targets.Remove(generation); }
        internal int Record(Action copy, Action<T> generate) {
            if (recording) throw new InvalidOperationException("Render command recording cannot be nested");
            recording = true;
            try {
                frame.AddRange(targets);
                copy(); // Always issue the native event, including retirement-only frames.
                int count = 0;
                foreach (var entry in frame) {
                    if (!targets.TryGetValue(entry.Key, out var current) || !ReferenceEquals(current, entry.Value)) continue;
                    generate(entry.Value); count++;
                }
                return count;
            } finally { frame.Clear(); recording = false; }
        }
        internal void Clear() { targets.Clear(); if (!recording) frame.Clear(); }
    }
}
