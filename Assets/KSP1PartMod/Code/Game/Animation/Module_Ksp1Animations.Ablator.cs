using System;
using System.Collections.Generic;
using System.Globalization;
using KSP.Game;
using KSP.Sim.ResourceSystem;
using KSP1PartMod.Animation.Behaviours;
using Redux.Ksp1Import;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>The ablator char (0.8.0, <c>ablator_char</c>): a <c>shieldChar</c> colour changer whose scalar is the part's
    /// own fraction of the shield resource, read every frame from the part's container (flight: the part's simulation
    /// container; VAB: the assembly part's), never KSP2's vessel-wide <c>AblatorRatio</c>. Read only, visual only.</summary>
    public partial class Module_Ksp1Animations
    {
        /// <summary>(part, mode) pairs whose char line was logged: once per part name per mode and session.</summary>
        private static readonly HashSet<string> LoggedAblator = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Resolves the resource of <paramref name="e"/> and reads the part's current fraction, before any renderer is
        /// taken. False (with <paramref name="note"/>) when the resource is unknown or the part has none of it: the entry is not
        /// bound and the colour stays as Redux leaves it, as in 0.7.0.</summary>
        private bool PrepareAblator(Entry e, out string note)
        {
            note = null;
            var def = e.Def;
            var ksp1Name = string.IsNullOrWhiteSpace(def.ScalarResource) ? "Ablator" : def.ScalarResource.Trim();
            var name = Ksp1ResourceMapper.MapResourceName(ksp1Name) ?? ksp1Name;
            var db = GameManager.Instance?.Game?.ResourceDefinitionDatabase;
            if (db == null)
            {
                note = "ablator char: no resource database";
                return false;
            }
            var id = db.GetResourceIDFromName(name);
            if (id == ResourceDefinitionID.InvalidID)
            {
                note = $"ablator char: no resource '{name}' in the game";
                return false;
            }
            var scalar = new AblatorScalar();
            var container = AblatorContainer();
            if (container != null)
            {
                var capacity = container.GetResourceCapacityUnits(id);
                if (!(capacity > 0))
                {
                    note = $"ablator char: the part has no {name}";
                    return false;
                }
                scalar.Tick(container.GetResourceStoredUnits(id), capacity);
            }
            e.AblatorId = id;
            e.Ablator = scalar;
            return true;
        }

        /// <summary>Once the renderers are taken: shows the fraction read by <see cref="PrepareAblator"/> at once (the ramp
        /// starts at 0, black), and logs the char line once per part name per mode.</summary>
        private void StartAblator(Entry e)
        {
            e.Color.SetScalar(e.Ablator.Fraction);
            if (!LoggedAblator.Add(_name + "|" + PartBackingMode)) return;
            var ksp1Name = string.IsNullOrWhiteSpace(e.Def.ScalarResource) ? "Ablator" : e.Def.ScalarResource.Trim();
            Log($"{_name} char '{e.Def.ModuleId}' follows the ablator ({Ksp1ResourceMapper.MapResourceName(ksp1Name) ?? ksp1Name}), now " +
                e.Ablator.Fraction.ToString("0.###", CultureInfo.InvariantCulture) +
                (AblatorContainer() == null ? " (no container yet, read each frame)" : ""));
        }

        /// <summary>The part's own resource container: the simulation part's in flight, the assembly part's in the VAB; null
        /// while there is none. Allocation-free.</summary>
        private IResourceContainerReadOnly AblatorContainer()
        {
            if (PartBackingMode == PartBackingModes.Flight)
            {
                var model = part != null ? part.Model : null;
                return model?.Container;
            }
            return OABPart?.Container;
        }
    }
}
