using System.Collections.Generic;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>The VAB tooltip shows max thrust and Isp only for parts of category <c>Engine</c>
    /// (<c>PartInfoOverlay.PopulatePriorityInfoFromPart</c>, Redux 26w42c), and Redux gives every KSP1 part the synthetic
    /// category of its mod. These are the lines the game builds for an engine, rebuilt the same way: key
    /// <c>string.Join(separator, label, subEntryName)</c>, value the sub-entry value, max thrust first, then Isp; then
    /// the propellant mixture. They go before the resource lines, where the native engine branch puts them.</summary>
    public static class EngineTooltip
    {
        public struct SubEntry
        {
            public string Name, Value;
            public SubEntry(string name, string value)
            {
                Name = name;
                Value = value;
            }
        }

        /// <summary>The engine lines, in the game's order. Null lists count as empty; the propellant line is added only
        /// when its value is not empty.</summary>
        public static List<KeyValuePair<string, string>> Lines(string separator, string thrustLabel, IList<SubEntry> thrust,
            string ispLabel, IList<SubEntry> isp, string propellantLabel, string propellant)
        {
            var lines = new List<KeyValuePair<string, string>>();
            Add(lines, separator, thrustLabel, thrust);
            Add(lines, separator, ispLabel, isp);
            if (!string.IsNullOrEmpty(propellant))
                lines.Add(new KeyValuePair<string, string>(propellantLabel, propellant));
            return lines;
        }

        private static void Add(List<KeyValuePair<string, string>> lines, string separator, string label, IList<SubEntry> entries)
        {
            if (entries == null) return;
            foreach (var e in entries)
                lines.Add(new KeyValuePair<string, string>(string.Join(separator ?? "", label, e.Name), e.Value));
        }

        /// <summary>Puts the engine lines first in <paramref name="info"/>, unless any of its lines already carries the first
        /// engine key (a Redux that shows them itself). Returns the number of lines added.</summary>
        public static int Merge(List<KeyValuePair<string, string>> info, List<KeyValuePair<string, string>> engineLines)
        {
            if (info == null || engineLines == null || engineLines.Count == 0) return 0;
            var first = engineLines[0].Key;
            foreach (var line in info)
                if (line.Key == first) return 0;
            info.InsertRange(0, engineLines);
            return engineLines.Count;
        }
    }
}
