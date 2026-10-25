using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>KRE ModuleDeployableAero (research notes <c>mods/NOTES.md</c> §3.3): the lifting or control surface
    /// works only while its controller's scalar is at <see cref="AnimEntryDef.DeployedPosition"/> (BoosterWing 0,
    /// GridFins 1), within 0.01. <see cref="Open"/> is false until the first tick, so the caller applies the
    /// first result whatever it is. Nothing is saved.</summary>
    public sealed class AeroGate : IEntryMachine
    {
        private const float Tolerance = 0.01f;

        private readonly float _deployedPosition;

        public AeroGate(AnimEntryDef def) => _deployedPosition = def.DeployedPosition;

        public bool Open { get; private set; }

        /// <summary>Always false: nothing is saved.</summary>
        public bool Changed => false;

        public bool Tick(float controllerScalar) =>
            Open = System.Math.Abs(controllerScalar - _deployedPosition) < Tolerance;

        public void Load(IReadOnlyDictionary<string, float> saved) { }

        public void Save(IDictionary<string, float> into) { }
    }
}
