using KSP1PartMod.Animation.Cfg;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>The PAM and action-group labels of an entry, from its KSP1 GUI names. "On" means open, extended,
    /// deployed or lit: KSP1 shows the start name (open, extend, deploy, on) while off and the end name while on.
    /// The PAM button follows the state, as KSP1's event does; the action group keeps one name, as KSP1's action
    /// does.</summary>
    public static class ControlLabels
    {
        /// <summary>Entries the player toggles from the PAM.</summary>
        public static bool HasButton(EntryKind kind) =>
            kind == EntryKind.Toggle || kind == EntryKind.Deploy || kind == EntryKind.Gear
            || kind == EntryKind.Color || kind == EntryKind.Group;

        /// <summary>Entries with an action-group action (the Gear bridge has only that).</summary>
        public static bool HasAction(EntryKind kind) => HasButton(kind) || kind == EntryKind.GearBridge;

        public static string Button(AnimEntryDef e, bool on) =>
            Text(on ? e.EndLabel : e.StartLabel) ?? Text(e.ActionLabel) ?? Default(e, on);

        public static string Action(AnimEntryDef e) =>
            Text(e.ActionLabel) ?? Text(e.StartLabel) ?? "Toggle " + Subject(e);

        private static string Default(AnimEntryDef e, bool on)
        {
            switch (e.Kind)
            {
                case EntryKind.Deploy:
                case EntryKind.Gear: return on ? "Retract" : "Extend";
                case EntryKind.Group: return on ? "Retract" : "Deploy";
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
