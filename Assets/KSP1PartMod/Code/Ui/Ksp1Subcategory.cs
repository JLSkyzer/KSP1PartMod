using System;
using System.Collections.Generic;

namespace KSP1PartMod.Ui
{
    /// <summary>The part types of the KSP1 editor, in its order. <see cref="Other"/> holds <c>none</c>, an absent
    /// category and any category a mod made up.</summary>
    public enum Ksp1Subcategory
    {
        Pods, FuelTank, Engine, Control, Structural, Coupling, Payload, Aero, Ground, Thermal, Electrical,
        Communication, Science, Utility, Robotics, Cargo, Other,
    }

    /// <summary>KSP1 <c>category</c> values to <see cref="Ksp1Subcategory"/>, plus the icon and label rules of the
    /// sub-category bar. Pure: no game type.</summary>
    public static class Ksp1Subcategories
    {
        private static readonly Ksp1Subcategory[] Order = (Ksp1Subcategory[])Enum.GetValues(typeof(Ksp1Subcategory));

        /// <summary>Every type, in the KSP1 editor order (the enum order).</summary>
        public static IReadOnlyList<Ksp1Subcategory> Ordered => Order;

        // Keys are lower case, without spaces or underscores. KSP1 1.12 writes "Propulsion" for engines; some mods
        // write "Engine", "Fuel Tank" or the plural forms.
        private static readonly Dictionary<string, Ksp1Subcategory> Aliases = new Dictionary<string, Ksp1Subcategory>(StringComparer.Ordinal)
        {
            ["pods"] = Ksp1Subcategory.Pods, ["pod"] = Ksp1Subcategory.Pods,
            ["fueltank"] = Ksp1Subcategory.FuelTank, ["fueltanks"] = Ksp1Subcategory.FuelTank,
            ["propulsion"] = Ksp1Subcategory.Engine, ["engine"] = Ksp1Subcategory.Engine, ["engines"] = Ksp1Subcategory.Engine,
            ["control"] = Ksp1Subcategory.Control,
            ["structural"] = Ksp1Subcategory.Structural, ["structures"] = Ksp1Subcategory.Structural,
            ["coupling"] = Ksp1Subcategory.Coupling,
            ["payload"] = Ksp1Subcategory.Payload, ["payloads"] = Ksp1Subcategory.Payload,
            ["aero"] = Ksp1Subcategory.Aero, ["aerodynamics"] = Ksp1Subcategory.Aero,
            ["ground"] = Ksp1Subcategory.Ground,
            ["thermal"] = Ksp1Subcategory.Thermal,
            ["electrical"] = Ksp1Subcategory.Electrical,
            ["communication"] = Ksp1Subcategory.Communication,
            ["science"] = Ksp1Subcategory.Science,
            ["utility"] = Ksp1Subcategory.Utility,
            ["robotics"] = Ksp1Subcategory.Robotics,
            ["cargo"] = Ksp1Subcategory.Cargo,
        };

        /// <summary>A raw KSP1 <c>category</c> (trimmed, <c>//</c> comment cut, case, spaces and underscores ignored).
        /// Anything unknown, <c>none</c> included, is <see cref="Ksp1Subcategory.Other"/>.</summary>
        public static Ksp1Subcategory Normalize(string raw)
        {
            var key = Key(raw);
            return key != null && Aliases.TryGetValue(key, out var sub) ? sub : Ksp1Subcategory.Other;
        }

        /// <summary>Redux's tags end with "&lt;mod&gt; &lt;mapped category&gt; KSP1": the token before the trailing
        /// <c>KSP1</c>. Lossy (Redux turns Robotics and Cargo into Utility), so a fallback only.</summary>
        public static Ksp1Subcategory FromTags(string tags)
        {
            if (string.IsNullOrWhiteSpace(tags)) return Ksp1Subcategory.Other;
            var tokens = tags.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2 || !string.Equals(tokens[tokens.Length - 1], "KSP1", StringComparison.OrdinalIgnoreCase))
                return Ksp1Subcategory.Other;
            return Normalize(tokens[tokens.Length - 2]);
        }

