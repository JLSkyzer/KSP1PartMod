using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>The PAM and action-group labels of an entry, from its KSP1 GUI names. "On" means open, extended,
    /// deployed or lit: KSP1 shows the start name (open, extend, deploy, on) while off and the end name while on.
    /// The PAM button follows the state, as KSP1's event does; the action group keeps one name, as KSP1's action
    /// does.</summary>
    public static class ControlLabels
    {
        /// <summary>The fixed PAM label of a decoupler preview whose cfg has no <c>editorGUIName</c> (12 of the 13
        /// BDB <c>ModuleBdbDecouplerAnimation</c> blocks).</summary>
        public const string DecouplerPreview = "Preview decoupler";

        /// <summary>Entries the player toggles from the PAM.</summary>
        public static bool HasButton(EntryKind kind) =>
            kind == EntryKind.Toggle || kind == EntryKind.Deploy || kind == EntryKind.Gear
            || kind == EntryKind.Color || kind == EntryKind.Group
            || kind == EntryKind.EngineDeploy || kind == EntryKind.DecouplerClip || kind == EntryKind.ScienceToggle
            || kind == EntryKind.LandingBag || kind == EntryKind.AccelSpring;

        /// <summary>Buttons that exist in the editor only (KSP1 editor events): no action-group action.</summary>
        public static bool EditorOnly(EntryKind kind) => kind == EntryKind.EngineDeploy || kind == EntryKind.DecouplerClip;

        /// <summary>Entries with an action-group action (the Gear bridge has only that).</summary>
        public static bool HasAction(EntryKind kind) => HasButton(kind) && !EditorOnly(kind) || kind == EntryKind.GearBridge;

        /// <summary>Entries with a button or an action.</summary>
        public static bool HasControl(EntryKind kind) => HasButton(kind) || HasAction(kind);

        /// <summary>Whether the entry's button and action work in this scene: an editor-only button in the editor (an
        /// engine nozzle preview only when the cfg has one), else KSP1's <c>guiActiveEditor</c> / <c>guiActive</c>
        /// (<see cref="AnimEntryDef.ToggleInEditor"/> / <see cref="AnimEntryDef.ToggleInFlight"/>).</summary>
        public static bool Allowed(AnimEntryDef e, bool inEditor)
        {
            switch (e.Kind)
            {
                case EntryKind.EngineDeploy: return inEditor && e.EditorToggle;
                case EntryKind.DecouplerClip: return inEditor;
                default: return inEditor ? e.ToggleInEditor : e.ToggleInFlight;
            }
        }

        public static string Button(AnimEntryDef e, bool on) =>
            e.Kind == EntryKind.DecouplerClip
                ? Text(e.StartLabel) ?? DecouplerPreview
                : Text(on ? e.EndLabel : e.StartLabel) ?? Text(e.ActionLabel) ?? Default(e, on);

        public static string Action(AnimEntryDef e) =>
            Text(e.ActionLabel) ?? Text(e.StartLabel) ?? "Toggle " + Subject(e);

        private static string Default(AnimEntryDef e, bool on)
        {
            switch (e.Kind)
            {
                case EntryKind.Deploy:
                case EntryKind.Gear: return on ? "Retract" : "Extend";
                case EntryKind.Group:
                case EntryKind.EngineDeploy:
                case EntryKind.ScienceToggle:
                case EntryKind.LandingBag: return on ? "Retract" : "Deploy";
                case EntryKind.AccelSpring: return on ? "Stow" : "Deploy";
                case EntryKind.Color: return on ? "Turn off" : "Turn on";
                default: return "Toggle " + Subject(e);
            }
        }

        private static string Subject(AnimEntryDef e) =>
            e.Kind == EntryKind.Gear || e.Kind == EntryKind.GearBridge ? "gear"
            : Text(e.Clip) ?? Text(e.ShaderProperty) ?? e.Kind.ToString().ToLowerInvariant();

        private static string Text(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
