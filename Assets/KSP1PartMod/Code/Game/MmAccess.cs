using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using KSP1PartMod.ModuleManager;

namespace KSP1PartMod.Game
{
    /// <summary>The members of Redux's <c>Ksp1ConfigFinalizer</c> that the ModuleManager glue (<see cref="MmPatches"/>) needs,
    /// resolved once by name (plan (c)). The node and value types are private and nested: they are handled as
    /// <c>object</c>, through small methods emitted once (<see cref="DynamicMethod"/>, owner = the finalizer, so they may
    /// touch its private types). The emitted readers do not box the <c>MmValue</c> struct: the common path of the prefixes
    /// reads value names without allocating. Each member that is not found is listed in <see cref="Missing"/>; a feature is
    /// ready only when all of its members are.</summary>
    internal sealed class MmAccess
    {
        public const string FinalizerTypeName = "Redux.Ksp1Import.Ksp1ConfigFinalizer";

        // Feature names, as the log prints them.
        public const string SafetyNetFeature = "plugin safety net";
        public const string BeforeAfterFeature = "BEFORE/AFTER rule";
        public const string ArithmeticFeature = "arithmetic";
        public const string IndexFeature = "index selectors";

        public Type Finalizer, MmNode, MmValue, NameDescriptor, PatchCommand;
        public MethodInfo Finalize, ModIsAvailable, CopyWithSatisfiedNeeds, ApplyNodeContents, ApplyChildPatch, NodeMatches,
            ParseName, Clone, NodeNameGetter, ValuesGetter, NodesGetter, ValueNameGetter, ValueRawGetter, CommandGetter,
            BaseNameGetter;
        public ConstructorInfo MmValueCtor;

        /// <summary>The members not found (or not of the expected shape), by name.</summary>
        public readonly List<string> Missing = new List<string>();

        // Emitted once, after the members are found.
        public Func<object, IList> Values, Nodes;
        public Func<object, string> NodeName;
        public Func<object, int, string> ValueName, ValueRaw;
        public Action<object, string, string> AddValue;
        public Action<object, object, object> CallApplyNodeContents, CallApplyChildPatch;
        public Func<object, object, bool> CallNodeMatches;
        public Func<string, object> CallParseName;
        public Func<object, object> CallClone;
        public Func<object, int> CommandOf;
        public Func<object, string> BaseNameOf;
        /// <summary><c>PatchCommand</c> value → our <see cref="MmCommand"/>, matched by name.</summary>
        public readonly Dictionary<int, MmCommand> Commands = new Dictionary<int, MmCommand>();

        /// <summary>Members of each feature (plan, Global Constraints): the safety net needs <c>ModIsAvailable</c> and
        /// <c>CopyWithSatisfiedNeeds</c> together; the value edits (arithmetic and value indices) need
        /// <c>ApplyNodeContents</c>, the <c>MmValue</c> constructor and the node readers; node indices need
        /// <c>ApplyChildPatch</c>, <c>NodeMatches</c> and Redux's name parser.</summary>
        public static readonly string[] SafetyNetMembers = { "Finalize", "ModIsAvailable", "CopyWithSatisfiedNeeds" };
        public static readonly string[] BeforeAfterMembers = { "Finalize", "CopyWithSatisfiedNeeds" };
        public static readonly string[] ValueMembers =
        {
            "Finalize", "ApplyNodeContents", "ApplyChildPatch", "MmNode", "MmValue", "MmValue(string, string)",
            "MmNode.Name", "MmNode.Values", "MmNode.Nodes", "MmValue.Name", "MmValue.RawValue", "node accessors",
        };
        public static readonly string[] NodeIndexMembers =
        {
            "NodeMatches", "NameDescriptor", "NameDescriptor.Parse", "NameDescriptor.Command", "NameDescriptor.BaseName",
            "PatchCommand", "MmNode.Clone", "name parser accessors",
        };

        public IEnumerable<string> MissingOf(IEnumerable<string> members) => members.Where(m => Missing.Contains(m));
        public bool Has(IEnumerable<string> members) => !MissingOf(members).Any();

