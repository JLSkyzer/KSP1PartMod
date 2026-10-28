using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using KSP.Sim.Definitions;
using KSP1PartMod.Animation.Cfg;
using KSP1PartMod.Game.Animation;
using KSP1PartMod.Native;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;
using Redux.Modules;

namespace KSP1PartMod.Game
{
    /// <summary>KSP1 scanners → Redux's <c>Module_ResourceScanner</c>, one per part (plan (j), <see cref="ScannerMapping"/>):
    /// <list type="bullet">
    /// <item><see cref="SurveyorImporter"/> (<c>ksp1_survey_scanners</c>): <c>ModuleOrbitalSurveyor</c>, KSP1's body-wide
    /// survey, with the native scanner's resources, power and inclination window. <c>ModuleOrbitalScanner</c> on the part is
    /// counted and noted (no importer of ours);</item>
    /// <item><see cref="ResourceScannerImporter"/> (<c>ksp1_resource_scanners</c>): <c>ModuleResourceScanner</c> with
    /// <c>RequiresUnlock = false</c>; every such scanner of the part is read at its first call and the kept resources share
    /// one module. With <c>RequiresUnlock</c> (or another scanner type) the part gets none (noted, counted);</item>
    /// <item>a part with both: the surveyor wins (noted), whatever the module order; a part that already holds a
    /// <c>Data_ResourceScanner</c> gets nothing more (noted).</item>
    /// </list>
    /// Any failure leaves the part as Redux 0.9.0 imports it (the module dropped, nothing of ours left on the part).</summary>
    internal static class ScannerImporters
    {
        private static readonly object Lock = new object();
        private static readonly Dictionary<string, Stats> PartStats = new Dictionary<string, Stats>(StringComparer.Ordinal);
        private static bool _summaryLogged;

        /// <summary>Tests: called after Redux's <c>CreateSerialized</c>, before the scanner is counted (a throw = a failure).</summary>
        internal static Action<string> FaultForTests;

        internal sealed class Stats
        {
            public bool Surveyor, Resource;
            public int RequiresUnlock, NoScannable;
        }

        internal static List<string> MissingMembers() => new List<string>(IsruAccess.Get().MissingScanner);

        internal static List<Ksp1ConfigNode> Nodes(Ksp1ConfigNode partNode, string module) =>
            (partNode?.GetNodes("MODULE") ?? Enumerable.Empty<Ksp1ConfigNode>())
            .Where(n => n != null && string.Equals(n.GetValue("name")?.Trim(), module, StringComparison.OrdinalIgnoreCase))
            .ToList();

        /// <summary>One entry per part and scanner kind (a part with both keeps both counts).</summary>
        internal static void SetStats(string part, bool surveyor, Stats stats)
        {
            lock (Lock) PartStats[(surveyor ? "surveyor|" : "resource|") + part] = stats;
        }

        /// <summary>Builds the native scanner from <paramref name="m"/> and counts it. False (rolled back) on any failure.</summary>
        internal static bool Build(string part, ScannerMapping m, Ksp1PartImportContext context, string ksp1Module, bool surveyor, Stats stats,
            out SerializedPartModule serializedModule)
        {
            serializedModule = default;
            var added = -1;
            try
            {
                var data = new Data_ResourceScanner
                {
                    RequiredResources = m.RequiredResources.Select(x => new PartModuleResourceSetting { ResourceName = x.Name, Rate = (float)x.Rate }).ToList(),
                    ScannableResources = m.ScannableResources.Select(n => new PartModuleResourceSetting { ResourceName = n }).ToList(),
                    TimeToComplete = (float)m.TimeToComplete,
                    minAltitude = m.MinAltitude,
                    maxAltitude = m.MaxAltitude,
                    minInclination = m.MinInclination,
                    maxInclination = m.MaxInclination,
                };
                added = context.ModuleData.Count;
                serializedModule = NativeModules.CreateSerialized(typeof(Module_ResourceScanner), data, context);
                var line = IsruLines.Scanner(part, m);
                FaultForTests?.Invoke(part);
                var record = NativeModules.Record;
                record.AddConversion(part, ScannerMapping.NativeModule);
                foreach (var field in m.NotReproduced) record.Flag(part, "scanner:nr:" + field);
                NativeModules.Isru.AddScanner(part);
                SetStats(part, surveyor, stats);
                added = -1;
                try
                {
                    if (record.AddNote(part, line)) NativeModules.Info(line);
                    foreach (var note in m.Notes) NativeModules.Note(part, note);
                }
                catch (Exception e)
                {
                    NativeModules.Error(NativeLines.Prefix + part + ": scanner lines not logged: " + e.Message);
                }
                return true;
            }
            catch (Exception e)
            {
                if (added >= 0)
                    while (context.ModuleData.Count > added) context.ModuleData.RemoveAt(context.ModuleData.Count - 1);
                serializedModule = default;
                NativeModules.ImporterFailed(part, ksp1Module, e);
                return false;
            }
        }

