namespace KSP1PartMod.Animation.Cfg
{
    /// <summary>What one entry of ours does. Toggle: ModuleAnimateGeneric. Deploy: the ModuleDeployablePart family.
    /// Gear: ModuleWheelDeployment, ModuleLandingLeg. Throttle: FXModuleAnimateThrottle. Heat: ModuleAnimateHeat.
    /// Color: ModuleColorChanger. Group: ModuleAnimationGroup. AeroGate: KRE ModuleDeployableAero. GearBridge: a
    /// Gear action group for a gear clip that Redux's deployable module plays.</summary>
    public enum EntryKind { Toggle, Deploy, Gear, Throttle, Heat, Color, Group, AeroGate, GearBridge }

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
    }
}
