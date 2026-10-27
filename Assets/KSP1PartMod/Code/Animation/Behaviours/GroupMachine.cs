using System.Collections.Generic;
using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Behaviours
{
    /// <summary>ModuleAnimationGroup (research notes <c>stock/NOTES.md</c> §3): a deploy clip (layer 3) and an
    /// active clip (layer 4).
    /// <list type="bullet">
    /// <item>Load snaps to the saved end (KSP1 stops every clip and replays at ±1000).</item>
    /// <item>Deploy plays the deploy clip from its current time at +1; Retract stops the active clip and plays the
    /// deploy clip from its end at −1. In the editor both run at ±<see cref="AnimEntryDef.EditorSpeedMult"/>
    /// (≥ 1000: at once).</item>
    /// <item>In flight only, once the deploy clip has stopped, the active clip plays while deployed with
    /// <see cref="AnimEntryDef.AlwaysActive"/>; it loops (KSP1 restarts it whenever it is not playing). Phase 1
    /// has no converter or harvester activity input, so without AlwaysActive the active clip never plays, and the
    /// deactivate clip is not driven.</item>
    /// </list>
    /// An entry without a deploy clip deploys and retracts at once (<see cref="DeployPose"/> is off). Saved:
    /// <c>.deployed</c>.</summary>
    public sealed class GroupMachine : IEntryMachine
    {
        private const float Snap = 1000f;

        private readonly string _kDeployed;
        private readonly bool _hasDeploy, _hasActive, _alwaysActive;
        private readonly float _deployLength, _activeLength, _editorSpeedMult;
        private float _deployT, _deploySpeed, _activeT;
        private bool _activePlaying;

        /// <param name="deployClipLength">Seconds, from the deploy clip's <c>AnimationState.length</c>.</param>
        /// <param name="activeClipLength">Seconds, from the active clip's <c>AnimationState.length</c>.</param>
        /// <param name="editorSpeed"><see cref="EditorSpeed"/>: positive, it replaces the cfg's editor multiplier.</param>
        public GroupMachine(AnimEntryDef def, float deployClipLength, float activeClipLength, float editorSpeed = EditorSpeed.Ksp1)
        {
            _kDeployed = def.Key + ".deployed";
            _hasDeploy = !string.IsNullOrEmpty(def.Clip);
            _hasActive = !string.IsNullOrEmpty(def.SecondClip);
            _alwaysActive = def.AlwaysActive;
            _editorSpeedMult = EditorSpeed.Multiplier(editorSpeed, def.EditorSpeedMult);
            _deployLength = Machine.Length(deployClipLength);
            _activeLength = Machine.Length(activeClipLength);
        }

        /// <summary>KSP1 <c>isDeployed</c>: true from the start of a deploy, false from the start of a retract.</summary>
        public bool Deployed { get; private set; }

        public bool Changed { get; private set; }

        public bool IsMoving => _deploySpeed != 0f;

        public ClipPose DeployPose => _hasDeploy ? new ClipPose(_deployT, _deploySpeed, 1f, true) : ClipPose.Off;

        public ClipPose ActivePose => _activePlaying ? new ClipPose(_activeT, 1f, 1f, true) : ClipPose.Off;

        /// <summary>Ignored when already deployed.</summary>
        public void Deploy(bool inEditor)
        {
            if (Deployed) return;
            Deployed = true;
            Changed = true;
            Play(1f, inEditor);
        }

        /// <summary>Ignored when not deployed.</summary>
        public void Retract(bool inEditor)
        {
            if (!Deployed) return;
            Deployed = false;
            Changed = true;
            _activePlaying = false;
            _deployT = 1f;
            Play(-1f, inEditor);
        }

        public void Tick(float dt, bool inEditor)
        {
            if (!(dt > 0f)) return;
            if (IsMoving)
            {
                // KSP1 waits for the deploy clip to stop before it looks at activity again (the next frame).
                _deployT += _deploySpeed * dt / _deployLength;
                if (_deployT >= 1f || _deployT <= 0f) Settle();
                return;
            }
            if (inEditor || !Deployed || !_hasActive || !_alwaysActive) return;
            if (!_activePlaying)
            {
                _activePlaying = true;
                _activeT = 0f;
                return;
            }
            _activeT += dt / _activeLength;
            if (_activeT >= 1f) _activeT -= (float)System.Math.Floor(_activeT);
        }

        public void Load(IReadOnlyDictionary<string, float> saved)
        {
            if (!Machine.TryRead(saved, _kDeployed, out var d)) return;
            Deployed = Machine.Bool(d);
            _activePlaying = false;
            Settle();
            Changed = false;
        }

        public void Save(IDictionary<string, float> into)
        {
            into[_kDeployed] = Machine.Of(Deployed);
            Changed = false;
        }

        private void Play(float direction, bool inEditor)
        {
            if (!_hasDeploy || inEditor && _editorSpeedMult >= Snap)
            {
                Settle();
                return;
            }
            _deploySpeed = inEditor ? direction * _editorSpeedMult : direction;
        }

        private void Settle()
        {
            _deployT = Deployed ? 1f : 0f;
            _deploySpeed = 0f;
        }
    }
}