        public bool SafetyNetReady => Has(SafetyNetMembers);
        public bool BeforeAfterReady => Has(BeforeAfterMembers);
        public bool ValuesReady => Has(ValueMembers);
        public bool NodeIndexReady => ValuesReady && Has(NodeIndexMembers);

        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>Resolves every member. Never throws: an unexpected failure is listed as a missing member.</summary>
        public static MmAccess TryBind(out List<string> missing)
        {
            var a = new MmAccess();
            try
            {
                a.Bind();
            }
            catch (Exception e)
            {
                a.Missing.Add("binding (" + e.GetType().Name + ": " + e.Message + ")");
                foreach (var m in SafetyNetMembers.Concat(ValueMembers).Concat(NodeIndexMembers))
                    if (!a.Missing.Contains(m)) a.Missing.Add(m);
            }
            missing = a.Missing;
            return a;
        }

        private void Need(bool found, string member)
        {
            if (!found && !Missing.Contains(member)) Missing.Add(member);
        }

        private static Type Nested(Type outer, string name) =>
            outer?.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic);

        private static MethodInfo Getter(Type type, string property, Type returnType)
        {
            var p = type?.GetProperty(property, Instance);
            var g = p?.GetGetMethod(true);
            return g != null && (returnType == null || g.ReturnType == returnType) ? g : null;
        }

        private static MethodInfo StaticMethod(Type type, string name, Type returnType, params Type[] parameters)
        {
            if (type == null || parameters.Any(p => p == null)) return null;
            var m = type.GetMethod(name, Static, null, parameters, null);
            return m != null && (returnType == null || m.ReturnType == returnType) ? m : null;
        }

