using System.Collections.Generic;

namespace KSP1PartMod.Animation.Runtime
{
    /// <summary>Spec 2b.1: an entry closed by a B9 gate shows its rest pose (time 0) once, through KSP1's hold-pose idiom
    /// (state enabled at time 0, weight 1, speed 0 for one frame, then disabled). A legacy state that an active entry of
    /// the part also holds (two entries planned on one clip, kept apart by their gates) is left to that entry: its pose
    /// wins, and sampling the rest there would fight it.</summary>
    public static class RestPlan
    {
        /// <summary>Per item of <paramref name="mine"/>: true when the inactive entry samples its rest on it, that is when
        /// it is not null and no array of <paramref name="activeOthers"/> holds the same object (by reference).</summary>
        public static bool[] Unshared<T>(T[] mine, IReadOnlyList<T[]> activeOthers) where T : class
        {
            var result = new bool[mine?.Length ?? 0];
            for (var i = 0; i < result.Length; i++)
            {
                var s = mine[i];
                if (s == null) continue;
                result[i] = true;
                if (activeOthers == null) continue;
                foreach (var other in activeOthers)
                {
                    if (other == null) continue;
                    foreach (var o in other)
                        if (ReferenceEquals(o, s))
                        {
                            result[i] = false;
                            break;
                        }
                    if (!result[i]) break;
                }
            }
            return result;
        }
    }
}
