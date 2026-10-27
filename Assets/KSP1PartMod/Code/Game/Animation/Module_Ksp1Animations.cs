using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using KSP.Game;
using KSP.Modules;
using KSP.Sim;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Behaviours;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Animation.Runtime;
using KSP1PartMod.Animation.State;
using Redux.Ksp1Import.Modules;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Plays the KSP1 animations of an imported part that Redux leaves alone. At init it binds each entry
    /// to the instance's legacy <c>Animation</c> clips (or renderers), restores the saved state and restricts Redux's
    /// deployable runtime to Redux's own clips. Each frame it ticks the entries' state machines and writes their
    /// poses (enabled, weight 1 or the machine's, layer, speed 0, normalized time). PAM buttons and action-group
    /// actions toggle the entries; the PAM button also toggles the symmetry counterparts. An entry that throws is
    /// disabled for the session with one log line; nothing here may block or break part init.</summary>
    [DisallowMultipleComponent]
    public class Module_Ksp1Animations : PartBehaviourModule
    {
        /// <summary>Changes the machines make by themselves are saved at most this often (seconds).</summary>
        private const float EncodeInterval = 1f;
        /// <summary>KSP1 ModuleAnimationGroup plays its active clip on layer 4.</summary>
        private const int ActiveLayer = 4;
        private const string ContextKeyPrefix = "ksp1anim_";

        private static readonly AnimationState[] NoStates = new AnimationState[0];
        private static readonly MethodInfo ActionContextKeySetter =
            typeof(ModuleActionGroupAction).GetProperty("ContextKey", BindingFlags.Instance | BindingFlags.Public)?.GetSetMethod(true);
        private static bool _loggedContextKeyFallback;

        /// <summary>Assigned by Redux's <c>PartModuleLoadPatcher.ApplyOnGameObject</c>, which only fills
        /// <c>[SerializeField]</c> fields of a <c>ModuleData</c> type. In flight <see cref="AddDataModules"/> then
        /// swaps it for the simulation's own copy (the one that carries the saved state).</summary>
        [SerializeField] private Data_Ksp1Animations data;

        private readonly List<Entry> _entries = new List<Entry>();
        /// <summary>Every saved value: the decoded state, overwritten by the machines. Values of entries not bound
        /// this session are kept as they were.</summary>
        private readonly Dictionary<string, float> _values = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly EncodeGate _gate = new EncodeGate(EncodeInterval);
        private bool _ready;
        private bool _urgent;
        /// <summary><see cref="AddDataModules"/> found no data and runs on an empty one it made.</summary>
        private bool _synthesised;
        private string _name;

        public override Type PartComponentModuleType => typeof(PartComponentModule_Ksp1Animations);

        /// <summary>Runs before <see cref="OnInitialize"/>, during part instantiation: never throws. With no data
        /// (for example our <c>$type</c> not resolved when the definition was read), an empty data object takes its
        /// place, so the part still loads and the module does nothing.</summary>
        protected override void AddDataModules()
        {
            try
            {
                base.AddDataModules();
                Data_Ksp1Animations made = null;
                if (data == null)
                {
                    KSP1PartModPlugin.Log?.LogWarning($"[KSP1PartMod] anim: {PartName()} module has no data (type not resolved?)");
                    data = made = new Data_Ksp1Animations();
                }
                DataModules.TryAddUnique(data, out data);
                // In flight the simulation's own copy may replace the empty one: that copy is real data.
                _synthesised = made != null && ReferenceEquals(data, made);
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log?.LogError($"[KSP1PartMod] anim: {SafePartName()} module data setup failed: {e}");
            }
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();
            try
            {
                if (data == null) return; // AddDataModules failed and said so
                data.Entries ??= new List<AnimEntryDef>();
                data.SetVisible(data.State, false);
                _name = PartName();
                var n = data.Entries.Count;
                Log($"{_name} module present ({PartBackingMode}) with {n} {(n == 1 ? "entry" : "entries")}");
                Setup();
            }
            catch (Exception e)
            {
                KSP1PartModPlugin.Log?.LogError($"[KSP1PartMod] anim: {SafePartName()} module init failed: {e}");
            }
        }

        protected override void OnModuleUpdate(float deltaTime) => Drive(deltaTime, false);

        protected override void OnModuleOABUpdate(float deltaTime) => Drive(deltaTime, true);

        /// <summary>Flushes a pending state encode, then nothing is driven any more.</summary>
        protected override void OnShutdown()
        {
            if (_ready)
            {
                _urgent = true;
                SaveIfChanged();
            }
            _ready = false;
            _entries.Clear();
        }

        // ---------------------------------------------------------------- setup

        private void Setup()
        {
            var inEditor = PartBackingMode == PartBackingModes.OAB;
            var saved = EntryStateCodec.Decode(data.State.GetValue(), out var error);
            if (error != null) Warn($"{_name} {error}; defaults kept for those values");
            foreach (var kv in saved) _values[kv.Key] = kv.Value;

            var failedClips = new List<string>();
            foreach (var def in data.Entries)
            {
                if (def == null) continue;
                try
                {
                    var e = Build(def, saved, out var note);
                    if (e == null)
                    {
                        Log($"{_name} {def.Kind} '{Subject(def)}' not bound: {note}");
                        AddClips(failedClips, def);
                    }
                    else
                    {
                        if (note != null) Log($"{_name} {def.Kind} '{Subject(def)}' bound: {note}");
                        _entries.Add(e);
                    }
                }
                catch (Exception ex)
                {
                    Error($"{_name} {def.Kind} '{Subject(def)}' disabled for this session: {ex}");
                    AddClips(failedClips, def);
                }
            }

            // After Build: the clips of the entries that failed go back to Redux.
            try
            {
                RestrictReduxRuntime(failedClips);
            }
            catch (Exception ex)
            {
                Error($"{_name} restricting Redux's runtime failed: {ex}");
            }

            ResolveAeroGates();
            AddControls(inEditor);

            Log($"{_name} data #{RuntimeHelpers.GetHashCode(data)} entries {data.Entries.Count} bound {_entries.Count} " +
                $"({PartBackingMode}; definition data {DefinitionNote()})");

            // Aero gates are applied on the first update: Module_ControlSurface's own init may still overwrite them.
            foreach (var e in _entries)
                if (!e.Disabled && e.Aero == null) Step(e, 0f, inEditor, catchErrors: true);
            _ready = true;
        }

        private static void AddClips(List<string> clips, AnimEntryDef def)
        {
            if (!string.IsNullOrWhiteSpace(def.Clip)) clips.Add(def.Clip);
            if (!string.IsNullOrWhiteSpace(def.SecondClip)) clips.Add(def.SecondClip);
        }

        /// <summary>Redux's fallback runtime plays every deployable source clip together; it keeps only Redux's own
        /// (<c>ReduxClips</c>) and those of our entries that failed (<see cref="ReduxRuntimeRule.KeptClips"/>). With
        /// <c>own_first_deployable</c> Redux may keep none: the runtime is then also disabled. Missing data
        /// (synthesised, no entry, no clip list) leaves it untouched. Its layer and start state come from Redux's
        /// first source, possibly a solar panel: logged only.</summary>
        private void RestrictReduxRuntime(IReadOnlyList<string> failedClips)
        {
            var runtime = GetComponentInChildren<Ksp1DeployableAnimationRuntime>(true);
            if (runtime == null) return;
            var skip = ReduxRuntimeRule.SkipReason(_synthesised, data.Entries?.Count ?? 0, data.ReduxClips != null);
            if (skip != null)
            {
                Log($"{_name} Redux runtime left untouched ({skip})");
                return;
            }
            var names = ReduxRuntimeRule.KeptClips(data.ReduxClips, failedClips, runtime.AnimationNames);
            var before = runtime.AnimationNames == null ? "" : string.Join(", ", runtime.AnimationNames);
            runtime.AnimationNames = names;
            if (names.Length == 0)
            {
                runtime.enabled = false;
                Log($"{_name} Redux runtime disabled, it keeps no clip (had [{before}], layer {runtime.Layer})");
            }
            else
                Log($"{_name} restricted Redux runtime to [{string.Join(", ", names)}] (had [{before}], layer {runtime.Layer}, start extended {runtime.StartExtended})");
        }

        private Entry Build(AnimEntryDef def, IReadOnlyDictionary<string, float> saved, out string note)
        {
            note = null;
            var e = new Entry(def);
            var editorSpeed = KSP1PartModPlugin.Animations.EditorAnimationSpeed;
            switch (def.Kind)
            {
                case EntryKind.Toggle:
                    if ((e.States = Bind(def, def.Clip, out note)).Length == 0) return null;
                    e.Machine = e.Toggle = new ToggleMachine(def, e.States[0].length, editorSpeed);
                    break;
                case EntryKind.Deploy:
                case EntryKind.Gear:
                    if ((e.States = Bind(def, def.Clip, out note)).Length == 0) return null;
                    e.Machine = e.Deploy = new DeployMachine(def, e.States[0].length, editorSpeed);
                    break;
                case EntryKind.Throttle:
                {
                    if ((e.States = Bind(def, def.Clip, out note)).Length == 0) return null;
                    e.Machine = e.Throttle = new ThrottleFollower(def);
                    GameInputs.TryEngine(this, def.EngineName, out e.Engine, out var field);
                    Log($"{_name} throttle from {field} ('{def.Clip}', {e.States.Length} state(s))");
                    break;
                }
                case EntryKind.Heat:
                {
                    if ((e.States = Bind(def, def.Clip, out note)).Length == 0) return null;
                    e.Machine = e.Heat = new HeatScalar(def);
                    GameInputs.TryTemperature(this, out e.Temperature, out var field);
                    Log($"{_name} heat from {field} ('{def.Clip}', {e.States.Length} state(s))");
                    break;
                }
                case EntryKind.Color:
                    e.Writer = new MaterialWriter(gameObject, def, KSP1PartModPlugin.Animations);
                    note = e.Writer.Note;
                    if (e.Writer.RendererCount == 0) return null;
                    Log($"{_name} color '{def.ShaderProperty}' {e.Writer.Note}");
                    note = null;
                    e.Machine = e.Color = new ColorRamp(def);
                    break;
                case EntryKind.Group:
                {
                    string activeNote = null;
                    e.States = string.IsNullOrWhiteSpace(def.Clip) ? NoStates : Bind(def, def.Clip, out note);
                    e.ActiveStates = string.IsNullOrWhiteSpace(def.SecondClip) ? NoStates : Bind(def, def.SecondClip, out activeNote);
                    if (activeNote != null) note = note == null ? "active: " + activeNote : note + "; active: " + activeNote;
                    if (e.States.Length == 0 && e.ActiveStates.Length == 0) return null;
                    e.Machine = e.Group = new GroupMachine(def,
                        e.States.Length > 0 ? e.States[0].length : 0f, e.ActiveStates.Length > 0 ? e.ActiveStates[0].length : 0f, editorSpeed);
                    break;
                }
                case EntryKind.AeroGate:
                    e.Machine = e.Aero = new AeroGate(def);
                    break; // its controller is resolved once every entry is built
                case EntryKind.GearBridge:
                    if (!GameInputs.TryReduxDeployable(this, out _, out e.ReduxSet, out e.ReduxState, out var bridge))
                    {
                        note = "gear bridge: no Redux deployable setter (" + bridge + ")";
                        return null;
                    }
                    if (!GearBridgeRule.NeedsOwnAction(GameInputs.ReduxDefaultActionGroup(this)))
                    {
                        note = "gear bridge: Redux already on Gear";
                        return null;
                    }
                    Log($"{_name} gear bridge drives {bridge}");
                    break;
                case EntryKind.ReduxFollower:
                {
                    if (!ReduxFollowerBinder.TryDeployable(this, def, out var state, out e.ReadDeploy, out var module, out note)) return null;
                    e.States = new[] { state };
                    e.Machine = e.Follower = new ReduxFollower(def, state.length, editorSpeed);
                    Log($"{_name} follows Redux deployable '{def.Clip}' ({module})");
                    break;
                }
                case EntryKind.ChuteFollower:
                {
                    if (!ReduxFollowerBinder.TryChute(this, def, out var semi, out var full, out e.ReadChute, out note)) return null;
                    e.States = new[] { semi };
                    e.ActiveStates = new[] { full };
                    e.Machine = e.Chute = new ChuteFollower(semi.length, full.length, def.SpeedFactorIn, def.SpeedFactorOut);
                    Log($"{_name} follows Redux parachute '{def.Clip}'/'{def.SecondClip}' (Module_Parachute)");
                    break;
                }
                default:
                    note = "unknown kind";
                    return null;
            }
            if (def.WrapMode != 0)
            {
                foreach (var s in e.States) s.wrapMode = (WrapMode)def.WrapMode;
                foreach (var s in e.ActiveStates) s.wrapMode = (WrapMode)def.WrapMode;
            }
            e.Machine?.Load(saved);
            return e;
        }

        private AnimationState[] Bind(AnimEntryDef def, string clip, out string note) =>
            AnimationBinder.Bind(gameObject, def, clip, data.ReduxClips, out note).Where(s => s != null).ToArray();

        /// <summary>The controller of a KRE aero gate is the KSP1 module at its <c>DeployModuleIndex</c>: one of our
        /// Toggle / Deploy / Gear entries, else Redux's deployable module. Once one is found, Redux's gating marker
        /// is removed from the instance and the gate is ours.</summary>
        private void ResolveAeroGates()
        {
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.Aero == null) continue;
                try
                {
                    if (!ResolveAeroGate(e)) _entries.RemoveAt(i);
                }
                catch (Exception ex)
                {
                    Disable(e, ex);
                }
            }
        }

        /// <summary>False when the gate has no controller (logged).</summary>
        private bool ResolveAeroGate(Entry e)
        {
            {
                var index = e.Def.ControllerModuleIndex;
                var ours = _entries.FirstOrDefault(o => o.Def.ModuleIndex == index && (o.Toggle != null || o.Deploy != null || o.Follower != null));
                if (ours != null)
                {
                    var toggle = ours.Toggle;
                    var deploy = ours.Deploy;
                    var follower = ours.Follower;
                    e.Controller = () => toggle != null ? toggle.Pose.NormalizedTime
                        : deploy != null ? deploy.Pose.NormalizedTime : follower.Pose.NormalizedTime;
                    e.ControllerName = "our " + ours.Def.Key;
                }
                else if (GameInputs.TryReduxDeployable(this, out var scalar, out _, out _, out var field))
                {
                    e.Controller = scalar;
                    e.ControllerName = field;
                }
                else
                {
                    Log($"{_name} AeroGate 'MODULE[{index}]' not bound: the controller is neither ours nor Redux's deployable ({field})");
                    return false;
                }
                var marker = GetComponent<Ksp1DeployableAeroControlSurface>();
                if (marker != null)
                {
                    Destroy(marker);
                    Log($"{_name} removed Redux's aero marker, the gate follows {e.ControllerName}");
                }
                else Log($"{_name} aero gate follows {e.ControllerName} (no Redux aero marker on the part)");
            }
            return true;
        }

        private void AddControls(bool inEditor)
        {
            int buttons = 0, actions = 0;
            var dynamicLabels = false;
            var sort = 10;
            foreach (var e in _entries)
            {
                var kind = e.Def.Kind;
                if (e.Disabled || !ControlLabels.HasAction(kind)) continue;
                try
                {
                    AddControl(e, inEditor, ref sort, ref buttons, ref actions, ref dynamicLabels);
                }
                catch (Exception ex)
                {
                    Disable(e, ex);
                }
            }
            if (buttons + actions > 0)
                Log($"{_name} action labels {(dynamicLabels ? "dynamic" : "static")}: {buttons} PAM button(s), {actions} action-group action(s)");
        }

        private void AddControl(Entry e, bool inEditor, ref int sort, ref int buttons, ref int actions, ref bool dynamicLabels)
        {
            {
                var kind = e.Def.Kind;
                var on = IsOn(e);
                var control = new EntryControl(this, e);
                var key = ContextKeyPrefix + e.Def.Key;
                if (ControlLabels.HasButton(kind))
                {
                    e.LabelOn = ControlLabels.Button(e.Def, true);
                    e.LabelOff = ControlLabels.Button(e.Def, false);
                    dynamicLabels |= e.LabelOn != e.LabelOff;
                    e.Button = new ModuleAction((Action)control.OnButton);
                    e.Button.SetContextKey(key);
                    data.AddAction(on ? e.LabelOn : e.LabelOff, e.Button, sort++);
                    e.ShownOn = on;
                    e.ShownVisible = ButtonVisible(e, inEditor);
                    data.SetVisible(e.Button, e.ShownVisible);
                    buttons++;
                }
                e.GroupState = new ModuleProperty<bool>(on);
                var group = ParseGroup(e.Def.ActionGroup);
                var action = NewGroupAction(control, group, e.GroupState, key);
                action.DisplayName = ControlLabels.Action(e.Def);
                action.ActionState = on;
                AddActionGroupAction(action);
                e.GroupAction = action;
                actions++;
            }
        }

        /// <summary>A toggle action that sets the state the action group asks for, keyed by the entry: KSP2 computes
        /// every action's new state from its <c>StateProperty</c> before invoking any, so a counterpart already moved
        /// by symmetry is not moved back. <c>ContextKey</c> (which keys the saved group mapping) has a private setter
        /// for this constructor; without it, an event action keyed by the entry toggles instead.</summary>
        private static ModuleActionGroupAction NewGroupAction(EntryControl control, KSPActionGroup group, ModuleProperty<bool> state, string key)
        {
            if (ActionContextKeySetter != null)
            {
                var action = new ModuleActionGroupAction((Action<bool>)control.OnGroup, group, state);
                try
                {
                    ActionContextKeySetter.Invoke(action, new object[] { key });
                    return action;
                }
                catch (Exception e)
                {
                    LogContextKeyFallback(e.GetBaseException().Message);
                }
            }
            else LogContextKeyFallback("no ContextKey setter");
            return new ModuleActionGroupAction((Action)control.OnGroupToggle, group, key) { StateProperty = state };
        }

        private static void LogContextKeyFallback(string why)
        {
            if (_loggedContextKeyFallback) return;
            _loggedContextKeyFallback = true;
            KSP1PartModPlugin.Log?.LogWarning($"[KSP1PartMod] anim: action groups use toggle events ({why})");
        }

        private static KSPActionGroup ParseGroup(string name) =>
            !string.IsNullOrWhiteSpace(name) && Enum.TryParse(name.Trim(), false, out KSPActionGroup g) ? g : KSPActionGroup.None;

        private string DefinitionNote()
        {
            try
            {
                var core = GameManager.Instance?.Game?.Parts != null && GameManager.Instance.Game.Parts.TryGet(_name, out var c) ? c : null;
                var definition = core?.modules?.OfType<Data_Ksp1Animations>().FirstOrDefault();
                if (definition == null) return "not found";
                return ReferenceEquals(definition, data)
                    ? $"#{RuntimeHelpers.GetHashCode(definition)}, shared with the definition"
                    : $"#{RuntimeHelpers.GetHashCode(definition)}, own copy";
            }
            catch (Exception e)
            {
                return "not read: " + e.GetBaseException().Message;
            }
        }

        // ---------------------------------------------------------------- per frame

        private void Drive(float dt, bool inEditor)
        {
            if (!_ready) return;
            for (var i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (!e.Disabled) Step(e, dt, inEditor, catchErrors: true);
            }
            SaveIfChanged();
        }

        private void Step(Entry e, float dt, bool inEditor, bool catchErrors)
        {
            try
            {
                switch (e.Def.Kind)
                {
                    case EntryKind.Toggle:
                        e.Toggle.Tick(dt);
                        Apply(e.States, e.Toggle.Pose, e.Def.Layer, ref e.StatesOn);
                        break;
                    case EntryKind.Deploy:
                    case EntryKind.Gear:
                        e.Deploy.Tick(dt);
                        Apply(e.States, e.Deploy.Pose, e.Def.Layer, ref e.StatesOn);
                        break;
                    case EntryKind.Throttle:
                    {
                        var (throttle, operational) = e.Engine != null ? e.Engine() : (0f, true);
                        e.Throttle.Tick(dt, inEditor, throttle, operational);
                        Apply(e.States, e.Throttle.Pose, e.Def.Layer, ref e.StatesOn);
                        break;
                    }
                    case EntryKind.Heat:
                    {
                        var (t, max) = e.Temperature != null ? e.Temperature() : (0f, 0f);
                        e.Heat.Tick(dt, t, max);
                        Apply(e.States, e.Heat.Pose, e.Def.Layer, ref e.StatesOn);
                        break;
                    }
                    case EntryKind.Color:
                        e.Color.Tick(dt);
                        e.Writer.Write(e.Color.Color);
                        break;
                    case EntryKind.Group:
                        e.Group.Tick(dt, inEditor);
                        Apply(e.States, e.Group.DeployPose, e.Def.Layer, ref e.StatesOn);
                        Apply(e.ActiveStates, e.Group.ActivePose, ActiveLayer, ref e.ActiveOn);
                        break;
                    case EntryKind.ReduxFollower:
                    {
                        var state = e.ReadDeploy();
                        if (e.Follower.Tick(dt, state, inEditor) && e.Follower.Following != null && e.SeenState)
                            Log($"{_name} follower '{e.Def.Clip}' -> {(e.Follower.Following.Value ? "extend" : "retract")} (Redux {state})");
                        e.SeenState = true;
                        Apply(e.States, e.Follower.Pose, e.Def.Layer, ref e.StatesOn);
                        break;
                    }
                    case EntryKind.ChuteFollower:
                    {
                        var state = e.ReadChute();
                        if (e.Chute.Tick(dt, state) && e.SeenState) Log($"{_name} parachute follower -> {state}");
                        e.SeenState = true;
                        Apply(e.States, e.Chute.SemiPose, e.Def.Layer, ref e.StatesOn);
                        Apply(e.ActiveStates, e.Chute.FullPose, e.Def.Layer, ref e.ActiveOn);
                        break;
                    }
                    case EntryKind.AeroGate:
                    {
                        var scalar = e.Controller();
                        var open = e.Aero.Tick(scalar);
                        if (e.AeroOpen != open)
                        {
                            ApplyAeroGate(e, open, scalar);
                            e.AeroOpen = open;
                        }
                        break;
                    }
                }
                RefreshControls(e, inEditor);
            }
            catch (Exception ex) when (catchErrors)
            {
                Disable(e, ex);
            }
        }

        /// <summary>KSP1's hold-pose idiom. A pose that is off disables the states once, then leaves them alone.</summary>
        private static void Apply(AnimationState[] states, ClipPose pose, int layer, ref bool on)
        {
            if (pose.Enabled)
            {
                for (var i = 0; i < states.Length; i++)
                {
                    var s = states[i];
                    if (s == null) continue;
                    s.enabled = true;
                    s.layer = layer;
                    s.weight = pose.Weight;
                    s.speed = 0f;
                    s.normalizedTime = pose.NormalizedTime;
                }
                on = true;
            }
            else if (on)
            {
                for (var i = 0; i < states.Length; i++)
                    if (states[i] != null) states[i].enabled = false;
                on = false;
            }
        }

        private void ApplyAeroGate(Entry e, bool open, float scalar)
        {
            var n = 0;
            foreach (var surface in GetComponents<Module_ControlSurface>())
            {
                if (surface == null || !surface.DataModules.TryGetByType<Data_ControlSurface>(out var d) || d == null) continue;
                d.AllowControl = open;
                d.IsCtrlSurfaceActive = open;
                d.DisableLiftingSurfaceForce = !open;
                if (!open) d.Deploy.SetValueInternal(false, skipEvent: true);
                n++;
            }
            Log($"{_name} aero gate {(open ? "open" : "closed")} at {scalar:0.###} ({n} control surface(s), follows {e.ControllerName})");
        }

        private void RefreshControls(Entry e, bool inEditor)
        {
            if (e.GroupState == null) return;
            var on = IsOn(e);
            if (e.GroupState.GetValue() != on) e.GroupState.SetValue(on);
            if (e.Button == null) return;
            if (e.ShownOn != on)
            {
                data.SetLabel(e.Button, on ? e.LabelOn : e.LabelOff);
                e.ShownOn = on;
            }
            var visible = ButtonVisible(e, inEditor);
            if (e.ShownVisible != visible)
            {
                data.SetVisible(e.Button, visible);
                e.ShownVisible = visible;
            }
        }

        private void SaveIfChanged()
        {
            var changed = false;
            for (var i = 0; i < _entries.Count && !changed; i++)
                changed = !_entries[i].Disabled && _entries[i].Machine != null && _entries[i].Machine.Changed;
            if (!_gate.ShouldEncode(changed, _urgent, Time.unscaledTime)) return;
            _urgent = false;
            try
            {
                foreach (var e in _entries)
                    if (!e.Disabled) e.Machine?.Save(_values);
                data.State.SetValue(EntryStateCodec.Encode(_values));
            }
            catch (Exception ex)
            {
                Error($"{_name} saving the animation state failed: {ex}");
            }
        }

        private void Disable(Entry e, Exception ex)
        {
            e.Disabled = true;
            Error($"{_name} {e.Def.Kind} '{Subject(e.Def)}' disabled for this session: {ex}");
            try
            {
                if (e.Button != null) data.SetVisible(e.Button, false);
            }
            catch (Exception)
            {
                // the entry is already reported
            }
        }

        // ---------------------------------------------------------------- controls

        /// <summary>Open, extended, deployed or lit, counting a move toward that end.</summary>
        private static bool IsOn(Entry e)
        {
            switch (e.Def.Kind)
            {
                case EntryKind.Toggle: return !e.Toggle.Closed;
                case EntryKind.Deploy:
                case EntryKind.Gear: return e.Deploy.State == DeployState.Extended || e.Deploy.State == DeployState.Extending;
                case EntryKind.Group: return e.Group.Deployed;
                case EntryKind.Color: return e.Color.On;
                case EntryKind.GearBridge: return e.ReduxState != null && e.ReduxState.GetValue();
                default: return false;
            }
        }

        /// <summary>True when the entry accepted the change.</summary>
        private static bool SetOn(Entry e, bool on, bool inEditor)
        {
            switch (e.Def.Kind)
            {
                case EntryKind.Toggle: return !e.Toggle.Closed != on && e.Toggle.Toggle(inEditor);
                case EntryKind.Deploy:
                case EntryKind.Gear: return on ? e.Deploy.Extend(inEditor) : e.Deploy.Retract(inEditor);
                case EntryKind.Group:
                    if (e.Group.Deployed == on) return false;
                    if (on) e.Group.Deploy(inEditor);
                    else e.Group.Retract(inEditor);
                    return true;
                case EntryKind.Color:
                    if (e.Color.On == on) return false;
                    e.Color.Toggle();
                    return true;
                case EntryKind.GearBridge:
                    if (IsOn(e) == on) return false;
                    e.ReduxSet(on);
                    return true;
                default: return false;
            }
        }

        private static bool Allowed(AnimEntryDef def, bool inEditor) => inEditor ? def.ToggleInEditor : def.ToggleInFlight;

        /// <summary>KSP1 hides the event of a fixed one-shot, and the retract of a non-retractable deployable in
        /// flight.</summary>
        private static bool ButtonVisible(Entry e, bool inEditor)
        {
            if (!Allowed(e.Def, inEditor)) return false;
            if (e.Toggle != null && e.Toggle.IsFixed) return false;
            if (e.Def.Kind == EntryKind.Deploy && !inEditor && !e.Def.Retractable && e.Deploy.State == DeployState.Extended) return false;
            return true;
        }

        /// <summary>The PAM button: this part, then its symmetry counterparts to the same state.</summary>
        private void Press(Entry e)
        {
            try
            {
                var inEditor = PartBackingMode == PartBackingModes.OAB;
                if (e.Disabled || !_ready || !Allowed(e.Def, inEditor)) return;
                var on = !IsOn(e);
                if (!SetOn(e, on, inEditor)) return;
                _urgent = true;
                PropagateToSymmetry(e.Def.Key, on);
            }
            catch (Exception ex)
            {
                Disable(e, ex);
            }
        }

        /// <summary>An action group: this part only (the group calls every counterpart's own action).</summary>
        private void SetFromGroup(Entry e, bool on)
        {
            try
            {
                var inEditor = PartBackingMode == PartBackingModes.OAB;
                if (e.Disabled || !_ready || !Allowed(e.Def, inEditor)) return;
                if (SetOn(e, on, inEditor)) _urgent = true;
            }
            catch (Exception ex)
            {
                Disable(e, ex);
            }
        }

        /// <summary>A counterpart's PAM button moved its entry with this key.</summary>
        private bool ApplyFromSymmetry(string key, bool on)
        {
            if (!_ready) return false;
            foreach (var e in _entries)
            {
                if (e.Disabled || e.Def.Key != key) continue;
                try
                {
                    if (!SetOn(e, on, PartBackingMode == PartBackingModes.OAB)) return false;
                    _urgent = true;
                    return true;
                }
                catch (Exception ex)
                {
                    Disable(e, ex);
                    return false;
                }
            }
            return false;
        }

        /// <summary>OAB: <c>OABPart.SymmetrySet.Parts</c>. Flight: <c>part.Model.SymmetrySet.Parts</c>, each mapped to
        /// its view with <c>SpaceSimulation.ModelViewMap.FromModel</c>, as Redux's own deployable does.</summary>
        private void PropagateToSymmetry(string key, bool on)
        {
            var n = 0;
            try
            {
                if (PartBackingMode == PartBackingModes.OAB)
                {
                    var self = OABPart;
                    var parts = self?.SymmetrySet?.Parts;
                    if (parts == null) return;
                    foreach (var p in parts)
                    {
                        if (p == null || p.UniqueId.Equals(self.UniqueId)) continue;
                        if (p.TryGetModule<Module_Ksp1Animations>(out var other) && other != null && other != this && other.ApplyFromSymmetry(key, on)) n++;
                    }
                }
                else if (PartBackingMode == PartBackingModes.Flight)
                {
                    var model = part?.Model;
                    var parts = model?.SymmetrySet?.Parts;
                    var map = Game?.SpaceSimulation?.ModelViewMap;
                    if (parts == null || map == null) return;
                    foreach (var pc in parts)
                    {
                        if (pc == null || pc.GlobalId.Equals(model.GlobalId)) continue;
                        var view = map.FromModel(pc.SimulationObject);
                        var behavior = view == null ? null : view.Part;
                        var other = behavior == null ? null : behavior.GetComponentInChildren<Module_Ksp1Animations>(true);
                        if (other != null && other != this && other.ApplyFromSymmetry(key, on)) n++;
                    }
                }
            }
            catch (Exception ex)
            {
                Warn($"{_name} symmetry for '{key}' failed: {ex.GetBaseException().Message}");
            }
            if (n > 0) Log($"{_name} symmetry: '{key}' {(on ? "on" : "off")} on {n} counterpart(s)");
        }

        // ---------------------------------------------------------------- helpers

        private string PartName() => data?.OwningPartData?.partName ?? gameObject.name;

        /// <summary>For catch blocks: never throws (the GameObject may already be destroyed).</summary>
        private string SafePartName()
        {
            try
            {
                return PartName();
            }
            catch (Exception)
            {
                return "<part>";
            }
        }

        private static string Subject(AnimEntryDef def) =>
            def.Clip ?? def.SecondClip ?? def.ShaderProperty ?? (def.Kind == EntryKind.AeroGate ? "MODULE[" + def.ControllerModuleIndex + "]" : def.Key);

        private static void Log(string message) => KSP1PartModPlugin.Log?.LogInfo("[KSP1PartMod] anim: " + message);

        private static void Warn(string message) => KSP1PartModPlugin.Log?.LogWarning("[KSP1PartMod] anim: " + message);

        private static void Error(string message) => KSP1PartModPlugin.Log?.LogError("[KSP1PartMod] anim: " + message);

        /// <summary>The delegates the game holds: one object per entry, so each action has its own target.</summary>
        private sealed class EntryControl
        {
            private readonly Module_Ksp1Animations _module;
            private readonly Entry _entry;

            public EntryControl(Module_Ksp1Animations module, Entry entry)
            {
                _module = module;
                _entry = entry;
            }

            public void OnButton() => _module.Press(_entry);

            public void OnGroup(bool on) => _module.SetFromGroup(_entry, on);

            public void OnGroupToggle() => _module.SetFromGroup(_entry, !IsOn(_entry));
        }

        /// <summary>One bound entry and what drives it.</summary>
        private sealed class Entry
        {
            public readonly AnimEntryDef Def;
            public IEntryMachine Machine;
            public ToggleMachine Toggle;
            public DeployMachine Deploy;
            public ThrottleFollower Throttle;
            public HeatScalar Heat;
            public ColorRamp Color;
            public GroupMachine Group;
            public AeroGate Aero;
            public ReduxFollower Follower;
            public ChuteFollower Chute;
            public Func<ReduxDeployState> ReadDeploy;
            public Func<ReduxChuteState> ReadChute;
            /// <summary>A follower has seen Redux's first state (shown at once, not logged).</summary>
            public bool SeenState;
            public AnimationState[] States = NoStates;
            public AnimationState[] ActiveStates = NoStates;
            public bool StatesOn, ActiveOn;
            public MaterialWriter Writer;
            public Func<(float throttle, bool operational)> Engine;
            public Func<(float t, float max)> Temperature;
            public Func<float> Controller;
            public string ControllerName;
            public bool? AeroOpen;
            public Action<bool> ReduxSet;
            public ModuleProperty<bool> ReduxState;
            public ModuleAction Button;
            public ModuleActionGroupAction GroupAction;
            public ModuleProperty<bool> GroupState;
            public string LabelOn, LabelOff;
            public bool? ShownOn;
            public bool ShownVisible;
            public bool Disabled;

            public Entry(AnimEntryDef def) => Def = def;
        }
    }
}