        private void Bind()
        {
            Finalizer = AccessTools.TypeByName(FinalizerTypeName);
            Need(Finalizer != null, "Ksp1ConfigFinalizer");
            if (Finalizer == null)
            {
                foreach (var m in SafetyNetMembers.Concat(ValueMembers).Concat(NodeIndexMembers)) Need(false, m);
                return;
            }
            MmNode = Nested(Finalizer, "MmNode");
            MmValue = Nested(Finalizer, "MmValue");
            NameDescriptor = Nested(Finalizer, "NameDescriptor");
            PatchCommand = Nested(Finalizer, "PatchCommand");
            Need(MmNode != null, "MmNode");
            Need(MmValue != null && MmValue.IsValueType, "MmValue");
            Need(NameDescriptor != null, "NameDescriptor");
            Need(PatchCommand != null && PatchCommand.IsEnum, "PatchCommand");

            var configFile = AccessTools.TypeByName("Redux.Ksp1Import.Ksp1ConfigFile");
            var configNode = AccessTools.TypeByName("Redux.Ksp1Import.Config.Ksp1ConfigNode");
            var importReport = AccessTools.TypeByName("Redux.Ksp1Import.Ksp1ImportReport");
            var mmReport = AccessTools.TypeByName("Redux.Ksp1Import.Ksp1ModuleManagerReport");
            var database = AccessTools.TypeByName("Redux.Ksp1Import.Ksp1FinalizedConfigDatabase");
            var files = configFile == null ? null : typeof(IReadOnlyList<>).MakeGenericType(configFile);

            Finalize = StaticMethod(Finalizer, "Finalize", database, files, importReport);
            Need(Finalize != null && Finalize.IsPublic, "Finalize");
            ModIsAvailable = StaticMethod(Finalizer, "ModIsAvailable", typeof(bool), typeof(string), typeof(string), typeof(HashSet<string>));
            Need(ModIsAvailable != null, "ModIsAvailable");
            CopyWithSatisfiedNeeds = StaticMethod(Finalizer, "CopyWithSatisfiedNeeds", MmNode, configNode, configFile,
                typeof(HashSet<string>), mmReport);
            Need(CopyWithSatisfiedNeeds != null, "CopyWithSatisfiedNeeds");
            ApplyNodeContents = StaticMethod(Finalizer, "ApplyNodeContents", typeof(void), MmNode, MmNode, mmReport);
            Need(ApplyNodeContents != null, "ApplyNodeContents");
            ApplyChildPatch = StaticMethod(Finalizer, "ApplyChildPatch", typeof(void), MmNode, MmNode, mmReport);
            Need(ApplyChildPatch != null, "ApplyChildPatch");
            NodeMatches = StaticMethod(Finalizer, "NodeMatches", typeof(bool), MmNode, NameDescriptor);
            Need(NodeMatches != null, "NodeMatches");
            ParseName = StaticMethod(NameDescriptor, "Parse", NameDescriptor, typeof(string));
            Need(ParseName != null, "NameDescriptor.Parse");

            MmValueCtor = MmValue?.GetConstructor(Instance, null, new[] { typeof(string), typeof(string) }, null);
            Need(MmValueCtor != null, "MmValue(string, string)");
            var valueList = MmValue == null ? null : typeof(List<>).MakeGenericType(MmValue);
            var nodeList = MmNode == null ? null : typeof(List<>).MakeGenericType(MmNode);
            NodeNameGetter = Getter(MmNode, "Name", typeof(string));
            Need(NodeNameGetter != null, "MmNode.Name");
            ValuesGetter = Getter(MmNode, "Values", valueList);
            Need(ValuesGetter != null, "MmNode.Values");
            NodesGetter = Getter(MmNode, "Nodes", nodeList);
            Need(NodesGetter != null, "MmNode.Nodes");
            ValueNameGetter = Getter(MmValue, "Name", typeof(string));
            Need(ValueNameGetter != null, "MmValue.Name");
            ValueRawGetter = Getter(MmValue, "RawValue", typeof(string));
            Need(ValueRawGetter != null, "MmValue.RawValue");
            Clone = MmNode?.GetMethod("Clone", Instance, null, Type.EmptyTypes, null);
            if (Clone != null && Clone.ReturnType != MmNode) Clone = null;
            Need(Clone != null, "MmNode.Clone");
            CommandGetter = Getter(NameDescriptor, "Command", PatchCommand);
            Need(CommandGetter != null, "NameDescriptor.Command");
            BaseNameGetter = Getter(NameDescriptor, "BaseName", typeof(string));
            Need(BaseNameGetter != null, "NameDescriptor.BaseName");

            if (MissingOf(ValueMembers).Any()) Need(false, "node accessors");
            else
            {
                try
                {
                    EmitValueAccessors(valueList);
                }
                catch (Exception e)
                {
                    Need(false, "node accessors");
                    KSP1PartModPlugin.Log?.LogWarning("[KSP1PartMod] mm: node accessors not emitted: " + e);
                }
            }

            if (MissingOf(NodeIndexMembers).Any()) Need(false, "name parser accessors");
            else
            {
                try
                {
                    EmitNameAccessors();
                }
                catch (Exception e)
                {
                    Need(false, "name parser accessors");
                    KSP1PartModPlugin.Log?.LogWarning("[KSP1PartMod] mm: name parser accessors not emitted: " + e);
                }
            }
        }

        private DynamicMethod NewMethod(string name, Type returnType, params Type[] parameters) =>
            new DynamicMethod("KSP1PartMod_Mm_" + name, returnType, parameters, Finalizer, true);

        private static void LoadArg(ILGenerator il, int index, Type target)
        {
            il.Emit(OpCodes.Ldarg, (short)index);
            if (target.IsValueType) il.Emit(OpCodes.Unbox_Any, target);
            else if (target != typeof(object)) il.Emit(OpCodes.Castclass, target);
        }

        private T EmitGetter<T>(MethodInfo getter, Type owner, Type returnAs) where T : class
        {
            var dm = NewMethod(getter.Name, returnAs, typeof(object));
            var il = dm.GetILGenerator();
            LoadArg(il, 0, owner);
            il.Emit(OpCodes.Callvirt, getter);
            if (getter.ReturnType.IsValueType && !returnAs.IsValueType) il.Emit(OpCodes.Box, getter.ReturnType);
            il.Emit(OpCodes.Ret);
            return (T)(object)dm.CreateDelegate(typeof(T));
        }