        /// <summary>The <c>native: scanners:</c> line, once per session whichever scanner converter asks first.</summary>
        internal static IEnumerable<string> SummaryLines()
        {
            List<Stats> all;
            lock (Lock)
            {
                if (_summaryLogged) yield break;
                _summaryLogged = true;
                all = PartStats.Values.ToList();
            }
            yield return IsruLines.ScannersSummary(all.Count(s => s.Surveyor), all.Count(s => s.Resource),
                all.Sum(s => s.RequiresUnlock), all.Sum(s => s.NoScannable));
        }

        internal static void ResetSession()
        {
            lock (Lock)
            {
                PartStats.Clear();
                _summaryLogged = false;
            }
            FaultForTests = null;
            SurveyorImporter.ResetSession();
            ResourceScannerImporter.ResetSession();
        }
    }

    /// <summary><c>ModuleOrbitalSurveyor</c> → <c>Module_ResourceScanner</c> (<c>ksp1_survey_scanners</c>).</summary>
    internal sealed class SurveyorImporter : IKsp1PartModuleImporter
    {
        private static readonly object Lock = new object();
        private static ConditionalWeakTable<Ksp1PartImportContext, object> _seen = new ConditionalWeakTable<Ksp1PartImportContext, object>();

        public bool TryImport(Ksp1ConfigNode moduleNode, Ksp1PartImportContext context, out SerializedPartModule serializedModule)
        {
            serializedModule = default;
            var part = context?.PartName ?? "?";
            try
            {
                if (context == null || moduleNode == null) return false;
                lock (Lock)
                {
                    if (_seen.TryGetValue(context, out _)) return false; // one surveyor module per part
                    _seen.Add(context, Lock);
                }
                var reference = IsruReference.Get();
                if (context.ModuleData.Any(d => d is Data_ResourceScanner))
                {
                    NativeModules.Note(part, ScannerMapping.SurveyorModule + ": " + IsruLines.ExistingScannerNote);
                    return false;
                }
                var m = ScannerMapping.Surveyor(new Ksp1NodeAdapter(moduleNode), reference.Scanner);
                var orbital = ScannerImporters.Nodes(context.PartNode, ScannerMapping.OrbitalScannerModule).Count;
                var stats = new ScannerImporters.Stats { Surveyor = m.HasModule };
                if (!m.HasModule)
                {
                    stats.NoScannable = 1;
                    ScannerImporters.SetStats(part, true, stats);
                    foreach (var note in m.Notes) NativeModules.Note(part, note);
                    return false;
                }
                if (!ScannerImporters.Build(part, m, context, ScannerMapping.SurveyorModule, true, stats, out serializedModule)) return false;
                if (orbital > 0) NativeModules.Note(part, IsruLines.OrbitalScannerFolded(orbital));
                return true;
            }
            catch (Exception e)
            {
                serializedModule = default;
                NativeModules.ImporterFailed(part, ScannerMapping.SurveyorModule, e);
                return false;
            }
        }

        internal static void ResetSession()
        {
            lock (Lock) _seen = new ConditionalWeakTable<Ksp1PartImportContext, object>();
        }
    }

