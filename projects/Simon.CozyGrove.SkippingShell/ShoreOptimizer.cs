using Il2Cpp;
using UnityEngine;
using MelonLoader;
using Il2CppPathfinding;

namespace Simon.CozyGrove.SkippingShell
{
    public static class ShoreOptimizer
    {
        private const int MaxSamples = 20;

        public static Vector3 FindOptimalShorePosition(AvatarController avatar, StoneSkippingTarget target, Item stone)
        {
            Vector3 targetPos = target.transform.position;
            Vector3 avatarPos = avatar.transform.position;
            
            float maxSkipForce = 20f;
            float mass = 1.0f;
            int skipCount = 0;
            var config = ThrownObject.GetConfig(stone);
            if (config != null)
            {
                mass = config.mass;
                if (config.skipAmounts != null)
                {
                    skipCount = config.skipAmounts.Count;
                    foreach (var pair in config.skipAmounts)
                    {
                        if (pair.force > maxSkipForce) maxSkipForce = pair.force;
                    }
                }
            }

            return FindOptimalShorePosition(avatarPos, targetPos, mass, skipCount);
        }

        public static Vector3 FindOptimalShorePosition(Vector3 currentPos, Vector3 targetPos, float mass, int skipAmounts)
        {
            Vector3 toAvatar = (currentPos - targetPos).normalized;
            
            // 1. Find the water's edge in the direction of the avatar
            Vector3 waterEdge = FindWaterEdge(targetPos, toAvatar, 100f);
            float distToEdge = Vector3.Distance(targetPos, waterEdge);
            
            // 2. Calculate ideal throw distance based on skips
            // Typical skip in this game is roughly 10-15m. 
            // We want to be at a distance where we can utilize all skips.
            float idealDistFromEdge = Mathf.Clamp(skipAmounts * 0.75f, 1.0f, 3.0f); 
            float targetDist = distToEdge + idealDistFromEdge;
            
            Vector3 idealPos = targetPos + toAvatar * targetDist;
            
            // 3. Ensure idealPos is actually on land, or find closest land
            if (IsOnLand(idealPos))
            {
                MelonLogger.Msg($"[ShoreOptimizer] Optimal spot found at {idealPos} (Dist to edge: {idealDistFromEdge:F1}m)");
                return idealPos;
            }

            // Fallback: If ideal spot is somehow invalid, stay at the edge but slightly back
            Vector3 fallbackPos = waterEdge + toAvatar * 2.0f;
            MelonLogger.Msg($"[ShoreOptimizer] Ideal spot invalid, using fallback near edge: {fallbackPos}");
            return fallbackPos;
        }

        private static Vector3 FindWaterEdge(Vector3 targetPos, Vector3 direction, float maxDist)
        {
            // Binary search or stepped search from shell towards avatar to find where land starts
            Vector3 lastWater = targetPos;
            for (float d = 1f; d < maxDist; d += 1.0f)
            {
                Vector3 testPos = targetPos + direction * d;
                if (IsOnLand(testPos))
                {
                    // Found land! Refine bit more
                    for (float sub = 0.9f; sub >= 0.1f; sub -= 0.1f)
                    {
                        if (!IsOnLand(targetPos + direction * (d - sub)))
                        {
                            return targetPos + direction * (d - sub + 0.1f);
                        }
                    }
                    return testPos;
                }
            }
            return targetPos; // Should not happen if avatar is on land
        }

        private static bool IsOnLand(Vector3 position)
        {
            if (AstarPath.active == null) return true;
            
            var nnInfo = AstarPath.active.GetNearest(position, NNConstraint.Default);
            if (nnInfo.node == null) return false;
            
            return nnInfo.node.Walkable;
        }

    }
}
