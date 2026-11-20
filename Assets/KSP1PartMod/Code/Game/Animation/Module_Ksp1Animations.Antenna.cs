using System;
using HarmonyLib;
using KSP.Modules;
using KSP.Sim.impl;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Tracks;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Antenna gating (0.18.0): a deployable KSP1 antenna transmits only while its deploy clip is extended. KSP2's
    /// <c>PartComponentModule_DataTransmitter</c> already gates on a <c>PartComponentModule_Deployable</c> of the part
    /// (<c>_requiresDeployment</c> and <c>_dataDeployable</c>, read in <c>OnStart</c>); an imported KSP1 antenna has none. In
    /// flight, once the sim module exists, this module gives it a plain <c>Data_Deployable</c> that no part, PAM or action group
    /// knows, and writes the Deploy entry's state to it on change (<see cref="AntennaRules"/>). The transmitter's own
    /// <c>OnUpdate</c> then notices the change and (de)activates the comm node. Nothing else of the transmitter is touched.
    /// A member that is missing turns the gating off for the session (one line).</summary>
    public partial class Module_Ksp1Animations
    {
        private static AccessTools.FieldRef<PartComponentModule_DataTransmitter, bool> _txRequires;
        private static AccessTools.FieldRef<PartComponentModule_DataTransmitter, Data_Deployable> _txDeployable;
        private static AccessTools.FieldRef<PartComponentModule_DataTransmitter, Data_Deployable.DeployState> _txPrev;
        private static bool _txResolved, _txBroken;

        private Entry _antennaEntry;
        private Data_Deployable _antennaBridge;
        private BridgeDeploy? _antennaLast;
        private bool _antennaBound;
        private int _antennaTries;

        /// <summary>Resolves the three private members once; false when one is missing.</summary>
        private static bool ResolveTransmitterMembers()
        {
            if (_txResolved) return !_txBroken;
            _txResolved = true;
            try
            {
                _txRequires = AccessTools.FieldRefAccess<PartComponentModule_DataTransmitter, bool>("_requiresDeployment");
                _txDeployable = AccessTools.FieldRefAccess<PartComponentModule_DataTransmitter, Data_Deployable>("_dataDeployable");
                _txPrev = AccessTools.FieldRefAccess<PartComponentModule_DataTransmitter, Data_Deployable.DeployState>("_prevDeployState");
            }
            catch (Exception ex)
            {
                _txBroken = true;
                KSP1PartModPlugin.Log?.LogWarning("[KSP1PartMod] anim: antenna: a PartComponentModule_DataTransmitter member was not found, antenna gating off (" + ex.Message + ")");
            }
            return !_txBroken;
        }

        /// <summary>Per frame, flight only: binds the bridge once the transmitter's sim module exists (a few frames at most), then
        /// writes the entry's state on change.</summary>
        private void StepAntenna()
        {
            if (PartBackingMode != PartBackingModes.Flight || _txBroken) return;
            if (!_antennaBound)
            {
                if (_antennaTries > 120) return;
                _antennaTries++;
                BindAntenna();
                if (!_antennaBound) return;
            }
            var e = _antennaEntry;
            if (e?.Deploy == null || _antennaBridge == null) return;
            IsruRuntime.Write(_antennaBridge, ref _antennaLast, ToBridge(e));
        }

        private static BridgeDeploy ToBridge(Entry e) => AntennaRules.StateOf(e.Deploy.State, e.Disabled || Inactive(e));

        private void BindAntenna()
        {
            var settings = KSP1PartModPlugin.Animations;
            if (settings == null || !settings.Enabled || !settings.Ksp1AntennaGating)
            {
                _antennaBound = true;
                return;
            }
            Entry found = null;
            foreach (var e in _entries)
                if (!e.Disabled && e.Deploy != null && AntennaRules.CanDriveBridge(e.Def))
                {
                    found = e;
                    break;
                }
            if (found == null)
            {
                _antennaBound = true; // no gating entry: the transmitter stays as Redux made it
                return;
            }
            var model = part?.Model;
            if (model == null || !model.TryGetModule<PartComponentModule_DataTransmitter>(out var tx) || tx == null) return; // not there yet: retry
            if (!ResolveTransmitterMembers())
            {
                _antennaBound = true;
                return;
            }
            if (_txRequires(tx) && _txDeployable(tx) != null)
            {
                // The part already has a native deployable: KSP2 gates it by itself.
                _antennaBound = true;
                KSP1PartModPlugin.Log?.LogInfo($"[KSP1PartMod] anim: {_name} antenna: the transmitter is already gated by a native deployable, left as is");
                return;
            }
            var bridge = new Data_Deployable();
            var state = ToBridge(found);
            bridge.CurrentDeployState.SetValue(ToDeployState(state));
            // The transmitter's OnStart took the node as active (no deployable then): a retracted start must read as a change
            // from Extended, so that its OnUpdate runs the deactivation (OnTransmitterDeactivated).
            _txPrev(tx) = Data_Deployable.DeployState.Extended;
            _txDeployable(tx) = bridge;
            _txRequires(tx) = true;
            _antennaBridge = bridge;
            _antennaEntry = found;
            _antennaLast = state;
            _antennaBound = true;
            var key = "antenna|" + _name;
            if (LoggedPluginLine.First(key, TrackCache.Generation))
                KSP1PartModPlugin.Log?.LogInfo($"[KSP1PartMod] anim: {_name} antenna: transmitter gated by {found.Def.Key}, starts {state}");
        }

        private static Data_Deployable.DeployState ToDeployState(BridgeDeploy s)
        {
            switch (s)
            {
                case BridgeDeploy.Extended: return Data_Deployable.DeployState.Extended;
                case BridgeDeploy.Extending: return Data_Deployable.DeployState.Extending;
                case BridgeDeploy.Retracting: return Data_Deployable.DeployState.Retracting;
                default: return Data_Deployable.DeployState.Retracted;
            }
        }
    }
}
