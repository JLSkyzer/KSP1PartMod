using System;
using System.Collections.Generic;

namespace KSP1PartMod.Native
{
    /// <summary>The <c>[KSP1PartMod] plume:</c> lines of 0.13.1 (<c>plume_diagnostics</c>), a diagnostic of the missing KSP1
    /// engine plumes: Redux builds them (<c>Ksp1EnginePlumeBuilder</c>: a stock KSP2 plume prefab under the thrust transform,
    /// named <c>[KSP1 Plume] …</c>, its skinned renderers switched off by <c>StopInitialVisuals</c>), and a
    /// <c>ThrottleVFXManager</c> on the part root, <c>Init</c>ed by <c>Module_Engine.OnInitialize</c>, switches them back on
    /// while the engine runs. Pure.</summary>
    public static class PlumeLines
    {
        public const string Prefix = "[KSP1PartMod] plume: ";
        public const string PlumeObjectPrefix = "[KSP1 Plume]";
        public const int MaxPlumes = 4, MaxRenderers = 6;

        /// <summary>The part type of an instance name: "type(Clone)" and "type.prefab_…" give "type".</summary>
        public static string TypeOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var cut = name.IndexOf("(Clone)", StringComparison.Ordinal);
            var prefab = name.IndexOf(".prefab_", StringComparison.Ordinal);
            if (prefab >= 0 && (cut < 0 || prefab < cut)) cut = prefab;
            return cut > 0 ? name.Substring(0, cut).TrimEnd() : name;
        }

        /// <summary>Per part type, two lines at most for the session: the bind line, then the running line.</summary>
        public sealed class Budget
        {
            private readonly Dictionary<string, int> _stage = new Dictionary<string, int>(StringComparer.Ordinal);
            private readonly HashSet<string> _watching = new HashSet<string>(StringComparer.Ordinal);

            /// <summary>True the first time a type binds (its bind line is due).</summary>
            public bool TakeBind(string type)
            {
                if (type == null || _stage.ContainsKey(type)) return false;
                _stage[type] = 1;
                return true;
            }

            /// <summary>True when an instance of <paramref name="type"/> should watch for its first run: the bind line is
            /// out, the running line is not, and no other instance watches.</summary>
            public bool TakeWatch(string type) =>
                type != null && _stage.TryGetValue(type, out var s) && s == 1 && _watching.Add(type);

            /// <summary>The watcher went away (part destroyed) before its engine ran: another instance may watch.</summary>
            public void ReleaseWatch(string type)
            {
                if (type != null) _watching.Remove(type);
            }

            /// <summary>The running line is out: nothing more for this type.</summary>
            public void Done(string type)
            {
                if (type == null) return;
                _stage[type] = 2;
                _watching.Remove(type);
            }
        }

        public sealed class Renderer
        {
            public string Kind, Material, Shader;
            public bool Enabled, HasMesh;
        }

        public sealed class Plume
        {
            public string Name;
            public bool ActiveInHierarchy;
            public float ScaleX, ScaleY, ScaleZ;
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public int RendererCount;
        }

        public sealed class Snapshot
        {
            public string Part, Moment, EngineState;
            public bool HasManager;
            public int Events, Effects;
            /// <summary>Null when the field was not found.</summary>
            public bool? Initialized, VfxActive, FxVisible;
            public readonly List<Plume> Plumes = new List<Plume>();
            public int PlumeCount;
        }

        private static string B(bool? b) => b.HasValue ? (b.Value ? "true" : "false") : "?";

        private static string F(float f) => f.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        public static string Line(Snapshot s)
        {
            var sb = new System.Text.StringBuilder(Prefix);
            sb.Append(s.Part ?? "?").Append(": ").Append(s.Moment ?? "?").Append(", engine ").Append(s.EngineState ?? "?").Append("; ");
            if (!s.HasManager) sb.Append("no ThrottleVFXManager on the part");
            else
                sb.Append("ThrottleVFXManager: ").Append(s.Events).Append(" event(s), ").Append(s.Effects).Append(" effect(s), initialized ")
                  .Append(B(s.Initialized)).Append(", vfx active ").Append(B(s.VfxActive)).Append(", fx visible ").Append(B(s.FxVisible));
            sb.Append("; ").Append(s.PlumeCount).Append(" plume object(s)");
            foreach (var p in s.Plumes)
            {
                sb.Append(" | '").Append(p.Name).Append("' active ").Append(B(p.ActiveInHierarchy)).Append(", scale (")
                  .Append(F(p.ScaleX)).Append(", ").Append(F(p.ScaleY)).Append(", ").Append(F(p.ScaleZ)).Append("), ")
                  .Append(p.RendererCount).Append(" renderer(s)");
                for (var i = 0; i < p.Renderers.Count; i++)
                {
                    var r = p.Renderers[i];
                    sb.Append(i == 0 ? ": " : ", ").Append(r.Kind).Append(r.Enabled ? " on" : " off").Append(r.HasMesh ? " mesh" : " no mesh")
                      .Append(" '").Append(r.Material ?? "no material").Append("'/'").Append(r.Shader ?? "no shader").Append('\'');
                }
                if (p.RendererCount > p.Renderers.Count) sb.Append(", …");
            }
            if (s.PlumeCount > s.Plumes.Count) sb.Append(" | …");
            return sb.ToString();
        }

        public static string NotInitialized(string part) => Prefix + part + ": ThrottleVFXManager not initialized";

        public static string Armed(bool on) => Prefix + (on ? "diagnostics armed (plume_diagnostics)" : "diagnostics off");
    }
}
