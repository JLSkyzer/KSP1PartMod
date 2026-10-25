using System;
using System.Linq;
using System.Reflection;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>Registers our module types with the game's <c>Unity.Entities.TypeManager</c>.
    /// <para>Redux puts each module behaviour (OAB) and each component module (flight) on an ECS entity with
    /// <c>EntityManager.AddComponentObject</c> (<c>Redux.Ecs.ModuleEntities.Create</c>). Its own types are in the
    /// generated <c>AssemblyTypeRegistry</c> of Ksp2Redux.dll, read once by <c>TypeManager.Initialize</c>; a mod
    /// assembly loaded later is not, and every part carrying our module failed to load in the VAB (in game,
    /// 2026-10-25: "Unknown Type: …Module_Ksp1Animations").</para>
    /// <para>From the IL of Unity.Entities (game build, 2026-10-25): <c>GetOrCreateTypeIndex(Type)</c> creates the
    /// index of a UnityEngine.Object type and only looks up any other; <c>GetOrCreateTypeIndexUnsafe(Type)</c> builds
    /// any type (for a class implementing IComponentData: a managed component, 4 bytes in chunk) and appends it to
    /// the tables. <c>Data_Ksp1Animations</c> needs nothing: base <c>ModuleData.BindToEntity</c> only adds
    /// <c>ModuleRequestEntity</c>, and we do not override it.</para>
    /// <para>Main thread only (load step and plugin callbacks). Once a lookup fails the state is Failed for the
    /// session, and the load step adds no module: a part without our module is better than a broken one.</para></summary>
    internal static class EcsTypeRegistration
    {
        public enum State { Pending, Registered, Failed }

        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>The types Redux hands to AddComponentObject for our module, in registration order.</summary>
        public static readonly Type[] ModuleTypes = { typeof(Module_Ksp1Animations), typeof(PartComponentModule_Ksp1Animations) };

        public static State Status { get; private set; } = State.Pending;

        private static Action<string> _info, _error;

        /// <summary>Idempotent: registers what is missing once the TypeManager is initialized. True when every type
        /// has an index. Never throws. info and error default to the plugin's log.</summary>
        public static bool TryRegister(string when, Action<string> info = null, Action<string> error = null)
        {
            if (Status == State.Registered) return true;
            if (Status == State.Failed) return false;
            _info = info ?? (m => KSP1PartModPlugin.Log?.LogInfo(m));
            _error = error ?? (m => KSP1PartModPlugin.Log?.LogError(m));
            try
            {
                var api = TypeManagerApi.Find(out var missing);
                if (api == null) return Fail($"Unity.Entities.TypeManager.{missing} not found");
                if (!api.IsInitialized())
                {
                    _info($"[KSP1PartMod] anim: ECS TypeManager not initialized yet ({when}), registration deferred");
                    return false;
                }
                foreach (var type in ModuleTypes)
                {
                    if (api.TryGetIndex(type, out var existing))
                    {
                        _info($"[KSP1PartMod] anim: ECS type {type.Name} already registered as index {existing}");
                        continue;
                    }
                    var how = "UnityEngine.Object";
                    if (typeof(UnityEngine.Object).IsAssignableFrom(type)) api.GetOrCreate(type);
                    else how = "managed component, " + api.CreateUnsafe(type);
                    if (!api.TryGetIndex(type, out var index))
                        return Fail($"{type.Name} still has no ECS type index after registration");
                    _info($"[KSP1PartMod] anim: registered ECS type {type.Name} as index {index} ({when}, {how})");
                }
                Status = State.Registered;
                return true;
            }
            catch (Exception e)
            {
                return Fail("registration threw: " + (e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e));
            }
        }

        private static bool Fail(string why)
        {
            Status = State.Failed;
            _error($"[KSP1PartMod] anim: ECS registration failed, KSP1 animation modules disabled for this session: {why}");
            return false;
        }

        /// <summary>Tests only: forget the session state.</summary>
        internal static void ResetForTests() => Status = State.Pending;

        /// <summary>The internal TypeManager members we use, found once by reflection.</summary>
        internal sealed class TypeManagerApi
        {
            private PropertyInfo _isInitialized;
            private MethodInfo _tryGet, _getOrCreate, _createUnsafe, _initShared, _shutdownShared;
            private PropertyInfo _indexValue;

            public static TypeManagerApi Find(out string missing)
            {
                missing = null;
                var tm = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => a.GetName().Name == "Unity.Entities")
                    .Select(a => a.GetType("Unity.Entities.TypeManager", false))
                    .FirstOrDefault(t => t != null);
                if (tm == null) { missing = "(type)"; return null; }

                var api = new TypeManagerApi { _isInitialized = tm.GetProperty("IsInitialized", AnyStatic) };
                if (api._isInitialized == null) { missing = "IsInitialized"; return null; }
                api._tryGet = tm.GetMethods(AnyStatic).FirstOrDefault(m => m.Name == "TryGetTypeIndex" && Params(m, 2)
                    && m.GetParameters()[0].ParameterType == typeof(Type) && m.GetParameters()[1].IsOut);
                if (api._tryGet == null) { missing = "TryGetTypeIndex(Type, out TypeIndex)"; return null; }
                api._getOrCreate = tm.GetMethod("GetOrCreateTypeIndex", AnyStatic, null, new[] { typeof(Type) }, null);
                if (api._getOrCreate == null) { missing = "GetOrCreateTypeIndex(Type)"; return null; }
                api._createUnsafe = tm.GetMethod("GetOrCreateTypeIndexUnsafe", AnyStatic, null, new[] { typeof(Type) }, null);
                if (api._createUnsafe == null) { missing = "GetOrCreateTypeIndexUnsafe(Type)"; return null; }
                // Optional: pointer refresh, and the guarded path of builds with collection checks (the editor).
                api._initShared = tm.GetMethod("InitializeSharedStatics", AnyStatic, null, Type.EmptyTypes, null);
                api._shutdownShared = tm.GetMethod("ShutdownSharedStatics", AnyStatic, null, Type.EmptyTypes, null);
                // TypeIndex.Index: the index without flags; ToString() if it is gone.
                api._indexValue = api._tryGet.GetParameters()[1].ParameterType.GetElementType()?.GetProperty("Index");
                return api;
            }

            private static bool Params(MethodInfo m, int n) => m.GetParameters().Length == n;

            public bool IsInitialized() => (bool)_isInitialized.GetValue(null);

            public bool TryGetIndex(Type type, out string index)
            {
                var args = new object[] { type, null };
                var found = (bool)_tryGet.Invoke(null, args);
                index = found ? Describe(args[1]) : null;
                return found;
            }

            public void GetOrCreate(Type type) => _getOrCreate.Invoke(null, new object[] { type });

            /// <summary>Builds a non-UnityEngine.Object type. The game's build (no collection checks) appends it
            /// directly; the tables have a fixed capacity of 8192 types and a managed component adds to no list whose
            /// pointer Burst caches, but the cached pointers are set again afterwards anyway (plain assignments, no
            /// null window). A build with collection checks refuses while those pointers are set and asks for a
            /// ShutdownSharedStatics / InitializeSharedStatics pair, used then. Returns how it was done.</summary>
            public string CreateUnsafe(Type type)
            {
                try
                {
                    _createUnsafe.Invoke(null, new object[] { type });
                    _initShared?.Invoke(null, null);
                    return _initShared != null ? "direct, shared statics refreshed" : "direct";
                }
                catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException
                                                          && e.InnerException.Message.Contains("shared statics")
                                                          && _shutdownShared != null && _initShared != null)
                {
                    _shutdownShared.Invoke(null, null);
                    try { _createUnsafe.Invoke(null, new object[] { type }); }
                    finally { _initShared.Invoke(null, null); }
                    return "inside a shared statics shutdown/initialize pair";
                }
            }

            private string Describe(object typeIndex)
            {
                if (typeIndex == null) return "?";
                try { return _indexValue != null ? Convert.ToString(_indexValue.GetValue(typeIndex)) : typeIndex.ToString(); }
                catch (Exception) { return typeIndex.ToString(); }
            }
        }
    }
}
