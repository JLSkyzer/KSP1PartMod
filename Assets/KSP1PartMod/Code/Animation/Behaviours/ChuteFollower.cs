using System;
using System.Collections.Generic;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary><c>Data_Parachute.DeploymentStates</c> of Redux 26w42c, same byte values.</summary>
    public enum ReduxChuteState : byte { Stowed, Armed, SemiDeployed, Deployed, Cut }

    /// <summary>Draws a KSP1 parachute's two legacy clips from Redux's <c>Data_Parachute.deployState</c>. Redux binds them
    /// to a Mecanim copy (<c>Ksp1ParachuteAnimatorBuilder.CloneAsMecanimClip</c>, same runtime <c>legacy = false</c> as
    /// the deployables), which moves nothing. KSP1 (<c>stock/NOTES.md</c> §9): stowed and armed hold the semi clip at 0;
    /// semi-deployed plays it from 0 to 1 at <c>semiDeploymentSpeed</c>; deployed plays the full clip from 0 to 1 at
    /// <c>deploymentSpeed</c>, the semi clip off; cut leaves both off (Redux hides the canopy). The first state seen
    /// shows the end of its clip (load). Cap and canopy visibility stay Redux's. Nothing is saved.</summary>
    public sealed class ChuteFollower : IEntryMachine
    {
        private const float MinSpeed = 1e-4f;

        private readonly float _semiLength, _fullLength, _semiSpeed, _fullSpeed;
        private ReduxChuteState? _state;
        private float _semiT, _fullT;

        public ChuteFollower(float semiLength, float fullLength, float semiSpeed, float fullSpeed)
        {
            _semiLength = Machine.Length(semiLength);
            _fullLength = Machine.Length(fullLength);
            _semiSpeed = Math.Max(MinSpeed, semiSpeed);
            _fullSpeed = Math.Max(MinSpeed, fullSpeed);
        }

        public ReduxChuteState? State => _state;

        public ClipPose SemiPose
        {
            get
            {
                switch (_state)
                {
                    case ReduxChuteState.Stowed:
                    case ReduxChuteState.Armed:
                    case ReduxChuteState.SemiDeployed: return new ClipPose(_semiT, 0f, 1f, true);
                    default: return ClipPose.Off;
                }
            }
        }

        public ClipPose FullPose => _state == ReduxChuteState.Deployed ? new ClipPose(_fullT, 0f, 1f, true) : ClipPose.Off;

        /// <summary>True when the state changed (the first state included).</summary>
        public bool Tick(float dt, ReduxChuteState state)
        {
            var first = _state == null;
            var changed = state != _state;
            if (changed)
            {
                switch (state)
                {
                    case ReduxChuteState.Stowed:
                    case ReduxChuteState.Armed: _semiT = 0f; break;
                    case ReduxChuteState.SemiDeployed: _semiT = first ? 1f : _state == ReduxChuteState.Deployed ? 1f : 0f; break;
                    case ReduxChuteState.Deployed: _fullT = first ? 1f : 0f; break;
                }
                _state = state;
                if (first) return true;
            }
            if (dt > 0f)
            {
                if (state == ReduxChuteState.SemiDeployed) _semiT = Math.Min(1f, _semiT + _semiSpeed * dt / _semiLength);
                else if (state == ReduxChuteState.Deployed) _fullT = Math.Min(1f, _fullT + _fullSpeed * dt / _fullLength);
            }
            return changed;
        }

        public bool Changed => false;

        public void Load(IReadOnlyDictionary<string, float> saved)
        {
        }

        public void Save(IDictionary<string, float> into)
        {
        }
    }
}
