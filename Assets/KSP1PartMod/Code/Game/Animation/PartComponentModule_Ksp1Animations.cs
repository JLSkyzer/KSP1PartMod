using System;
using KSP.Sim.impl;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>The simulation side of <see cref="Module_Ksp1Animations"/>; it only carries the data.</summary>
    public class PartComponentModule_Ksp1Animations : PartComponentModule
    {
        public override Type PartBehaviourModuleType => typeof(Module_Ksp1Animations);
    }
}
