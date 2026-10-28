using System.Collections.Generic;

namespace KSP1PartMod.Audit
{
    /// <summary>One frame of an exception stack.</summary>
    public readonly struct StackFrameInfo
    {
        public StackFrameInfo(string typeName, bool isPartModule, string method)
        {
            TypeName = typeName;
            IsPartModule = isPartModule;
            Method = method;
        }

        public string TypeName { get; }
        public bool IsPartModule { get; }
        public string Method { get; }
    }

    public static class StackModuleLocator
    {
        /// <summary>The first part-module frame from the top (frame 0 is the throw site), by type name, or null.
        /// Harmony wrappers are attributed to the module: <c>DMD&lt;Module_X::Method&gt;</c>, <c>DMD&lt;Module_X&gt;</c> and
        /// <c>Module_X_Patch1</c> all give <c>Module_X</c>.</summary>
        public static string FailingModule(IReadOnlyList<StackFrameInfo> frames)
        {
            if (frames == null) return null;
            for (int i = 0; i < frames.Count; i++)
                if (frames[i].IsPartModule)
                    return Clean(frames[i].TypeName);
            return null;
        }

        /// <summary>Strips a <c>DMD&lt;…&gt;</c> wrapper (and what follows <c>::</c>) and a trailing <c>_Patch</c>[digits].</summary>
        internal static string Clean(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return typeName;
            var name = typeName;
            if (name.StartsWith("DMD<"))
            {
                name = name.Substring(4);
                if (name.EndsWith(">")) name = name.Substring(0, name.Length - 1);
                var sep = name.IndexOf("::", System.StringComparison.Ordinal);
                if (sep >= 0) name = name.Substring(0, sep);
            }
            int p = name.LastIndexOf("_Patch", System.StringComparison.Ordinal);
            if (p > 0)
            {
                bool digits = true;
                for (int i = p + 6; i < name.Length; i++) if (name[i] < '0' || name[i] > '9') { digits = false; break; }
                if (digits) name = name.Substring(0, p);
            }
            return name;
        }
    }
}
