using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.B9
{
    /// <summary>The mass and cost a B9 subtype adds (wiki: <c>addedMass</c>/<c>addedCost</c> on top of the tank, the
    /// tank's <c>tankMass</c>/<c>tankCost</c> per unit of volume; the product by the volume is <b>inferred</b>, S). Tonnes,
    /// as KSP1's and Redux's part <c>mass</c>. Cost is computed for the logs only.</summary>
    public static class B9MassRule
    {
        /// <summary><c>addedMass + tankMass × volume</c>. A tank type without <c>tankMass</c> adds 0 (noted, except
        /// <c>Structural</c>, which has 0); no volume (unparseable) adds no dry mass.</summary>
        public static double Delta(IKsp1Node subtype, B9TankType tank, double? volume, ICollection<string> notes = null) =>
            Sum(subtype, "addedMass", tank, tank?.TankMass, volume, notes, true);

        /// <summary><c>addedCost + tankCost × volume</c> (logged, not applied).</summary>
        public static double Cost(IKsp1Node subtype, B9TankType tank, double? volume, ICollection<string> notes = null) =>
            Sum(subtype, "addedCost", tank, tank?.TankCost, volume, notes, false);

        private static double Sum(IKsp1Node subtype, string key, B9TankType tank, double? perVolume, double? volume,
            ICollection<string> notes, bool isMass)
        {
            if (subtype == null) return 0;
            var id = B9TankRule.SubtypeId(subtype);
            var raw = subtype.GetValue(key);
            var added = B9Values.Optional(raw, out var bad);
            if (bad) B9Values.Add(notes, B9Lines.Notes.Unparseable(id, key, raw.Trim()));
            var total = added ?? 0;
            if (tank == null) return total;
            if (perVolume == null)
            {
                if (isMass) B9Values.Add(notes, B9Lines.Notes.TankWithoutMass(tank.Name));
                return total;
            }
            if (volume == null)
            {
                if (perVolume.Value != 0 && isMass) B9Values.Add(notes, B9Lines.Notes.NoVolumeForDryMass(id, tank.Name));
                return total;
            }
            return total + perVolume.Value * volume.Value;
        }
    }
}
