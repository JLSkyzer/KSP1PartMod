using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KSP.OAB;
using KSP.Sim.Definitions;
using Redux.Ksp1Import.Assets;
using UnityEngine;

namespace KSP1PartMod.Game.Audit
{
    /// <summary>The game members the audit needs, resolved by name (Redux 26w42c). The public ones are checked so that a
    /// renamed member turns the audit off with one line instead of failing at the first VAB entry; the private and
    /// internal ones are kept for the calls.</summary>
    internal sealed class AuditTargets
    {
        public const string PatcherType = "PatchManager.Parts.Patchers.PartModuleLoadPatcher";
        public const string ShaderUtilsType = "Redux.Patching.ShaderUtils";
        public const string PrefabUtilsType = "Redux.Patching.PrefabUtils";

        public FieldInfo AllPossibleParts;     // ObjectAssemblyBuilder._allPossibleParts (IReadOnlyCollection<PartCore>)
        public MethodInfo GetOrCloneTemplateMethod;  // PrefabUtils.GetOrCloneTemplate(GameObject, string, out bool)
        public MethodInfo TryGetTemplateMethod;      // PrefabUtils.TryGetTemplate(string, out GameObject)
        public MethodInfo GetPrefabAddressMethod;    // PartModuleLoadPatcher.GetPrefabAddress(string), internal
        public MethodInfo ApplyOnGameObjectMethod;   // PartModuleLoadPatcher.ApplyOnGameObject(ref GameObject, PartData, bool), internal
        public MethodInfo HotswapPartShadersMethod;  // ShaderUtils.HotswapPartShaders(GameObject)
        public MethodInfo AssetsLoadMethod;    // AssetProvider.Load<GameObject>(string, Action<GameObject>, bool), the VAB's load
        public MethodInfo DestroyPrefabGraphMethod;  // Ksp1ImportedPartRegistry.DestroyPrefabGraph(GameObject, bool), private

        /// <summary>Every target, or null with the missing ones named (comma-separated).</summary>
        public static AuditTargets Resolve(out string missing)
        {
            var miss = new List<string>();
            var t = new AuditTargets();
            var builder = typeof(ObjectAssemblyBuilder);

            t.AllPossibleParts = AccessTools.Field(builder, "_allPossibleParts");
            if (t.AllPossibleParts == null || !typeof(IEnumerable<PartCore>).IsAssignableFrom(t.AllPossibleParts.FieldType))
                miss.Add("ObjectAssemblyBuilder._allPossibleParts");
            Field(builder, "BuilderAssets", typeof(ObjectAssemblyAssets), miss);
            Property(builder, "Stats", typeof(OABSessionInformation), miss);
            Field(builder, "eventsBuilder", typeof(ObjectAssemblyBuilderEvents), miss);
            Property(builder, "ActivePartTracker", typeof(ObjectAssemblyPartTracker), miss);
            Property(typeof(OABSessionInformation), "AvailableParts", null, miss);
            Property(typeof(OABSessionInformation), "AllKnownParts", null, miss);

            if (AccessTools.Constructor(typeof(OABPartData), new[] { typeof(PartCore) }) == null) miss.Add("OABPartData(PartCore)");
            Method(typeof(OABPartData), "CreateEditorPart", new[] { typeof(List<SerializedPartModule>), typeof(ObjectAssemblyAssets) }, miss);
            Property(typeof(OABPartData), "ModelPrefab", typeof(GameObject), miss);
            if (AccessTools.Constructor(typeof(ObjectAssemblyBuilderEvents), Type.EmptyTypes) == null) miss.Add("ObjectAssemblyBuilderEvents()");
            Field(typeof(ObjectAssemblyBuilderEvents), "IsLoadingVessel", typeof(bool), miss);
            Field(typeof(ObjectAssemblyBuilderEvents), "PostOnPartFinalized", typeof(Action<IObjectAssemblyPart>), miss);
            var part = typeof(ObjectAssemblyPart);
            Method(part, "FinalizeLoad", new[] { typeof(ObjectAssemblyBuilderEvents), typeof(IObjectAssemblyAvailablePart) }, miss);
            Property(part, "IsFinalized", typeof(bool), miss);
            Property(part, "UniqueId", null, miss);
            Property(part, "Modules", null, miss);

            var prefabUtils = AccessTools.TypeByName(PrefabUtilsType);
            if (prefabUtils == null) miss.Add(PrefabUtilsType);
            else
            {
                t.GetOrCloneTemplateMethod = AccessTools.Method(prefabUtils, "GetOrCloneTemplate",
                    new[] { typeof(GameObject), typeof(string), typeof(bool).MakeByRefType() });
                if (t.GetOrCloneTemplateMethod == null || t.GetOrCloneTemplateMethod.ReturnType != typeof(GameObject)) miss.Add("PrefabUtils.GetOrCloneTemplate");
                t.TryGetTemplateMethod = AccessTools.Method(prefabUtils, "TryGetTemplate", new[] { typeof(string), typeof(GameObject).MakeByRefType() });
                if (t.TryGetTemplateMethod == null || t.TryGetTemplateMethod.ReturnType != typeof(bool)) miss.Add("PrefabUtils.TryGetTemplate");
            }

            var patcher = AccessTools.TypeByName(PatcherType);
            if (patcher == null) miss.Add(PatcherType);
            else
            {
                t.GetPrefabAddressMethod = AccessTools.Method(patcher, "GetPrefabAddress", new[] { typeof(string) });
                if (t.GetPrefabAddressMethod == null || t.GetPrefabAddressMethod.ReturnType != typeof(string)) miss.Add("PartModuleLoadPatcher.GetPrefabAddress");
                t.ApplyOnGameObjectMethod = AccessTools.Method(patcher, "ApplyOnGameObject",
                    new[] { typeof(GameObject).MakeByRefType(), typeof(PartData), typeof(bool) });
                if (t.ApplyOnGameObjectMethod == null) miss.Add("PartModuleLoadPatcher.ApplyOnGameObject");
            }

            var shaders = AccessTools.TypeByName(ShaderUtilsType);
            t.HotswapPartShadersMethod = shaders == null ? null : AccessTools.Method(shaders, "HotswapPartShaders", new[] { typeof(GameObject) });
            if (t.HotswapPartShadersMethod == null) miss.Add("ShaderUtils.HotswapPartShaders");

            // By reflection: the overload set of AssetProvider.Load names Addressables types we do not reference.
            foreach (var m in typeof(KSP.Assets.AssetProvider).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != "Load" || !m.IsGenericMethodDefinition) continue;
                var ps = m.GetParameters();
                if (ps.Length == 3 && ps[0].ParameterType == typeof(string) && ps[2].ParameterType == typeof(bool))
                {
                    t.AssetsLoadMethod = m.MakeGenericMethod(typeof(GameObject));
                    break;
                }
            }
            if (t.AssetsLoadMethod == null) miss.Add("AssetProvider.Load<T>(string, Action<T>, bool)");