        /// <summary>The raw category when there is one, else the tags.</summary>
        public static Ksp1Subcategory Resolve(string rawCategory, string tags) =>
            Key(rawCategory) != null ? Normalize(rawCategory) : FromTags(tags);

        /// <summary>Names of the game's <c>PartCategories</c> whose button sprite stands for this type, best first.
        /// Empty for Robotics and Other, which take the icon of their first part.</summary>
        public static IReadOnlyList<string> StockIconCategories(Ksp1Subcategory sub)
        {
            switch (sub)
            {
                case Ksp1Subcategory.Robotics:
                case Ksp1Subcategory.Other: return Array.Empty<string>();
                case Ksp1Subcategory.Cargo: return new[] { "Storage", "Payload" };
                default: return new[] { sub.ToString() };
            }
        }

        /// <summary>True when the first part's icon comes before any stock sprite.</summary>
        public static bool PrefersPartIcon(Ksp1Subcategory sub) =>
            sub == Ksp1Subcategory.Robotics || sub == Ksp1Subcategory.Other;

        /// <summary>The stock picker's localization key of the same category (<c>VAB/PartsPicker/&lt;key&gt;</c>, keys as
        /// the game's <c>FilterUtils.PartCategoryToLocKey</c> gives them), or null when the game has none.</summary>
        public static string LocKey(Ksp1Subcategory sub)
        {
            string key;
            switch (sub)
            {
                case Ksp1Subcategory.Pods: key = "Pods"; break;
                case Ksp1Subcategory.FuelTank: key = "Fuel Tanks"; break;
                case Ksp1Subcategory.Engine: key = "Engines"; break;
                case Ksp1Subcategory.Control: key = "Control"; break;
                case Ksp1Subcategory.Structural: key = "Structures"; break;
                case Ksp1Subcategory.Coupling: key = "Coupling"; break;
                case Ksp1Subcategory.Payload: key = "Payloads"; break;
                case Ksp1Subcategory.Aero: key = "Aerodynamics"; break;
                case Ksp1Subcategory.Ground: key = "Ground"; break;
                case Ksp1Subcategory.Thermal: key = "Thermal"; break;
                case Ksp1Subcategory.Electrical: key = "Electrical"; break;
                case Ksp1Subcategory.Communication: key = "Communication"; break;
                case Ksp1Subcategory.Science: key = "Science"; break;
                case Ksp1Subcategory.Utility: key = "Utility"; break;
                default: return null;
            }
            return "VAB/PartsPicker/" + key;
        }

        /// <summary>The game's translation of <see cref="LocKey"/> when it has one, else the KSP1 name (French for the
        /// types the game lacks when the language code starts with "fr").</summary>
        /// <param name="translate">Key to text, or null/blank when missing.</param>
        public static string Label(Ksp1Subcategory sub, string languageCode, Func<string, string> translate)
        {
            var key = LocKey(sub);
            if (key != null && translate != null)
            {
                var text = translate(key);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            var fr = IsFrench(languageCode);
            switch (sub)
            {
                case Ksp1Subcategory.FuelTank: return "Fuel Tanks";
                case Ksp1Subcategory.Engine: return "Engines";
                case Ksp1Subcategory.Aero: return "Aerodynamics";
                case Ksp1Subcategory.Robotics: return fr ? "Robotique" : "Robotics";
                case Ksp1Subcategory.Cargo: return "Cargo";
                case Ksp1Subcategory.Other: return fr ? "Autres" : "Other";
                default: return sub.ToString();
            }
        }

        public static string AllLabel(string languageCode) => IsFrench(languageCode) ? "Tout" : "All";

        private static bool IsFrench(string languageCode) =>
            languageCode != null && languageCode.StartsWith("fr", StringComparison.OrdinalIgnoreCase);

        private static string Key(string raw)
        {
            if (raw == null) return null;
            var comment = raw.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) raw = raw.Substring(0, comment);
            raw = raw.Replace(" ", "").Replace("_", "").Replace("\t", "").ToLowerInvariant();
            return raw.Length == 0 ? null : raw;
        }
    }
}