    /// <summary><c>ModuleResourceScanner</c> (<c>RequiresUnlock = false</c>) → <c>Module_ResourceScanner</c>
    /// (<c>ksp1_resource_scanners</c>).</summary>
    internal sealed class ResourceScannerImporter : IKsp1PartModuleImporter
    {
        private static readonly object Lock = new object();
        private static ConditionalWeakTable<Ksp1PartImportContext, object> _seen = new ConditionalWeakTable<Ksp1PartImportContext, object>();

        public bool TryImport(Ksp1ConfigNode moduleNode, Ksp1PartImportContext context, out SerializedPartModule serializedModule)
        {
            serializedModule = default;
            var part = context?.PartName ?? "?";
            try
            {
                if (context == null || moduleNode == null) return false;
                lock (Lock)
                {
                    if (_seen.TryGetValue(context, out _)) return false; // the part's scanners were read at its first call
                    _seen.Add(context, Lock);
                }
                var reference = IsruReference.Get();

                // Every resource scanner of the part, the called node in place of its entry (B9's scratch re-import).
                var raw = ScannerImporters.Nodes(context.PartNode, ScannerMapping.ResourceScannerModule);
                var nodes = raw.Select(n => (IKsp1Node)new Ksp1NodeAdapter(n)).ToList();
                var at = raw.FindIndex(n => ReferenceEquals(n, moduleNode));
                if (at < 0)
                {
                    var called = new Ksp1NodeAdapter(moduleNode);
                    var resource = IsruCfg.Value(called, "ResourceName") ?? "";
                    at = nodes.FindIndex(n => (IsruCfg.Value(n, "ResourceName") ?? "") == resource);
                    if (at >= 0) nodes[at] = called;
                    else nodes.Add(called);
                }

                var db = ConverterImporter.CurrentDatabase();
                var r = ConverterImporter.Resources(db, IsruReference.Minable());
                var maps = nodes.Select(n => ScannerMapping.Resource(n, r, reference.Scanner)).ToList();
                var stats = new ScannerImporters.Stats
                {
                    RequiresUnlock = maps.Count(m => m.LeftOut == ScannerLeftOut.RequiresUnlock),
                    NoScannable = maps.Count(m => m.LeftOut == ScannerLeftOut.NoScannableResource || m.LeftOut == ScannerLeftOut.ScannerType),
                };
                var kept = maps.Where(m => m.HasModule).ToList();
                foreach (var m in maps.Where(m => !m.HasModule))
                    foreach (var note in m.Notes) NativeModules.Note(part, note);

                // The surveyor wins only when it builds its scanner (the same pure mapping its importer runs).
                var surveyorBuilds = NativeModules.IsConverterArmed(ScannerMapping.SurveyorModule) &&
                                     ScannerImporters.Nodes(context.PartNode, ScannerMapping.SurveyorModule)
                                         .Any(n => ScannerMapping.Surveyor(new Ksp1NodeAdapter(n), reference.Scanner).HasModule);
                if (kept.Count > 0 && surveyorBuilds)
                {
                    // One native scanner per part: the surveyor's, whatever the module order.
                    ScannerImporters.SetStats(part, false, stats);
                    NativeModules.Note(part, IsruLines.SurveyorWinsNote);
                    return false;
                }
                if (kept.Count > 0 && context.ModuleData.Any(d => d is Data_ResourceScanner))
                {
                    ScannerImporters.SetStats(part, false, stats);
                    NativeModules.Note(part, ScannerMapping.ResourceScannerModule + ": " + IsruLines.ExistingScannerNote);
                    return false;
                }
                if (kept.Count == 0)
                {
                    ScannerImporters.SetStats(part, false, stats);
                    return false;
                }
                var merged = ScannerMapping.MergeResources(kept);
                stats.Resource = true;
                return ScannerImporters.Build(part, merged, context, ScannerMapping.ResourceScannerModule, false, stats, out serializedModule);
            }
            catch (Exception e)
            {
                serializedModule = default;
                NativeModules.ImporterFailed(part, ScannerMapping.ResourceScannerModule, e);
                return false;
            }
        }

        internal static void ResetSession()
        {
            lock (Lock) _seen = new ConditionalWeakTable<Ksp1PartImportContext, object>();
        }
    }
}
