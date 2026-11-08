using System;

namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Editable address text, not a committed page location or history.</summary>
    public sealed class BrowserAddressDraft
    {
        public sealed class Submission
        {
            internal readonly BrowserAddressDraft Owner;
            internal readonly long Revision;
            public string Text { get; }
            internal Submission(BrowserAddressDraft owner, long revision, string text)
            { Owner = owner; Revision = revision; Text = text; }
        }
        private long revision;
        public string Text { get; private set; } = "";

        public void Edit(string text)
        {
            text = text ?? "";
            if (Text == text) return;
            long next = checked(revision + 1); Text = text; revision = next;
        }
        public Submission Capture() { return new Submission(this, revision, Text); }
        public bool Acknowledge(Submission submitted, Uri requestedAddress)
        {
            if (submitted == null || !ReferenceEquals(submitted.Owner, this) || submitted.Revision != revision) return false;
            if (requestedAddress == null || !requestedAddress.IsAbsoluteUri) throw new ArgumentException("An absolute requested address is required", nameof(requestedAddress));
            long next = checked(revision + 1); Text = requestedAddress.AbsoluteUri; revision = next;
            return true;
        }
    }
}
