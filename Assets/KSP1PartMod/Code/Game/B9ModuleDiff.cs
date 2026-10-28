using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSP.IO;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Cfg;
using Newtonsoft.Json.Linq;
using Redux.Ksp1Import;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;
using VSwift.Modules.Transformers;

namespace KSP1PartMod.Game
{
    /// <summary>B9 module data, plan (d.2) to (d.5): the KSP1 module a subtype edits is imported twice by Redux's own
    /// importers, unedited and edited, each into a scratch context; the two module data objects are compared field by
    /// field over their <c>[KSPDefinition]</c> fields, through <c>IOProvider</c> JSON; each field that differs becomes one
    /// <c>ModuleDefinitionTransformer</c>. The unit conversions are therefore exactly Redux's. Nothing of the real part is
    /// written here: the scratch contexts only read the part's attach nodes and resource containers.</summary>
    internal static class B9ModuleDiff
    {
        public const string TranslatorTypeName = "Redux.Ksp1Import.Ksp1PartTranslator";
        public const string OrderedEnginesName = "GetOrderedSwitchedEngineModules";
        private const string DefinitionAttributeName = "KSPDefinitionAttribute";

        /// <summary>The members this step needs, resolved by name against the loaded game.</summary>
        internal sealed class Members
        {
            public MethodInfo AddValue;        // internal void Ksp1ConfigNode.AddValue(string, string)
            public MethodInfo AddNode;         // internal void Ksp1ConfigNode.AddNode(Ksp1ConfigNode)
            public MethodInfo OrderedEngines;  // private static List<Ksp1ConfigNode> Ksp1PartTranslator.GetOrderedSwitchedEngineModules(Ksp1ConfigNode)
            public readonly List<string> Missing = new List<string>();
        }

        internal enum Outcome
        {
            /// <summary>Imported by Redux on both sides; <see cref="Result.Transformers"/> holds the differing fields (maybe
            /// none).</summary>
            Compared,
            /// <summary>Redux imports no native module for the target (unsupported or dropped module, a second gimbal, a
            /// failed import).</summary>
            NotImported,
            /// <summary>The native behaviour is not on the part at all.</summary>
            NoNativeModule,
            /// <summary>The native behaviour appears more than once on the part: V-SwiFT edits the first only.</summary>
            Repeated,
        }

        internal sealed class Result
        {
            public Outcome Outcome;
            public Type Behaviour;
            public int Count;
            public string Reason;
            public readonly List<ModuleDefinitionTransformer> Transformers = new List<ModuleDefinitionTransformer>();
        }

        private static Action<Ksp1ConfigNode, string, string> _addValue;
        private static Action<Ksp1ConfigNode, Ksp1ConfigNode> _addNode;
        private static Func<Ksp1ConfigNode, List<Ksp1ConfigNode>> _orderedEngines;
        private static readonly ConcurrentDictionary<Type, FieldInfo[]> Fields = new ConcurrentDictionary<Type, FieldInfo[]>();

        internal static bool IsBound => _addValue != null && _addNode != null && _orderedEngines != null;

        internal static Members Resolve()
        {
            var m = new Members();
            m.AddValue = AccessTools.Method(typeof(Ksp1ConfigNode), "AddValue", new[] { typeof(string), typeof(string) });
            if (m.AddValue == null || m.AddValue.IsStatic) { m.AddValue = null; m.Missing.Add("Ksp1ConfigNode.AddValue(string, string)"); }
            m.AddNode = AccessTools.Method(typeof(Ksp1ConfigNode), "AddNode", new[] { typeof(Ksp1ConfigNode) });
            if (m.AddNode == null || m.AddNode.IsStatic) { m.AddNode = null; m.Missing.Add("Ksp1ConfigNode.AddNode(Ksp1ConfigNode)"); }
            var translator = AccessTools.TypeByName(TranslatorTypeName);
            m.OrderedEngines = translator == null ? null : AccessTools.Method(translator, OrderedEnginesName, new[] { typeof(Ksp1ConfigNode) });
            if (m.OrderedEngines == null || !m.OrderedEngines.IsStatic || m.OrderedEngines.ReturnType != typeof(List<Ksp1ConfigNode>))
            {
                m.OrderedEngines = null;
                m.Missing.Add("Ksp1PartTranslator." + OrderedEnginesName + "(Ksp1ConfigNode)");
            }
            return m;
        }

        /// <summary>Binds the members (all must resolve).</summary>
        internal static bool Bind(Members m)
        {
            if (m == null || m.Missing.Count > 0) return false;
            _addValue = (Action<Ksp1ConfigNode, string, string>)Delegate.CreateDelegate(typeof(Action<Ksp1ConfigNode, string, string>), m.AddValue);
            _addNode = (Action<Ksp1ConfigNode, Ksp1ConfigNode>)Delegate.CreateDelegate(typeof(Action<Ksp1ConfigNode, Ksp1ConfigNode>), m.AddNode);
            _orderedEngines = (Func<Ksp1ConfigNode, List<Ksp1ConfigNode>>)Delegate.CreateDelegate(typeof(Func<Ksp1ConfigNode, List<Ksp1ConfigNode>>), m.OrderedEngines);
            return true;
        }

        internal static void Reset()
        {
            _addValue = null;
            _addNode = null;
            _orderedEngines = null;
        }