        private Func<object, int, string> ValueField(Type valueList, MethodInfo field)
        {
            var dm = NewMethod("Value" + field.Name, typeof(string), typeof(object), typeof(int));
            var il = dm.GetILGenerator();
            var local = il.DeclareLocal(MmValue);
            LoadArg(il, 0, MmNode);
            il.Emit(OpCodes.Callvirt, ValuesGetter);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, valueList.GetMethod("get_Item", new[] { typeof(int) }));
            il.Emit(OpCodes.Stloc, local);
            il.Emit(OpCodes.Ldloca, local);
            il.Emit(OpCodes.Call, field);
            il.Emit(OpCodes.Ret);
            return (Func<object, int, string>)dm.CreateDelegate(typeof(Func<object, int, string>));
        }

        private Action<object, object, object> StaticCall3(MethodInfo method)
        {
            var ps = method.GetParameters();
            var dm = NewMethod(method.Name, typeof(void), typeof(object), typeof(object), typeof(object));
            var il = dm.GetILGenerator();
            for (var i = 0; i < 3; i++) LoadArg(il, i, ps[i].ParameterType);
            il.Emit(OpCodes.Call, method);
            if (method.ReturnType != typeof(void)) il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            return (Action<object, object, object>)dm.CreateDelegate(typeof(Action<object, object, object>));
        }

        private void EmitValueAccessors(Type valueList)
        {
            Values = EmitGetter<Func<object, IList>>(ValuesGetter, MmNode, typeof(IList));
            Nodes = EmitGetter<Func<object, IList>>(NodesGetter, MmNode, typeof(IList));
            NodeName = EmitGetter<Func<object, string>>(NodeNameGetter, MmNode, typeof(string));
            ValueName = ValueField(valueList, ValueNameGetter);
            ValueRaw = ValueField(valueList, ValueRawGetter);

            var add = NewMethod("AddValue", typeof(void), typeof(object), typeof(string), typeof(string));
            var il = add.GetILGenerator();
            LoadArg(il, 0, MmNode);
            il.Emit(OpCodes.Callvirt, ValuesGetter);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Newobj, MmValueCtor);
            il.Emit(OpCodes.Callvirt, valueList.GetMethod("Add", new[] { MmValue }));
            il.Emit(OpCodes.Ret);
            AddValue = (Action<object, string, string>)add.CreateDelegate(typeof(Action<object, string, string>));

            // Through the original method: Harmony's detour runs, so our own prefixes see nested calls.
            CallApplyNodeContents = StaticCall3(ApplyNodeContents);
            CallApplyChildPatch = StaticCall3(ApplyChildPatch);
        }

        private void EmitNameAccessors()
        {
            var parse = NewMethod("Parse", typeof(object), typeof(string));
            var il = parse.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, ParseName);
            il.Emit(OpCodes.Ret);
            CallParseName = (Func<string, object>)parse.CreateDelegate(typeof(Func<string, object>));

            var matches = NewMethod("NodeMatches", typeof(bool), typeof(object), typeof(object));
            il = matches.GetILGenerator();
            LoadArg(il, 0, MmNode);
            LoadArg(il, 1, NameDescriptor);
            il.Emit(OpCodes.Call, NodeMatches);
            il.Emit(OpCodes.Ret);
            CallNodeMatches = (Func<object, object, bool>)matches.CreateDelegate(typeof(Func<object, object, bool>));

            CallClone = EmitGetter<Func<object, object>>(Clone, MmNode, typeof(object));
            CommandOf = EmitGetter<Func<object, int>>(CommandGetter, NameDescriptor, typeof(int));
            BaseNameOf = EmitGetter<Func<object, string>>(BaseNameGetter, NameDescriptor, typeof(string));

            foreach (var name in Enum.GetNames(PatchCommand))
                if (Enum.TryParse(name, false, out MmCommand ours))
                    Commands[Convert.ToInt32(Enum.Parse(PatchCommand, name))] = ours;
            foreach (var needed in new[] { MmCommand.None, MmCommand.Edit, MmCommand.CreateOrEdit, MmCommand.Copy, MmCommand.Delete })
                if (!Commands.ContainsValue(needed)) throw new InvalidOperationException("PatchCommand." + needed + " not found");
        }

        /// <summary>The command of a parsed Redux name, or null when its value is unknown to us.</summary>
        public MmCommand? Command(object descriptor) =>
            Commands.TryGetValue(CommandOf(descriptor), out var c) ? c : (MmCommand?)null;
    }
}
