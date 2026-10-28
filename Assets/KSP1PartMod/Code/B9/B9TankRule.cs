using System;
using System.Collections.Generic;
using System.Linq;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.B9
{
    /// <summary>One KSP1 <c>RESOURCE</c> a subtype gives the part.</summary>
    public sealed class B9Resource
    {
        public string Name;
        public double Amount;
        public double MaxAmount;
        public override string ToString() => $"{Name} {B9Values.Inv(Amount)}/{B9Values.Inv(MaxAmount)}";
    }

    /// <summary>The KSP1 resources of a B9 subtype (wiki semantics; the arithmetic is ours): the tank type's RESOURCE
    /// nodes, each merged with the subtype's RESOURCE of the same name, then the subtype's RESOURCE nodes of new names.
    /// <c>maxAmount = unitsPerVolume × volume</c> (a node with <c>maxAmount</c> and no <c>unitsPerVolume</c> keeps it).
    /// Fill: the resource's <c>percentFilled</c>, else the subtype's, else the tank type's, else 100, read as a
    /// <b>percentage</b> (wiki; Redux's own catalog reads a value up to 1 as a fraction, so such values are noted), clamped
    /// to 0..100; an explicit <c>amount</c> wins (capped at <c>maxAmount</c>).</summary>
    public static class B9TankRule
    {
        public static List<B9Resource> Resources(IKsp1Node subtype, B9TankType tank, double volume, ICollection<string> notes = null)
        {
            var result = new List<B9Resource>();
            if (subtype == null) return result;
            var subtypeId = SubtypeId(subtype);
            var own = subtype.GetNodes("RESOURCE").Select(B9ResourceDef.From).Where(r => !string.IsNullOrEmpty(r.Name)).ToList();
            var defs = new List<B9ResourceDef>();
            foreach (var r in tank?.Resources ?? new B9ResourceDef[0])
                defs.Add(r.MergedWith(own.FirstOrDefault(o => SameName(o.Name, r.Name))));
            foreach (var o in own)
                if (!defs.Any(d => SameName(d.Name, o.Name)) ) defs.Add(o);

            var subtypeFill = B9Values.Optional(subtype.GetValue("percentFilled"));
            foreach (var d in defs)
            {
                double max;
                if (d.UnitsPerVolume.HasValue) max = d.UnitsPerVolume.Value * volume;
                else if (d.MaxAmount.HasValue) max = d.MaxAmount.Value;
                else
                {
                    B9Values.Add(notes, B9Lines.Notes.ResourceWithoutSize(subtypeId, d.Name));
                    continue;
                }
                max = Math.Max(0, max);
                var fill = d.PercentFilled ?? subtypeFill ?? tank?.PercentFilled ?? 100;
                if (fill > 0 && fill <= 1) B9Values.Add(notes, B9Lines.Notes.PercentFilledSmall(fill));
                fill = Math.Min(100, Math.Max(0, fill));
                var amount = d.Amount.HasValue ? Math.Min(max, Math.Max(0, d.Amount.Value)) : max * fill / 100;
                result.Add(new B9Resource { Name = d.Name, Amount = amount, MaxAmount = max });
            }
            return result;
        }

        private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

        internal static string SubtypeId(IKsp1Node subtype) =>
            B9Values.FirstNonBlank(subtype?.GetValue("name"), subtype?.GetValue("title"), "?");
    }
}
