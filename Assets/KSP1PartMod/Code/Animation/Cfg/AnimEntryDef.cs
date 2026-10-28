namespace KSP1PartMod.Animation.Cfg
{
    /// <summary>What one entry of ours does. Toggle: ModuleAnimateGeneric. Deploy: the ModuleDeployablePart family.
    /// Gear: ModuleWheelDeployment, ModuleLandingLeg. Throttle: FXModuleAnimateThrottle. Heat: ModuleAnimateHeat.
    /// Color: ModuleColorChanger. Group: ModuleAnimationGroup. AeroGate: KRE ModuleDeployableAero. GearBridge: a
    /// Gear action group for a gear clip that Redux's deployable module plays. ReduxFollower: Redux's single deployable
    /// source, whose Mecanim copy does not move; we draw its legacy clip from Redux's state. ChuteFollower: the same for
    /// ModuleParachute (Clip = semi, SecondClip = full, SpeedFactorIn / Out = semi / full deployment speed). The last ten kinds are the
    /// plugin modules of <see cref="PluginEntryParser"/> (DeployableEngines, Bluedog_DB, DMagic, FXModuleAnimateRCS).</summary>
    public enum EntryKind { Toggle, Deploy, Gear, Throttle, Heat, Color, Group, AeroGate, GearBridge, ReduxFollower, ChuteFollower,
        EngineDeploy, EngineMode, LookAt, DecouplerClip, RcsGlow, RcsEmissive, MeshHider, ScienceToggle, AccelSpring, LandingBag }

    /// <summary>One KSP1 animation module we drive, read from its cfg. Plain public fields so that Newtonsoft and
    /// <c>IOProvider</c> serialize it into the part definition.</summary>
    public sealed class AnimEntryDef
    {
        public EntryKind Kind;
        /// <summary><c>&lt;kind&gt;:&lt;clip or property&gt;:&lt;ordinal&gt;</c>, stable across loads: the prefix of the
        /// entry's saved values.</summary>
        public string Key;
        /// <summary>Index of the KSP1 MODULE node in cfg order.</summary>
        public int ModuleIndex;
        /// <summary>The clip (Group: the deploy clip).</summary>
        public string Clip;
        /// <summary>Group: the active clip.</summary>
        public string SecondClip;
        /// <summary>Group: the deactivate clip.</summary>
        public string ThirdClip;
        /// <summary>Gear: the GameObject that holds the Animation (ModuleWheelDeployment <c>animationTrfName</c>).</summary>
        public string AnimatorName;
        public int Layer;
        /// <summary>Unity WrapMode forced on the state; 0 keeps the clip's own (8 = ClampForever).</summary>
        public int WrapMode;
        /// <summary>Gear: speed factor while deploying (<c>extendDurationFactor</c>).</summary>
        public float SpeedFactorIn = 1;
        /// <summary>Gear: speed factor while retracting (<c>retractDurationFactor</c>).</summary>
        public float SpeedFactorOut = 1;
        /// <summary>Editor speed. Toggle: times the clip length (KSP1 10, i.e. 0.1 s; 0 when
        /// <c>instantAnimInEditor = false</c>, meaning the flight speed). Deploy: <c>editorAnimationSpeedMult</c>.
        /// Gear and Group: 1000, an instant snap.</summary>
        public float EditorSpeedMult = 10;
        /// <summary>Throttle: <c>responseSpeed</c>.</summary>
        public float ResponseSpeed;
        /// <summary>Toggle: not <c>isOneShot</c>. Deploy: <c>retractable</c>.</summary>
        public bool Retractable = true;
        /// <summary>Gear: the clip runs forward to deploy (<c>deployedPosition</c> = 1).</summary>
        public bool DeployForward = true;
        public bool PlayInEditor, WeightOnOperational;
        /// <summary>Color: ramp at <see cref="Rate"/> rather than snap.</summary>
        public bool UseRate = true;
        /// <summary>Group: play the active clip while deployed.</summary>
        public bool AlwaysActive;
        public bool ToggleInEditor = true, ToggleInFlight = true;
        public string StartLabel, EndLabel, ActionLabel;
        /// <summary>A name from <see cref="ActionGroupMap"/>, or null.</summary>
        public string ActionGroup;
        public string EngineName;
        public string ShaderProperty;
        /// <summary>Color: ramp per second (<c>animRate</c>).</summary>
        public float Rate = 1;
        /// <summary>Gear: KSP1 <c>deployedPosition</c>. AeroGate: the controller scalar at which the surface is
        /// deployed (<c>DeployModulePos</c>).</summary>
        public float DeployedPosition = 1;
        /// <summary>Color: red, green, blue, alpha. Heat: the <c>animStateCurve</c> (null: linear 0..1). Each curve
        /// is its keys flattened as time, value, inTangent, outTangent; a NaN tangent means the key gave only time
        /// and value, which KSP1 adds with Unity's <c>AnimationCurve.AddKey(time, value)</c>.</summary>
        public float[][] Curves;
        /// <summary>AeroGate: KSP1 MODULE index of the controller (<c>DeployModuleIndex</c>).</summary>
        public int ControllerModuleIndex = -1;
        /// <summary>Color: <c>includedRenderer</c> and <c>excludedRenderer</c> values (exclusion wins).</summary>
        public string[] IncludedRenderers, ExcludedRenderers;
        /// <summary>Color: an <c>FXModuleThrottleEffects</c> of the part names this colour changer's <c>moduleID</c>
        /// (<see cref="ScalarSourceParser"/>): the colour follows that setter's scalar, not the toggle. The setter's
        /// <c>engineName</c> goes to <see cref="EngineName"/>.</summary>
        public bool ScalarLinked;
        /// <summary>Color with <see cref="ScalarLinked"/>: the setter's <c>responseSpeed</c>.</summary>
        public float ScalarResponse = 0.5f;
        /// <summary>Color with <see cref="ScalarLinked"/>: the setter's <c>dependOnEngineState</c>,
        /// <c>dependOnOutput</c> and <c>dependOnThrottle</c>.</summary>
        public bool DependOnEngineState, DependOnOutput, DependOnThrottle;
        /// <summary>Color with <see cref="ScalarLinked"/>: the colour changer's <c>moduleID</c> the setter named (for the
        /// log).</summary>
        public string ModuleId;

        /// <summary>EngineDeploy, DecouplerClip, EngineMode: the fraction of the clip at which KSP1 lets the engine
        /// ignite or the decoupler fire (logged only: we never delay either).</summary>
        public float WaitForAnimation;
        /// <summary>EngineDeploy: a VAB preview toggle exists.</summary>
        public bool EditorToggle;
        /// <summary>EngineDeploy (Bluedog_DB): the module uses only the first animator found.</summary>
        public bool FirstAnimatorOnly;
        /// <summary>EngineDeploy (Bluedog_DB): the deployed state is saved.</summary>
        public bool PersistDeployed;
        /// <summary>EngineMode: <c>EngineIDAtTimeZero</c>, the mode in which the clip sits at time 0.</summary>
        public string ModeAtTimeZero;
        /// <summary>EngineMode: <c>SpeedMultiplier</c>. ScienceToggle: <c>animSpeed</c>.</summary>
        public float SpeedMultiplier = 1;
        /// <summary>RcsGlow: <c>thrustForceMult</c>.</summary>
        public float ThrustForceMult = 1;
        /// <summary>LookAt: the <c>targetName</c> / <c>rotatorsName</c> of each CONSTRAINLOOKFX (same length).
        /// MeshHider: <see cref="TransformNames"/>.</summary>
        public string[] TargetNames, RotatorNames, TransformNames;
        /// <summary>LookAt: <c>axisConstrained</c> and <c>axisScales</c> (x, y, z).</summary>
        public bool[] AxisConstrained;
        public float[] AxisScales;
        /// <summary>AccelSpring: <c>springK</c>, <c>springDamping</c>, and the clip positions of the retracted
        /// (<c>stowedAnchor</c>) and deployed (<c>deployedAnchor</c>) anchors.</summary>
        public float SpringK, SpringDamping, AnchorRetracted, AnchorDeployed;
        /// <summary>ScienceToggle: <c>oneWayAnimation</c>.</summary>
        public bool OneWay;
        /// <summary>LandingBag: speed of the backward play (KSP1: 0.2).</summary>
        public float RetractSpeed = 1;

        /// <summary>The B9PartSwitch subtypes that switch this entry's module off (<c>moduleActive = false</c>), one
        /// gate per switch (<see cref="B9GatePlanner"/>); null when no switch does. Any kind.</summary>
        public B9Gate[] Gates;
    }
}
