using System;
using KSP.Modules;
using KSP.Sim.Definitions;
using Redux.Modules;
using UnityEngine;

namespace KSP1PartMod.Game.Animation
{
    /// <summary>The running state of the part's native converter, drill or scanner (converters and drills plan, Task 4), for
    /// a <c>ModuleAnimationGroup</c>'s active clip (<c>isru_active_animation</c>). Read only:
    /// <list type="bullet">
    /// <item><c>Data_Mine.status</c> is <c>MineState.Operational</c>;</item>
    /// <item><c>Data_ResourceConverter.conversionState</c> is <c>Operational</c> or <c>OperationalLow</c>;</item>
    /// <item><c>Data_ResourceScanner.status</c> is <c>Scanning</c>.</item>
    /// </list>
    /// The module data is taken from the module's <c>DataModules</c> on the first read that finds it (a module of the same
    /// part may initialize after ours). <see cref="Running"/> is bound once and does not allocate.</summary>
    internal sealed class NativeActivity
    {
        private readonly PartBehaviourModule _module;
        private Data_Mine _mine;
        private Data_ResourceConverter _converter;
        private Data_ResourceScanner _scanner;

        /// <summary><c>Module_Mine</c>, <c>Module_ResourceConverter</c> or <c>Module_ResourceScanner</c>.</summary>
        public string NativeModule { get; }

        /// <summary>The bound reader (one delegate per instance, made once).</summary>
        public Func<bool> Running { get; }

        private NativeActivity(PartBehaviourModule module, string nativeModule)
        {
            _module = module;
            NativeModule = nativeModule;
            Running = Read;
        }

        /// <summary>Tests: a reader over the data itself.</summary>
        internal NativeActivity(Data_Mine mine) : this(null, nameof(Module_Mine)) => _mine = mine;
        internal NativeActivity(Data_ResourceConverter converter) : this(null, nameof(Module_ResourceConverter)) => _converter = converter;
        internal NativeActivity(Data_ResourceScanner scanner) : this(null, nameof(Module_ResourceScanner)) => _scanner = scanner;

        /// <summary>The first native ISRU module on <paramref name="host"/>'s GameObject: a drill, else a converter, else a
        /// scanner. Null when there is none.</summary>
        public static NativeActivity Find(Component host)
        {
            if (host == null) return null;
            var mine = host.GetComponent<Module_Mine>();
            if (mine != null) return new NativeActivity(mine, nameof(Module_Mine));
            var converter = host.GetComponent<Module_ResourceConverter>();
            if (converter != null) return new NativeActivity(converter, nameof(Module_ResourceConverter));
            var scanner = host.GetComponent<Module_ResourceScanner>();
            return scanner != null ? new NativeActivity(scanner, nameof(Module_ResourceScanner)) : null;
        }

        private bool Read()
        {
            if (_mine != null) return _mine.status == MineState.Operational;
            if (_converter != null)
                return _converter.conversionState == ResourceConversionState.Operational ||
                       _converter.conversionState == ResourceConversionState.OperationalLow;
            if (_scanner != null) return _scanner.status == ResourceScannerStatus.Scanning;
            if (_module == null) return false;
            var data = _module.DataModules;
            if (data == null) return false;
            // Until the native module has added its data (once found, the branches above answer).
            switch (NativeModule)
            {
                case nameof(Module_Mine): return data.TryGetByType(out _mine) && _mine != null && Read();
                case nameof(Module_ResourceConverter): return data.TryGetByType(out _converter) && _converter != null && Read();
                default: return data.TryGetByType(out _scanner) && _scanner != null && Read();
            }
        }
    }
}
