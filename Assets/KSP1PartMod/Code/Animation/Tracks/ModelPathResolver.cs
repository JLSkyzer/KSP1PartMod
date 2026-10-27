using System;
using System.Collections.Generic;
using System.IO;

namespace KSP1PartMod.Animation.Tracks
{
    /// <summary>Every file under a folder, by file name, ignoring case. Built once per load, on the worker.</summary>
    public sealed class DirectoryIndex
    {
        private static readonly IReadOnlyList<string> None = new string[0];
        private readonly Dictionary<string, List<string>> _byName;

        private DirectoryIndex(Dictionary<string, List<string>> byName) => _byName = byName;

        /// <summary>An empty index when <paramref name="root"/> does not exist. Never throws: a folder that cannot be
        /// listed (access denied, removed meanwhile, path too long) is left out, with what lies under it, so rule 5
        /// works with the rest.</summary>
        public static DirectoryIndex Build(string root) => Build(root, Directory.GetFiles, Directory.GetDirectories);

        /// <summary><see cref="Build(string)"/> with the listing calls given (test seam).</summary>
        internal static DirectoryIndex Build(string root, Func<string, IEnumerable<string>> filesOf, Func<string, IEnumerable<string>> dirsOf)
        {
            var byName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return new DirectoryIndex(byName);
            }
            catch (Exception) { return new DirectoryIndex(byName); }

            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var dir = pending.Pop();
                foreach (var path in List(filesOf, dir))
                {
                    var name = Path.GetFileName(path);
                    if (!byName.TryGetValue(name, out var list)) byName[name] = list = new List<string>(1);
                    list.Add(path);
                }
                foreach (var sub in List(dirsOf, dir)) pending.Push(sub);
            }
            return new DirectoryIndex(byName);
        }

        /// <summary>The listing, read in full here so that a lazy enumeration fails inside the catch; empty on a
        /// file-system error.</summary>
        private static List<string> List(Func<string, IEnumerable<string>> listing, string dir)
        {
            try { return new List<string>(listing(dir) ?? new string[0]); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException
                                      || e is System.Security.SecurityException || e is ArgumentException
                                      || e is NotSupportedException)
            {
                return new List<string>(0);
            }
        }

        public IReadOnlyList<string> ByFileName(string name) =>
            name != null && _byName.TryGetValue(name, out var list) ? list : None;
    }

    /// <summary>Finds the <c>.mu</c> file a KSP1 cfg names, by Redux 26w42c's rules, in its order
    /// (<c>Ksp1ModelImporter.ResolveModelPath</c>): 1 <c>&lt;root&gt;/&lt;ref&gt;.mu</c>; 2 <c>&lt;cfg dir&gt;/&lt;ref&gt;</c>;
    /// 3 <c>&lt;cfg basename&gt;.mu</c> in that folder for <c>model.mu</c>; 4 the only <c>.mu</c> of that folder; 5 a
    /// file name unique under the root (never <c>model.mu</c>). A <c>mesh</c> value is relative to the cfg folder and
    /// only goes through rules 2 to 4, as in Redux.</summary>
    public sealed class ModelPathResolver
    {
        private const string ModelMu = "model.mu";
        private readonly string _root;
        private readonly Lazy<DirectoryIndex> _index;

        /// <param name="index">Called at most once, the first time rule 5 is needed.</param>
        public ModelPathResolver(string gameDataRoot, Func<DirectoryIndex> index)
        {
            _root = gameDataRoot;
            _index = new Lazy<DirectoryIndex>(index ?? (() => DirectoryIndex.Build(gameDataRoot)));
        }

        /// <summary>The full path of the model, or null. <paramref name="tried"/> lists the rule numbers tried, the
        /// last one being the one that matched, e.g. <c>1,2,3,4,5 (name in 2 folders)</c>.</summary>
        public string Resolve(string cfgPath, string reference, bool isMeshValue, out string tried)
        {
            tried = "";
            if (string.IsNullOrWhiteSpace(reference)) return null;
            var rel = reference.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var cfgDir = Path.GetDirectoryName(cfgPath) ?? "";
            if (isMeshValue && !Path.IsPathRooted(rel)) rel = Path.Combine(cfgDir, rel);
            if (!Path.HasExtension(rel)) rel += ".mu";

            var steps = new List<string>(5);
            string found;
            if (Path.IsPathRooted(rel))
            {
                steps.Add("2");
                found = File.Exists(rel) ? rel : Fallback(cfgPath, rel, steps);
                tried = string.Join(",", steps);
                return found;
            }

            steps.Add("1");
            var atRoot = Path.Combine(_root, rel);
            if (File.Exists(atRoot)) { tried = "1"; return atRoot; }
            steps.Add("2");
            var atCfg = Path.Combine(cfgDir, rel);
            found = File.Exists(atCfg) ? atCfg : Fallback(cfgPath, atCfg, steps) ?? UniqueByName(Path.GetFileName(rel), steps);
            tried = string.Join(",", steps);
            return found;
        }

        /// <summary>Rules 3 and 4, on the folder of the missing path.</summary>
        private static string Fallback(string cfgPath, string missing, List<string> steps)
        {
            steps.Add("3");
            var dir = Path.GetDirectoryName(missing);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) { steps.Add("4"); return null; }
            if (Path.GetFileName(missing).Equals(ModelMu, StringComparison.OrdinalIgnoreCase))
            {
                var byCfg = Path.Combine(dir, Path.GetFileNameWithoutExtension(cfgPath) + ".mu");
                if (File.Exists(byCfg)) return byCfg;
            }
            steps.Add("4");
            var mus = Directory.GetFiles(dir, "*.mu", SearchOption.TopDirectoryOnly);
            return mus.Length == 1 ? mus[0] : null;
        }

        /// <summary>Rule 5.</summary>
        private string UniqueByName(string fileName, List<string> steps)
        {
            if (string.IsNullOrWhiteSpace(_root) || string.IsNullOrWhiteSpace(fileName)
                || fileName.Equals(ModelMu, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(_root))
                return null;
            var hits = _index.Value.ByFileName(fileName);
            if (hits.Count == 1) { steps.Add("5"); return hits[0]; }
            steps.Add(hits.Count == 0 ? "5 (name nowhere)" : $"5 (name in {hits.Count} folders)");
            return null;
        }
    }
}
