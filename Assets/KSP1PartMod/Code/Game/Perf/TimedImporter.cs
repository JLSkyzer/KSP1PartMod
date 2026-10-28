using System;
using System.Diagnostics;
using KSP.Sim.Definitions;
using KSP1PartMod.Perf;
using Redux.Ksp1Import.Config;
using Redux.Ksp1Import.Modules;

namespace KSP1PartMod.Game.Perf
{
    /// <summary>One of our native importers, timed (plan Task 3): <see cref="TryImport"/> forwards to the inner importer
    /// unchanged (its result, its out module, and any exception, which propagates as thrown) and adds the time to
    /// <see cref="PerfPhase.OurImporters"/>. Only built while the load timeline is armed.</summary>
    internal sealed class TimedImporter : IKsp1PartModuleImporter
    {
        public TimedImporter(IKsp1PartModuleImporter inner)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IKsp1PartModuleImporter Inner { get; }

        public bool TryImport(Ksp1ConfigNode moduleNode, Ksp1PartImportContext context, out SerializedPartModule serializedModule)
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                return Inner.TryImport(moduleNode, context, out serializedModule);
            }
            finally
            {
                PerfClock.NoteTicks(PerfPhase.OurImporters, Stopwatch.GetTimestamp() - start);
            }
        }
    }
}
