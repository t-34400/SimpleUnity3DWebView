using System;
using System.Collections.Generic;
using System.Linq;
using T34400.VRWebView;
using T34400.VRWebView.Presentation;

internal static class HoverTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class Sink : IBrowserPointerSink, IBrowserHoverSink
    {
        internal readonly List<string> Events = new List<string>();
        internal Action<BrowserHoverEvent> OnHover;
        public void SendPointer(BrowserPointerEvent value) { Events.Add("touch:"+value.Phase); }
        public void SendHover(BrowserHoverEvent value) { Events.Add("hover:"+value.Phase); OnHover?.Invoke(value); }
    }
    private sealed class TouchOnly : IBrowserPointerSink { public void SendPointer(BrowserPointerEvent value) {} }
    private static BrowserHoverEvent H(int id, HoverPhase phase, float x=.25f, float y=.5f) => new BrowserHoverEvent(id,phase,x,y);
    internal static IEnumerable<(string Name, Action Run)> Scenarios()
    {
        yield return ("hover ownership rejects foreign movement and invalid coordinates", () => {
            var capture = new HoverCapture(); var events = new List<BrowserHoverEvent>(); capture.Received += events.Add;
            Check(!capture.Send(1,HoverPhase.Move,0,0), "Move created a hover without Enter");
            Check(!capture.Send(1,HoverPhase.Enter,float.NaN,0), "NaN took ownership");
            Check(!capture.Send(1,(HoverPhase)9,0,0), "Invalid phase took ownership");
            Check(capture.Send(1,HoverPhase.Enter,.1f,.2f), "Enter rejected");
            Check(!capture.Send(2,HoverPhase.Move,1,1), "Foreign move stole hover");
            capture.Exit(2); Check(capture.IsHovering, "Foreign exit cleared hover");
            capture.Exit(1); capture.Exit(1);
            Check(events.Count==2 && events[1].Phase==HoverPhase.Exit && events[1].X==.1f && !capture.IsHovering, "Exit lost last coordinates or duplicated");
        });
        yield return ("stationary hover does not enqueue repeated native motion", () => {
            var capture = new HoverCapture(); int moves = 0;
            capture.Received += value => { if(value.Phase == HoverPhase.Move) moves++; };
            capture.Send(1,HoverPhase.Enter,.2f,.3f);
            Check(capture.Send(1,HoverPhase.Move,.2f,.3f) && moves==0, "Stationary ray generated motion");
            Check(capture.Send(1,HoverPhase.Move,.3f,.3f) && moves==1, "Actual motion lost");
        });
        yield return ("hover exit may reenter with a new owner without stale cleanup", () => {
            var capture = new HoverCapture();
            capture.Received += value => { if(value.Phase==HoverPhase.Exit) capture.Send(2,HoverPhase.Enter,.4f,.5f); };
            capture.Send(1,HoverPhase.Enter,0,0); capture.Exit();
            Check(capture.ActiveSourceId==2, "Old exit erased new owner");
        });
        yield return ("rebinding ends the previous hover before moving the new page", () => {
            var router = new BrowserPointerRouter(); var first = new Sink(); var second = new Sink();
            router.Bind(first); Check(router.SendHover(H(1,HoverPhase.Enter)),"Enter rejected");
            router.Bind(second);
            Check(first.Events.SequenceEqual(new[]{"hover:Enter","hover:Exit"}),"Old hover did not exit");
            Check(!router.SendHover(H(1,HoverPhase.Move)),"Move crossed target without enter");
            Check(router.SendHover(H(2,HoverPhase.Enter)),"New target cannot enter");
        });
        yield return ("touch down ends mouse hover and blocks hover during a captured drag", () => {
            var router = new BrowserPointerRouter(); var sink = new Sink(); router.Bind(sink);
            router.SendHover(H(1,HoverPhase.Enter)); router.Send(new BrowserPointerEvent(1,PointerPhase.Down,.2f,.3f));
            Check(sink.Events.SequenceEqual(new[]{"hover:Enter","hover:Exit","touch:Down"}),"Mouse/touch ordering changed");
            Check(!router.SendHover(H(2,HoverPhase.Enter)),"Hover entered during drag");
            router.Send(new BrowserPointerEvent(1,PointerPhase.Up,.2f,.3f));
            Check(router.SendHover(H(1,HoverPhase.Enter)),"Hover cannot resume after up");
        });
        yield return ("hover failure is observable and releases the uncertain hover", () => {
            var router = new BrowserPointerRouter(); var sink = new Sink(); router.Bind(sink);
            sink.OnHover = value => { if(value.Phase==HoverPhase.Move) throw new InvalidOperationException("move failed"); };
            router.SendHover(H(1,HoverPhase.Enter));
            Check(!router.SendHover(H(1,HoverPhase.Move)) && router.LastError is InvalidOperationException,"Hover error hidden");
            Check(sink.Events.Last()=="hover:Exit", "Uncertain hover not cleaned up");
            Check(router.SendHover(H(2,HoverPhase.Enter)),"Failed hover blocked new owner");
        });
        yield return ("a reentrant hover exit cannot send touch down to an obsolete binding", () => {
            var router = new BrowserPointerRouter(); var old = new Sink(); var next = new Sink();
            router.Bind(old); router.SendHover(H(1,HoverPhase.Enter));
            old.OnHover = value => { if(value.Phase==HoverPhase.Exit) router.Bind(next); };
            Check(!router.Send(new BrowserPointerEvent(1,PointerPhase.Down,0,0)),"Obsolete down accepted");
            Check(!old.Events.Contains("touch:Down") && next.Events.Count==0,"Down escaped to old or new page");
            Check(router.SendHover(H(2,HoverPhase.Enter)),"Reentrant target lost");
        });
        yield return ("legacy touch-only backends remain usable without hover capability", () => {
            var router = new BrowserPointerRouter(); router.Bind(new TouchOnly());
            Check(!router.SendHover(H(1,HoverPhase.Enter)),"Touch-only backend accepted hover");
            Check(router.LastError==null && router.Send(new BrowserPointerEvent(1,PointerPhase.Down,0,0)),"Unsupported hover broke touch");
        });
        yield return ("foreign and nonfinite hover events cannot clear the active route", () => {
            var router = new BrowserPointerRouter(); var sink = new Sink(); router.Bind(sink);
            router.SendHover(H(1,HoverPhase.Enter));
            Check(!router.SendHover(H(2,HoverPhase.Exit)),"Foreign hover exit accepted");
            Check(!router.SendHover(H(1,HoverPhase.Move,float.PositiveInfinity)),"Nonfinite move accepted");
            router.ClearHover(2); Check(sink.Events.Count==1,"Foreign clear exited route");
            router.ClearHover(1); router.ClearHover(1);
            Check(sink.Events.SequenceEqual(new[]{"hover:Enter","hover:Exit"}),"Cleanup duplicated");
        });
    }
}
