using System.Text;

namespace KSP1PartMod.Animation.Mu
{
    /// <summary>A compact text form of a <see cref="MuFile"/>'s tree, for comparing two readers or two loads.</summary>
    public static class MuTreeSignature
    {
        /// <summary>One line per object, depth first: path, child count, component entry types in file order, and
        /// for an Animation its clips as <c>name:curveCount</c>. Lines are separated by '\n'.</summary>
        public static string Of(MuFile f)
        {
            var sb = new StringBuilder();
            Append(sb, f.Root);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, MuNode node)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(node.Path()).Append(" children=").Append(node.Children.Count)
                .Append(" entries=[").Append(string.Join(",", node.EntryTypes)).Append(']');
            if (node.Animation != null)
            {
                sb.Append(" clips=[");
                for (var i = 0; i < node.Animation.Clips.Count; i++)
                {
                    var clip = node.Animation.Clips[i];
                    if (i > 0) sb.Append(',');
                    sb.Append(clip.Name).Append(':').Append(clip.Curves.Count);
                }
                sb.Append(']');
            }
            foreach (var child in node.Children) Append(sb, child);
        }
    }
}
