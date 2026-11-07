using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace T34400.VRWebView.Editor
{
    // Kept independent of Unity APIs so launcher preservation has executable tests.
    internal static class HostEntryManifest
    {
        internal const string Activity = "com.unity3d.player.UnityPlayerActivity";
        internal const string GameActivity = "com.unity3d.player.UnityPlayerGameActivity";
        private static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";
        private static XDocument Read(string xml)
        {
            using (var input = new StringReader(xml))
            using (var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                return XDocument.Load(reader);
        }
        private static XElement Launcher(XDocument doc)
        {
            var candidates = doc.Root?.Element("application")?.Elements("activity")
                .Where(a => a.Elements("intent-filter").Any(f =>
                    f.Elements("action").Any(x => (string)x.Attribute(Android + "name") == "android.intent.action.MAIN")
                    && f.Elements("category").Any(x => (string)x.Attribute(Android + "name") == "android.intent.category.LAUNCHER"))).ToArray();
            if (candidates == null || candidates.Length != 1) throw new InvalidOperationException("Expected exactly one Unity launcher activity");
            string name = (string)candidates[0].Attribute(Android + "name");
            if (name != Activity && name != GameActivity) throw new InvalidOperationException("Custom launcher is not changed by this experiment");
            return candidates[0];
        }
        internal static string LauncherName(string xml) { return (string)Launcher(Read(xml)).Attribute(Android + "name"); }
        internal static string Rewrite(string xml, bool game)
        {
            var doc = Read(xml); var launcher = Launcher(doc);
            launcher.SetAttributeValue(Android + "name", game ? GameActivity : Activity);
            launcher.SetAttributeValue(Android + "theme", game ? "@style/Theme.AppCompat.DayNight.NoActionBar" : "@style/UnityThemeSelector");
            SetMetadata(launcher, "unityplayer.UnityActivity", "true");
            var library = launcher.Elements("meta-data").Where(x => (string)x.Attribute(Android + "name") == "android.app.lib_name").ToArray();
            if (library.Any(x => (string)x.Attribute(Android + "value") != "game"))
                throw new InvalidOperationException("Custom native activity library is not changed by this experiment");
            foreach (var item in library) item.Remove();
            if (game) SetMetadata(launcher, "android.app.lib_name", "game");
            return doc.ToString() + Environment.NewLine;
        }
        private static void SetMetadata(XElement launcher, string name, string value)
        {
            var items = launcher.Elements("meta-data").Where(x => (string)x.Attribute(Android + "name") == name).ToArray();
            foreach (var item in items) item.Remove();
            launcher.Add(new XElement("meta-data", new XAttribute(Android + "name", name), new XAttribute(Android + "value", value)));
        }
    }
}