        /// <summary>Redux's own parsed node with the values and children of <paramref name="node"/> (same order).</summary>
        internal static Ksp1ConfigNode ToConfigNode(IKsp1Node node)
        {
            var result = new Ksp1ConfigNode(node.Name ?? "MODULE");
            foreach (var v in node.AllValues)
                if (v.Key != null) _addValue(result, v.Key, v.Value ?? "");
            foreach (var child in node.AllNodes)
                if (child != null) _addNode(result, ToConfigNode(child));
            return result;
        }

        /// <summary>The public instance fields of <paramref name="dataType"/> that carry <c>[KSPDefinition]</c> (matched by
        /// the attribute's type name), cached per type.</summary>
        internal static FieldInfo[] DefinitionFields(Type dataType) =>
            Fields.GetOrAdd(dataType, t => t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.GetCustomAttributes(true).Any(a => a.GetType().Name == DefinitionAttributeName))
                .ToArray());

        /// <summary>The native fields that <paramref name="edited"/> (the edited copy of the part's MODULE number
        /// <paramref name="targetIndex"/>) changes, through <paramref name="registry"/>, as V-SwiFT transformers.</summary>
        internal static Result Diff(Ksp1PartModuleImporterRegistry registry, Ksp1ConfigNode partNode, Ksp1PartImportContext context,
            int targetIndex, IKsp1Node edited)
        {
            var result = new Result();
            var modules = partNode.GetNodes("MODULE").ToList();
            if (targetIndex < 0 || targetIndex >= modules.Count || edited == null)
                return NotImported(result, "no such module");
            var target = modules[targetIndex];
            var name = target.GetValue("name");
            var editedNode = ToConfigNode(edited);

            SerializedPartModule before = default, after = default;
            bool imported;
            var ordered = Ksp1PartModuleImporterRegistry.IsEngineModuleName(name) ? _orderedEngines(partNode) : null;
            if (ordered != null && ordered.Count > 0)
            {
                // Redux imports a part's switched engines together, one mode each (multi-mode engine).
                var i = ordered.FindIndex(n => ReferenceEquals(n, target));
                if (i < 0) return NotImported(result, "not among Redux's engine modes");
                var editedList = new List<Ksp1ConfigNode>(ordered) { [i] = editedNode };
                imported = registry.TryImportEngineModes(ordered, Scratch(context, partNode), out before)
                           && registry.TryImportEngineModes(editedList, Scratch(context, partNode), out after);
            }
            else
            {
                if (Ksp1PartModuleImporterRegistry.IsGimbalModuleName(name) &&
                    !ReferenceEquals(modules.First(m => Ksp1PartModuleImporterRegistry.IsGimbalModuleName(m.GetValue("name"))), target))
                    return NotImported(result, "not the part's first gimbal, the only one Redux imports");
                imported = registry.TryImport(target, Scratch(context, partNode), out before)
                           && registry.TryImport(editedNode, Scratch(context, partNode), out after);
            }
            if (!imported || before.BehaviourType == null || before.BehaviourType != after.BehaviourType)
                return NotImported(result, "Redux imports no native module for it");

            result.Behaviour = before.BehaviourType;
            result.Count = context.PartData?.serializedPartModules?.Count(m => m.BehaviourType == before.BehaviourType) ?? 0;
            if (result.Count == 0)
            {
                result.Outcome = Outcome.NoNativeModule;
                return result;
            }
            if (result.Count > 1)
            {
                result.Outcome = Outcome.Repeated;
                return result;
            }

            result.Outcome = Outcome.Compared;
            var a = before.ModuleData ?? new List<SerializedModuleData>();
            var b = after.ModuleData ?? new List<SerializedModuleData>();
            for (var k = 0; k < Math.Min(a.Count, b.Count); k++)
            {
                var da = a[k].DataObject;
                var db = b[k].DataObject;
                if (da == null || db == null || da.GetType() != db.GetType()) continue;
                foreach (var f in DefinitionFields(da.GetType()))
                {
                    var ja = IOProvider.ToJson(f.GetValue(da));
                    var jb = IOProvider.ToJson(f.GetValue(db));
                    if (string.Equals(ja, jb, StringComparison.Ordinal)) continue;
                    result.Transformers.Add(new ModuleDefinitionTransformer
                    {
                        BehaviourType = before.BehaviourType.Name,
                        DataType = da.GetType().Name,
                        Key = f.Name,
                        Value = JToken.Parse(jb),
                    });
                }
            }
            return result;
        }

        private static Result NotImported(Result r, string reason)
        {
            r.Outcome = Outcome.NotImported;
            r.Reason = reason;
            return r;
        }

        /// <summary>A throwaway context: the real part node, a throwaway part data that shares (reads only) the real one's
        /// attach nodes (decoupler node ids) and resource containers (heat shield capacity), a scratch report.</summary>
        private static Ksp1PartImportContext Scratch(Ksp1PartImportContext real, Ksp1ConfigNode partNode) =>
            new Ksp1PartImportContext(real.PartName, new Ksp1ImportReport())
            {
                PartNode = real.PartNode ?? partNode,
                PartData = new PartData
                {
                    partName = real.PartData?.partName ?? real.PartName,
                    attachNodes = real.PartData?.attachNodes,
                    resourceContainers = real.PartData?.resourceContainers,
                },
            };
    }
}