            var registry = typeof(Ksp1ImportedPartRegistry);
            Method(registry, nameof(Ksp1ImportedPartRegistry.TryGetPrefab), new[] { typeof(string), typeof(GameObject).MakeByRefType() }, miss);
            Method(registry, nameof(Ksp1ImportedPartRegistry.GetPrefabKey), new[] { typeof(string) }, miss);
            Method(registry, nameof(Ksp1ImportedPartRegistry.IsImportedPart), new[] { typeof(string) }, miss);
            Property(registry, nameof(Ksp1ImportedPartRegistry.ImportedParts), null, miss);
            Property(registry, nameof(Ksp1ImportedPartRegistry.IsComplete), typeof(bool), miss);
            t.DestroyPrefabGraphMethod = AccessTools.Method(registry, "DestroyPrefabGraph", new[] { typeof(GameObject), typeof(bool) });
            if (t.DestroyPrefabGraphMethod == null) miss.Add("Ksp1ImportedPartRegistry.DestroyPrefabGraph");
            var entry = typeof(Ksp1ImportedPartRegistry.Entry);
            var prefab = AccessTools.Property(entry, "Prefab");
            if (prefab == null || prefab.PropertyType != typeof(GameObject) || prefab.GetSetMethod() == null) miss.Add("Ksp1ImportedPartRegistry.Entry.Prefab");
            Property(entry, "ModName", typeof(string), miss);
            Property(entry, "PartName", typeof(string), miss);

            Property(typeof(PartBehaviourModule), "IsInitialized", typeof(bool), miss);

            missing = string.Join(", ", miss);
            return miss.Count == 0 ? t : null;
        }

        private static void Field(Type type, string name, Type fieldType, List<string> miss)
        {
            var f = AccessTools.Field(type, name);
            if (f == null || (fieldType != null && f.FieldType != fieldType)) miss.Add(type.Name + "." + name);
        }

        private static void Property(Type type, string name, Type propertyType, List<string> miss)
        {
            var p = AccessTools.Property(type, name);
            if (p == null || (propertyType != null && p.PropertyType != propertyType)) miss.Add(type.Name + "." + name);
        }

        private static void Method(Type type, string name, Type[] args, List<string> miss)
        {
            if (AccessTools.Method(type, name, args) == null) miss.Add(type.Name + "." + name);
        }

        // ---- calls ----

        public bool FindTemplate(string key, out GameObject template)
        {
            var args = new object[] { key, null };
            var found = (bool)TryGetTemplateMethod.Invoke(null, args);
            template = args[1] as GameObject;
            return found && template != null;
        }

        public GameObject CloneTemplate(GameObject prefab, string key, out bool created)
        {
            var args = new object[] { prefab, key, false };
            var template = (GameObject)GetOrCloneTemplateMethod.Invoke(null, args);
            created = (bool)args[2];
            return template;
        }

        public string PrefabAddress(string partName) => (string)GetPrefabAddressMethod.Invoke(null, new object[] { partName });

        /// <summary>As <c>OnPartPrefabLoaded</c> does on a new template (<c>replacePrefab: false</c>); the method may swap
        /// the reference, which is returned.</summary>
        public GameObject ApplyPatcher(GameObject template, PartData data)
        {
            var args = new object[] { template, data, false };
            ApplyOnGameObjectMethod.Invoke(null, args);
            return args[0] as GameObject ?? template;
        }

        public void Hotswap(GameObject template) => HotswapPartShadersMethod.Invoke(null, new object[] { template });

        public void LoadAsset(object assets, string address, Action<GameObject> done) =>
            AssetsLoadMethod.Invoke(assets, new object[] { address, done, true });

        public void DestroyGraph(GameObject prefab, bool destroyTextures) =>
            DestroyPrefabGraphMethod.Invoke(null, new object[] { prefab, destroyTextures });
    }
}
