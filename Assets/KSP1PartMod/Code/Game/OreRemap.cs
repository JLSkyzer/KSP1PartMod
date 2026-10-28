using System;
using System.Collections.Generic;
using HarmonyLib;
using KSP1PartMod.Native;
using Redux.Ksp1Import.Modules;

namespace KSP1PartMod.Game
{
    /// <summary>KSP1 Ore → a native resource (<c>ksp1_ore_resource</c>, plan (g)). Redux maps Ore to <c>TestRocks</c>, which
    /// has no density map on any body. At pre-initialization, before Redux's import, the <c>Ore</c> entry of Redux's own
    /// table (<c>Ksp1ResourceMapper.DirectResourceMap</c>) is set in memory to the chosen resource, so tanks, B9 tank types,
    /// converter inputs and drill outputs all agree. No cfg is touched and no method is patched (a dictionary entry, not a
    /// Harmony patch on a method the JIT may inline).
    /// <para>The target is checked against the game's resource database once it holds definitions: at the first
    /// <c>Ksp1PartModuleImporterRegistry</c> (built by Redux's translator before any part, through a postfix of ours) or the
    /// first converter import, whichever comes first. An unknown target restores Redux's value (KSP1 drills on Ore are then
    /// skipped by the mapping rule). A missing table keeps Redux's mapping. Never throws into the load.</para></summary>
    internal static class OreRemap
    {
        public const string HarmonyId = "KSP1PartMod.OreRemap";
        public const string Key = "Ore";

        private static readonly object Lock = new object();
        private static IDictionary<string, string> _map;
        private static string _previous, _target;
        private static bool _verified;

        /// <summary>Tests: replaces the game's database check (true: the resource exists). Null: the game's database, and no
        /// check while it is not there.</summary>
        internal static Func<string, bool> ExistsOverride;

        /// <summary>The resource Ore is mapped to by us, or null when the remap is not active.</summary>
        internal static string Active
        {
            get { lock (Lock) return _map == null ? null : _target; }
        }

        /// <summary>Sets <c>Ore</c> to <paramref name="target"/> in Redux's table (<c>null</c>: Redux's mapping kept, nothing
        /// done). Returns true when the remap is active.</summary>
        public static bool Arm(Harmony harmony, string target)
        {
            if (string.IsNullOrEmpty(target)) return false;
            IDictionary<string, string> map;
            try
            {
                map = IsruAccess.ResourceMap();
            }
            catch (Exception e)
            {
                NativeModules.Error(NativeLines.Prefix + "ore: Redux's resource table not read: " + e);
                map = null;
            }
            if (map == null || !map.TryGetValue(Key, out var previous))
            {
                NativeModules.Warn(IsruLines.OreTableNotFound);
                return false;
            }
            lock (Lock)
            {
                if (_map == null) _previous = previous;
                map[Key] = target;
                _map = map;
                _target = target;
                _verified = false;
            }
            NativeModules.Info(IsruLines.OreImported(target));
            try
            {
                var ctor = AccessTools.Constructor(typeof(Ksp1PartModuleImporterRegistry), Type.EmptyTypes);
                if (ctor != null) harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(OreRemap), nameof(AfterRegistryConstructor)));
            }
            catch (Exception e)
            {
                // The importers still check it at their first call.
                NativeModules.Error(NativeLines.Prefix + "ore: check at the first registry not armed, done at the first converter: " + e);
            }
            return true;
        }

        private static void AfterRegistryConstructor()
        {
            try
            {
                EnsureVerified();
            }
            catch (Exception e)
            {
                NativeModules.Error(NativeLines.Prefix + "ore: target check failed: " + e);
            }
        }

        /// <summary>Once the resource database is there: an unknown target restores Redux's mapping. Idempotent.</summary>
        internal static void EnsureVerified()
        {
            string target;
            lock (Lock)
            {
                if (_map == null || _verified) return;
                target = _target;
            }
            bool exists;
            if (ExistsOverride != null) exists = ExistsOverride(target);
            else
            {
                var db = IsruAccess.Database();
                if (db == null) return; // too early, or outside the game: checked later
                exists = IsruAccess.Exists(db, target);
            }
            lock (Lock)
            {
                if (_map == null || _verified) return;
                _verified = true;
                if (exists) return;
                RestoreLocked();
            }
            NativeModules.Warn(IsruLines.OreNotAResource(target));
        }

        private static void RestoreLocked()
        {
            if (_map == null) return;
            _map[Key] = _previous;
            _map = null;
        }

        /// <summary>Tests: Redux's value back, and the state cleared.</summary>
        internal static void Reset()
        {
            lock (Lock)
            {
                RestoreLocked();
                _previous = _target = null;
                _verified = false;
            }
            ExistsOverride = null;
        }
    }
}
