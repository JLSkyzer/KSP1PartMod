using System;
using KSP.Modules;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Settings;
using KSP1PartMod.Animation.Tracks;
using KSP1PartMod.Native;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>KSP1 <c>ModuleAnimationGroup</c> entries and KSP2's native converter, drill and scanner of the same part
    /// (converters and drills plan, Task 4, (k)):
    /// <list type="bullet">
    /// <item><b>deploy bridge</b>: <see cref="IsruRuntime"/> hands over the plain <c>Data_Deployable</c> it gave a native
    /// drill or scanner. It follows the first bound Group entry with a deploy clip (Retracted, Extending, Extended,
    /// Retracting), written only on change; an entry gated off by B9 or disabled reads Extended. With no such entry it is
    /// Extended once;</item>
    /// <item><b>active input</b> (<c>isru_active_animation</c>, flight only): a Group entry with an active clip and no
    /// <c>AlwaysActive</c> plays it while the native module runs (<see cref="NativeActivity"/>).</item>
    /// </list>
    /// The radiator bridge (radiators plan, Task 3) comes from <see cref="RadiatorRuntime"/> with the radiator's handle: it
    /// follows the Deploy entry of the part's <c>ModuleDeployableRadiator</c> (<see cref="RadiatorAnimRules"/>), and its writes go
    /// through <see cref="RadiatorRuntime.Write"/> (auto-on at an extension).
    /// No key, saved value or control changes; both per-frame paths are allocation-free.</summary>
    public partial class Module_Ksp1Animations
    {
        private Data_Deployable _bridge;
        private string _bridgeNative;
        private Entry _bridgeEntry;
        private BridgeDeploy? _bridgeLast;
        private bool _bridgeBound, _initDone;
        private NativeActivity _activity;
        private bool _activitySearched;
        /// <summary>The radiator handle when the bridge is a <c>Module_ActiveRadiator</c>'s (null for a drill or scanner).</summary>
        private RadiatorRuntime.Cooler _radiator;

        /// <summary>From <see cref="IsruRuntime"/> or <see cref="RadiatorRuntime"/> (with <paramref name="radiator"/>), after the
        /// native module's <c>OnInitialize</c>: before or after ours.</summary>
        internal void AttachDeployBridge(Data_Deployable bridge, string nativeModule, RadiatorRuntime.Cooler radiator = null)
        {
            if (bridge == null) return;
            _bridge = bridge;
            _bridgeNative = nativeModule;
            _radiator = radiator;
            _bridgeBound = false;
            _bridgeEntry = null;
            _bridgeLast = null;
            if (_initDone) BindBridge();
        }

        internal void FinishIsruInit()
        {
            _initDone = true;
            try
            {
                BindBridge();
            }
            catch (Exception ex)
            {
                // The native module must not stay retracted for good: extended, as without animations.
                Error($"{SafePartName()} deploy bridge failed, extended: {ex}");
                _bridgeEntry = null;
                WriteBridge(BridgeDeploy.Extended);
            }
        }

        private void WriteBridge(BridgeDeploy s)
        {
            if (_radiator != null) RadiatorRuntime.Write(_radiator, s);
            else IsruRuntime.Write(_bridge, ref _bridgeLast, s);
        }

        private void BindBridge()
        {
            if (_bridge == null || _bridgeBound) return;
            _bridgeBound = true;
            var part = _name ?? SafePartName();
            var mode = PartBackingMode.ToString();
            if (_radiator != null)
            {
                BindRadiatorBridge(part, mode);
                return;
            }
            Entry found = null;
            foreach (var e in _entries)
                if (!e.Disabled && e.Group != null && e.Group.HasDeploy && IsruAnimRules.CanDriveBridge(e.Def))
                {
                    found = e;
                    break;
                }
            var key = "deploy|" + part + "|" + _bridgeNative + "|" + mode;
            if (found == null)
            {
                IsruRuntime.Write(_bridge, ref _bridgeLast, BridgeDeploy.Extended);
                IsruRuntime.LogOnce(key, IsruLines.AlwaysExtended(part, _bridgeNative, ExtendedReason.NoDeployClip));
                return;
            }
            _bridgeEntry = found;
            IsruRuntime.LogOnce(key, Inactive(found)
                ? IsruLines.AlwaysExtended(part, _bridgeNative, ExtendedReason.GatedByB9)
                : IsruLines.DeployFollows(part, _bridgeNative, found.Def.Key));
            StepBridge();
        }

        /// <summary>The radiator's bridge: the Deploy entry of its <c>ModuleDeployableRadiator</c>; none, animations off, or the entry
        /// gated by B9: Extended (it cools, as with no bridge).</summary>
        private void BindRadiatorBridge(string part, string mode)
        {
            var key = RadiatorRuntime.BridgeLogKey(part, mode);
            var settings = KSP1PartModPlugin.Animations;
            if (settings != null && !settings.Enabled)
            {
                WriteBridge(BridgeDeploy.Extended);
                IsruRuntime.LogOnce(key, RadiatorLines.AlwaysExtended(part, ExtendedReason.AnimationsOff));
                return;
            }
            Entry found = null;
            foreach (var e in _entries)
                if (!e.Disabled && e.Deploy != null && RadiatorAnimRules.CanDriveBridge(e.Def, _radiator.DeployIndex))
                {
                    found = e;
                    break;
                }
            if (found == null)
            {
                WriteBridge(BridgeDeploy.Extended);
                IsruRuntime.LogOnce(key, RadiatorLines.AlwaysExtended(part, ExtendedReason.NoDeployClip));
                return;
            }
            _bridgeEntry = found;
            IsruRuntime.LogOnce(key, Inactive(found)
                ? RadiatorLines.AlwaysExtended(part, ExtendedReason.GatedByB9)
                : RadiatorLines.DeployFollows(part, found.Def.Key));
            StepBridge();
        }

        /// <summary>Per frame: the bridge follows its entry (written only on change).</summary>
        private void StepBridge()
        {
            var e = _bridgeEntry;
            if (e == null) return;
            if (_radiator != null)
            {
                if (e.Deploy != null) RadiatorRuntime.Write(_radiator, RadiatorAnimRules.StateOf(e.Deploy.State, e.Disabled || Inactive(e)));
                return;
            }
            IsruRuntime.Write(_bridge, ref _bridgeLast, IsruAnimRules.StateOf(e.Group.Deployed, e.Group.IsMoving, e.Disabled || Inactive(e)));
        }

        /// <summary>At build, flight only: the entry's active clip follows the part's native module.</summary>
        private void BindActiveInput(Entry e, AnimationSettings settings)
        {
            if (PartBackingMode != PartBackingModes.Flight || !IsruAnimRules.WantsActiveInput(e.Def, settings != null && settings.IsruActiveAnimation))
                return;
            if (!_activitySearched)
            {
                _activitySearched = true;
                _activity = NativeActivity.Find(this);
            }
            if (_activity == null) return;
            e.Group.ActiveInput = _activity.Running;
            var line = IsruLines.ActiveClipFollows(_name, e.Def.Key, _activity.NativeModule);
            if (LoggedPluginLine.First(_name + "|" + e.Def.Key + "|" + PartBackingMode + "|active", TrackCache.Generation))
                KSP1PartModPlugin.Log?.LogInfo(line);
        }
    }
}
