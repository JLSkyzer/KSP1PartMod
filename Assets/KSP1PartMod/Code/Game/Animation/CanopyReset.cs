using System;
using System.Collections.Generic;
using System.Reflection;
using KSP.Modules;
using KSP1PartMod.Animation.Behaviours;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>The canopy bone of a followed KSP1 parachute, put back to its parent's rotation each frame before Redux
    /// orients it (<see cref="CanopyRule"/>, <c>chute_canopy_reset</c>). Called from the module's flight update
    /// (<c>OnModuleUpdate</c>, Redux's <c>ViewLegacyUpdateSystem</c> in the InitializationSystemGroup, which
    /// <c>UniverseSimWorld</c> inserts before <c>Update.ScriptRunBehaviourUpdate</c>): after the fixed step that runs
    /// <c>Rotate(spread)</c> (SimulationSystemGroup, before <c>FixedUpdate.ScriptRunBehaviourFixedUpdate</c>), before the
    /// Animator and <c>GameInstance.LateUpdate</c>, which calls <c>Module_Parachute.OnLateUpdate</c>. The bone is Redux's
    /// own (<c>_canopyBoneTransform</c>), else found as Redux finds it. It is reset only on the frames Redux composes: state
    /// semi-deployed or deployed, Redux's Animator active and enabled and not on the bone (then Redux's absolute branch is
    /// already right), bone with a parent. The Animator is left alone. No allocation per frame.</summary>
    internal sealed class CanopyReset
    {
        private static FieldInfo _reduxBone;
        private static bool _reduxBoneLooked;
        private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);

        private readonly Module_Parachute _chute;
        private readonly string _part;
        private Transform _bone;
        private int _mode; // 0 not resolved yet, 1 reset, 2 left to Redux

        private CanopyReset(Module_Parachute chute, string part)
        {
            _chute = chute;
            _part = part;
        }

        /// <summary>Null when the switch is off or the part has no <c>Module_Parachute</c>.</summary>
        public static CanopyReset TryCreate(Component owner, string part)
        {
            if (owner == null || KSP1PartModPlugin.Animations?.ChuteCanopyReset == false) return null;
            var chute = owner.GetComponent<Module_Parachute>();
            return chute == null ? null : new CanopyReset(chute, part);
        }

        public void Tick(ReduxChuteState state)
        {
            if (_mode == 2 || !CanopyRule.Applies(state) || _chute == null) return;
            var animator = _chute.animator;
            if (animator == null || !animator.isActiveAndEnabled) return; // Redux does not compose this frame
            if (_mode == 0 && !Resolve(animator)) return;
            if (_bone == null || _bone.parent == null) return;
            _bone.localRotation = Quaternion.identity;
        }

        private bool Resolve(Animator animator)
        {
            _bone = ReduxBone() ?? FindLikeRedux();
            if (_bone == null)
            {
                _mode = 2;
                LogOnce(CanopyRule.SkipLine(_part, $"no canopy bone '{BoneName() ?? "?"}'"));
                return false;
            }
            if (animator.transform == _bone)
            {
                _mode = 2;
                LogOnce(CanopyRule.SkipLine(_part, $"its Animator is on the canopy bone '{_bone.name}'"));
                return false;
            }
            _mode = 1;
            LogOnce(CanopyRule.ResetLine(_part, _bone.name, animator.gameObject.name));
            return true;
        }

        private Transform ReduxBone()
        {
            if (!_reduxBoneLooked)
            {
                _reduxBoneLooked = true;
                _reduxBone = typeof(Module_Parachute).GetField("_canopyBoneTransform", BindingFlags.Instance | BindingFlags.NonPublic);
                if (_reduxBone != null && _reduxBone.FieldType != typeof(Transform)) _reduxBone = null;
            }
            return _reduxBone?.GetValue(_chute) as Transform;
        }

        private string BoneName() =>
            _chute.DataModules != null && _chute.DataModules.TryGetByType<Data_Parachute>(out var data) && data != null ? data.canopyBoneName : null;

        /// <summary><c>Module_Parachute.FindParachuteTransform</c>: the part's model transform, else a case-insensitive
        /// recursive search from the module's GameObject.</summary>
        private Transform FindLikeRedux()
        {
            var name = BoneName();
            if (string.IsNullOrWhiteSpace(name)) return null;
            Transform found = null;
            try
            {
                found = _chute.part != null ? _chute.part.FindModelTransform(name) : null;
            }
            catch (Exception)
            {
                found = null;
            }
            return found != null ? found : FindChild(_chute.gameObject.transform, name);
        }

        private static Transform FindChild(Transform parent, string name)
        {
            if (parent == null) return null;
            if (parent.name.Equals(name, StringComparison.OrdinalIgnoreCase)) return parent;
            for (var i = 0; i < parent.childCount; i++)
            {
                var t = FindChild(parent.GetChild(i), name);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>One line per part type and outcome for the session (instances are named "type(Clone)" or
        /// "type.prefab_…").</summary>
        private void LogOnce(string line)
        {
            var type = _part ?? "?";
            var cut = type.IndexOfAny(new[] { '(', '.' });
            if (cut > 0) type = type.Substring(0, cut);
            // Called once per instance, at resolve: the outcome is the mode.
            if (Logged.Add(type + "|" + _mode))
                KSP1PartModPlugin.Log?.LogInfo(_part == null || type == _part ? line : line.Replace(_part, type));
        }
    }
}
