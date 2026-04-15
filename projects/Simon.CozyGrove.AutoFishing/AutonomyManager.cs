using Il2Cpp;
using UnityEngine;

namespace Simon.CozyGrove.AutoFishing
{
    // Lightweight bid/lock system that coordinates multiple autonomous mods
    // acting on the same avatar, avoiding conflicts without any game-side API.
    public static class AutonomyManager
    {
        public static void AcquireLock(AvatarController avatar, string modName)
        {
            if (avatar == null || avatar.transform == null) return;
            if (avatar.transform.Find($"AutonomyLock_{modName}") == null)
            {
                var l = new GameObject($"AutonomyLock_{modName}");
                l.transform.SetParent(avatar.transform);
            }
        }

        public static void ReleaseLock(AvatarController avatar, string modName)
        {
            if (avatar == null || avatar.transform == null) return;
            var l = avatar.transform.Find($"AutonomyLock_{modName}");
            if (l != null) UnityEngine.Object.Destroy(l.gameObject);
        }

        public static bool IsLockedByAnother(AvatarController avatar, string modName)
        {
            if (avatar == null || avatar.transform == null) return false;
            for (int i = 0; i < avatar.transform.childCount; i++)
            {
                var n = avatar.transform.GetChild(i).name;
                if (n.StartsWith("AutonomyLock_") && n != $"AutonomyLock_{modName}") return true;
            }
            return false;
        }

        public static void UpdateBid(AvatarController avatar, string modName, float distance)
        {
            if (avatar == null || avatar.transform == null) return;
            ClearBid(avatar, modName);
            if (distance < float.MaxValue)
            {
                var b = new GameObject($"AutonomyBid_{modName}_{distance.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
                b.transform.SetParent(avatar.transform);
            }
        }

        public static void ClearBid(AvatarController avatar, string modName)
        {
            if (avatar == null || avatar.transform == null) return;
            for (int i = avatar.transform.childCount - 1; i >= 0; i--)
            {
                var n = avatar.transform.GetChild(i).name;
                if (n.StartsWith($"AutonomyBid_{modName}_"))
                    UnityEngine.Object.Destroy(avatar.transform.GetChild(i).gameObject);
            }
        }

        public static bool IsMyBidLowest(AvatarController avatar, string modName, float myDistance)
        {
            if (avatar == null || avatar.transform == null) return true;
            for (int i = 0; i < avatar.transform.childCount; i++)
            {
                var n = avatar.transform.GetChild(i).name;
                if (n.StartsWith("AutonomyBid_") && !n.StartsWith($"AutonomyBid_{modName}_"))
                {
                    var parts = n.Split('_');
                    if (parts.Length >= 3 && float.TryParse(
                            parts[2],
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out float otherDist))
                    {
                        if (otherDist < myDistance) return false;
                        if (otherDist == myDistance && string.Compare(parts[1], modName) < 0) return false;
                    }
                }
            }
            return true;
        }
    }
}
