using System;
using System.Collections.Generic;
using KSP.Sim;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>The definition and saved state of <see cref="Module_Ksp1Animations"/>. The definition reaches part
    /// instances as JSON (<c>PartJson</c>, the OAB's per-part copy, the simulation's merge), so every member is
    /// plain Newtonsoft data. <c>DataJsonTests</c> show in EditMode that NaN curve tangents come back as NaN
    /// through <c>IOProvider</c>'s settings, through its ApplyConfig path onto an existing object, and through
    /// plain Newtonsoft settings.</summary>
    [Serializable]
    public class Data_Ksp1Animations : ModuleData
    {
        public override Type ModuleType => typeof(Module_Ksp1Animations);

        /// <summary>Our entries, from <c>OwnershipPlanner</c>.</summary>
        [KSPDefinition] public List<AnimEntryDef> Entries = new List<AnimEntryDef>();

        /// <summary>The clip names Redux's deployable runtime keeps.</summary>
        [KSPDefinition] public List<string> ReduxClips = new List<string>();

        /// <summary>Every entry's saved values (<c>EntryStateCodec</c>).</summary>
        [KSPState] public ModuleProperty<string> State = new ModuleProperty<string>("");
    }
}
