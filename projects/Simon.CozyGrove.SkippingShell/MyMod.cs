using Il2Cpp;
using MelonLoader;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Il2CppSpryFox.Common;


namespace Simon.CozyGrove.SkippingShell
{
    public class MyMod : MelonMod
    {
        private bool _isActive = false;
        private object _coroInstance = null;
        private AvatarController _cachedAvatar = null;
        private bool _isInGameScene = false;
        private Dictionary<StoneSkippingTarget, float> _targetIgnoreUntil = new();

        private const int MaxWalkingSteps = 10;

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _isInGameScene = (sceneName == "Game");
            _cachedAvatar = null;
            _targetIgnoreUntil.Clear();
            if (!_isInGameScene)
            {
                ResetState();
            }
        }

        private AvatarController GetAvatar()
        {
            if (_cachedAvatar == null && _isInGameScene)
            {
                _cachedAvatar = UnityEngine.Object.FindObjectOfType<AvatarController>();
            }
            return _cachedAvatar;
        }

        public override void OnUpdate()
        {
            if (!_isInGameScene) return;

            var avatar = GetAvatar();
            if (avatar == null) return;

            // Toggle logic (Ctrl+S)
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.S))
            {
                _isActive = !_isActive;
                if (!_isActive)
                {
                    ResetState();
                }
                MelonLogger.Msg($"SkippingShell is now {(_isActive ? "ON" : "OFF")}");
                ShowBark(avatar, $"SkippingShell: {(_isActive ? "ON" : "OFF")}");
            }

            if (!_isActive) return;

            if (_coroInstance == null)
            {
                _coroInstance = MelonCoroutines.Start(SkippingShellCoro());
            }
        }

        private void ResetState()
        {
            if (_coroInstance != null)
            {
                MelonCoroutines.Stop(_coroInstance);
                _coroInstance = null;
            }
            if (_cachedAvatar != null)
            {
                AutonomyManager.ClearBid(_cachedAvatar, "SkippingShell");
                AutonomyManager.ReleaseLock(_cachedAvatar, "SkippingShell");
            }
        }

        private IEnumerator SkippingShellCoro()
        {
            while (true)
            {
                if (!_isActive || !_isInGameScene)
                {
                    yield return new WaitForSeconds(1f);
                    continue;
                }

                var avatar = GetAvatar();
                if (avatar == null)
                {
                    yield return new WaitForSeconds(1f);
                    continue;
                }

                if (IsAvatarBusy(avatar))
                {
                    AutonomyManager.ClearBid(avatar, "SkippingShell");
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }

                // 1. Find a skipping stone in inventory
                Item stoneItem = null;
                if (IsStoneEquipped(avatar, out Item equippedStone))
                {
                    stoneItem = equippedStone;
                }
                else
                {
                    stoneItem = FindSkippingStone(avatar);
                }

                if (stoneItem == null)
                {
                    ShowBark(avatar, "No skipping stones found!");
                    yield return new WaitForSeconds(5f);
                    continue;
                }

                MelonLogger.Msg($"Status: Using stone '{stoneItem.configID.Value}'");

                // 2. Find nearest shell
                var target = FindNearestShell(avatar);
                if (target == null)
                {
                    AutonomyManager.ClearBid(avatar, "SkippingShell");
                    MelonLogger.Msg("Status: No shells found.");
                    ShowBark(avatar, "No shells found.");
                    yield return new WaitForSeconds(5f);
                    continue;
                }

                float targetDist = Vector3.Distance(avatar.transform.position, target.transform.position);
                AutonomyManager.UpdateBid(avatar, "SkippingShell", targetDist);
                
                yield return new WaitForSeconds(0.2f);
                
                if (avatar == null || !avatar.gameObject.activeInHierarchy || 
                    IsAvatarBusy(avatar) || 
                    !AutonomyManager.IsMyBidLowest(avatar, "SkippingShell", targetDist))
                {
                    AutonomyManager.ClearBid(avatar, "SkippingShell");
                    continue;
                }

                AutonomyManager.ClearBid(avatar, "SkippingShell");
                AutonomyManager.AcquireLock(avatar, "SkippingShell");

                MelonLogger.Msg($"Status: Targeting shell at {target.transform.position}");
                ShowBark(avatar, "Found shell! Preparing...");

                // Calculate Max Skips
                int maxSkips = 1;
                try
                {
                    var config = ThrownObject.GetConfig(stoneItem);
                    if (config != null && config.skipAmounts != null)
                    {
                        foreach (var pair in config.skipAmounts)
                        {
                            if (pair.skips > maxSkips) maxSkips = pair.skips;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"Could not calculate max skips: {ex.Message}");
                }

                float dynamicMaxRange = Mathf.Max(35f, maxSkips * 40f);
                MelonLogger.Msg($"Stone Max Skips: {maxSkips}, Calculated Max Range: {dynamicMaxRange}m");

                // 3. Move toward the shell
                // Re-evaluate distance fresh each step; walk 5 steps max
                int step = 0;
                while (step < MaxWalkingSteps && _isActive)
                {
                    float d = Vector3.Distance(avatar.transform.position, target.transform.position);
                    // Walk until we are within 15m from the target
                    if (d <= 15f) break;

                    ShowBark(avatar, "Walking to shore...");
                    Vector3 dir = (target.transform.position - avatar.transform.position).normalized;
                    Vector3 stepPos = avatar.transform.position + dir * Mathf.Min(15f, d - 8f);
                    MelonLogger.Msg($"Step {step}: walking toward ({stepPos.x:F1},{stepPos.y:F1}), dist={d:F2}");
                    avatar.WalkToPosition(stepPos, true, false, 10.0f, null);

                    // Wait for walking to complete with timeout
                    // Walk logic
                    
                    // Wait a frame to let WalkToPosition take effect
                    yield return null;
                    while (avatar.actionsController.HasAnyActions()) yield return null;

                    float dAfter = Vector3.Distance(avatar.transform.position, target.transform.position);
                    if (Mathf.Abs(d - dAfter) < 0.2f) 
                    {
                        MelonLogger.Msg("Avatar stalled (hit shore/cliff). Will attempt throw from here.");
                        break; // Progress stopped, probably hit a constraint
                    }

                    step++;
                }

                // Make sure we are at least within safe range
                float finalDist = Vector3.Distance(avatar.transform.position, target.transform.position);
                if (finalDist > dynamicMaxRange)
                {
                    MelonLogger.Msg($"Shell still too far ({finalDist:F1}m > {dynamicMaxRange}m) - ignoring for 30s");
                    ShowBark(avatar, "Too far to reach!");
                    _targetIgnoreUntil[target] = Time.time + 30f;
                    AutonomyManager.ReleaseLock(avatar, "SkippingShell");
                    yield break;
                }

                MelonLogger.Msg($"Status: At distance {finalDist:F2} from shell");

                if (!_isActive) 
                {
                    AutonomyManager.ReleaseLock(avatar, "SkippingShell");
                    yield break;
                }

                // 4. Use native throwing action
                MelonLogger.Msg("Status: Throwing!");
                ShowBark(avatar, "Throwing!");
                yield return ThrowWithNativeAction(avatar, stoneItem, target);

                // IMPORTANT: Prevent recursive throwing at the same shell if we miss or it takes time to process!
                _targetIgnoreUntil[target] = Time.time + 30f;

                // Wait for rewards
                yield return new WaitForSeconds(3f);
                while (IsAvatarBusy(avatar)) yield return null;

                CollectNearbyDoobers(avatar);
                yield return new WaitForSeconds(1f);
                
                AutonomyManager.ReleaseLock(avatar, "SkippingShell");
            }
        }

        private bool IsAvatarBusy(AvatarController avatar)
        {
            if (avatar == null || avatar.actionsController == null) return true;
            if (GameUI.Instance.IsAnyModalUIOpen() || GameUI.Instance.InDialog()) return true;
            if (AutonomyManager.IsLockedByAnother(avatar, "SkippingShell")) return true;
            return avatar.actionsController.HasAnyActions();
        }

        private bool IsStoneEquipped(AvatarController avatar, out Item equippedStone)
        {
            equippedStone = null;

            // 1. Check avatar.activeItem
            if (avatar.activeItem != null && IsSkippingStone(avatar.activeItem))
            {
                equippedStone = avatar.activeItem;
                return true;
            }

            // 2. Check avatar.inventory.ActiveItem
            if (avatar.inventory != null && avatar.inventory.ActiveItem != null && IsSkippingStone(avatar.inventory.ActiveItem))
            {
                equippedStone = avatar.inventory.ActiveItem;
                return true;
            }

            // 3. Scan inventory for 'equipped' flag
            if (avatar.inventory != null)
            {
                var slots = avatar.inventory.slots;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i]?.item != null && slots[i].item.equipped && IsSkippingStone(slots[i].item))
                    {
                        equippedStone = slots[i].item;
                        return true;
                    }
                }
            }

            // 4. Diagnostic: Reflection check for private usingItem
            try
            {
                var usingItemField = typeof(AvatarController).GetField("usingItem", BindingFlags.NonPublic | BindingFlags.Instance);
                if (usingItemField != null)
                {
                    var val = usingItemField.GetValue(avatar);
                    if (val != null)
                    {
                        Item hiddenItem = val as Item;
                        if (hiddenItem != null && IsSkippingStone(hiddenItem))
                        {
                            MelonLogger.Msg($"[Diagnostic] Found stone in private 'usingItem' field: {hiddenItem.collectableItemConfig.id}");
                            equippedStone = hiddenItem;
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex) { MelonLogger.Msg($"[Diagnostic] Reflection failed: {ex.Message}"); }

            return false;
        }

        private bool IsSkippingStone(Item item)
        {
            if (item == null) return false;
            string id = item.configID.Value;

            // Try tag-based approach
            if (item.collectableItemConfig != null)
            {
                try
                {
                    Tag skippableTag = Tag.Create(CollectableItemConfig.TAG_SKIPPABLE);
                    if (item.collectableItemConfig.tags.Has(skippableTag)) return true;
                }
                catch { }
            }

            // Fallback: match by ID substring
            return id.IndexOf("skipping_stone", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private Item FindSkippingStone(AvatarController avatar)
        {
            if (avatar.inventory == null) return null;

            foreach (var slot in avatar.inventory.slots)
            {
                if (slot == null || slot.item == null) continue;

                string id = slot.item.configID.Value;

                // Try tag-based approach first
                if (slot.item.collectableItemConfig != null)
                {
                    try
                    {
                        Tag skippableTag = Tag.Create(CollectableItemConfig.TAG_SKIPPABLE);
                        if (slot.item.collectableItemConfig.tags.Has(skippableTag))
                        {
                            return slot.item;
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Msg($"  [Tag check failed]: {ex.Message}");
                    }
                }

                // Fallback: match by ID substring
                if (id.IndexOf("skipping_stone", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return slot.item;
                }
            }
            return null;
        }

        private StoneSkippingTarget FindNearestShell(AvatarController avatar)
        {
            StoneSkippingTarget best = null;
            float bestDistSq = float.MaxValue;
            Vector3 pos = avatar.transform.position;

            var gaveRewardsField = typeof(StoneSkippingTarget).GetField("gaveRewards", BindingFlags.Instance | BindingFlags.NonPublic);

            Action<StoneSkippingTarget> checkTarget = (target) =>
            {
                if (target == null) return;
                // Skip temporarily ignored targets
                if (_targetIgnoreUntil.ContainsKey(target) && Time.time < _targetIgnoreUntil[target])
                    return;
                bool gaveRewards = false;
                if (gaveRewardsField != null)
                {
                    try { gaveRewards = (bool)gaveRewardsField.GetValue(target); }
                    catch { }
                }
                if (gaveRewards) 
                {
                    // If target already gave rewards, ignore it permanently from this session
                    _targetIgnoreUntil[target] = float.MaxValue;
                    return;
                }

                float d2 = (target.transform.position - pos).sqrMagnitude;
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    best = target;
                }
            };

            var targets = StoneSkippingTarget.allTargets;
            if (targets != null && targets.Count > 0)
            {
                MelonLogger.Msg($"Shell list has {targets.Count} target(s).");
                foreach (var t in targets) checkTarget(t);
            }

            if (best == null)
            {
                var found = UnityEngine.Object.FindObjectsOfType<StoneSkippingTarget>();
                if (found != null && found.Length > 0)
                {
                    MelonLogger.Msg($"Found {found.Length} shell(s) via scene scan.");
                    foreach (var t in found) checkTarget(t);
                }
            }

            if (best != null) MelonLogger.Msg($"Nearest shell: {best.name} at distance {Mathf.Sqrt(bestDistSq):F2}");
            else MelonLogger.Msg("No shells found in scene.");

            return best;
        }


        private IEnumerator ThrowWithNativeAction(AvatarController avatar, Item item, StoneSkippingTarget target)
        {
            Vector3 targetPos = target.transform.position;
            Vector3 avatarPos = avatar.transform.position;

            // Face target
            Vector3 lookDir = targetPos - avatarPos;
            lookDir.y = 0;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                avatar.animationController.SetFacingDirection(lookDir);
                avatar.SetHorizontalFlipping(lookDir.x < 0);
            }

            // Set up throwing animation for a brief moment
            var throwAction = new AvatarActionThrowing(avatar, item);
            avatar.actionsController.Add(throwAction.Cast<IAvatarAction>());
            yield return new WaitForSeconds(0.5f);

            avatar.actionsController.CancelAll();

            // Native throw mechanism
            // Use Z-axis for vertical height in isometric view.
            // Set to 1.0m to provide a balanced "drop-in" arc that clears the lip but remains controllable.
            Vector3 spawnPos = avatarPos + (lookDir.normalized * 0.6f);
            spawnPos.z += 1.0f; 
            
            ThrownObject thrownObj;
            bool isBlocking;
            // First spawn the object using the native controller (handles inventory and item matching).
            if (avatar.ThrowObject(Vector3.zero, item, out thrownObj, out isBlocking, true, new Il2CppSystem.Nullable<Vector3>(spawnPos)))
            {
                if (thrownObj != null)
                {
                    // Set the target destination explicitly to assist the internal skip logic
                    try
                    {
                        var endPosField = typeof(ThrownObject).GetField("throwEndPosition", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (endPosField != null)
                        {
                            endPosField.SetValue(thrownObj, targetPos);
                        }
                    }
                    catch { }

                    // Calculate direction directly in the isometric ground plane (XY)
                    Vector3 direction = (targetPos - spawnPos).normalized;

                    // Balanced Trajectory Tuning:
                    // Previous 1.62x hit the side; 3.5x was too strong.
                    // We target ~14.8 speed for 16m throws using a 1.68x multiplier.
                    float distance = Vector3.Distance(spawnPos, targetPos);
                    float gravity = Mathf.Abs(UnityEngine.Physics.gravity.y);
                    if (gravity < 1f) gravity = 9.81f; 
                    
                    var config = ThrownObject.GetConfig(item);
                    float mass = (config != null) ? config.mass : 1.0f;
                    
                    // Base physics speed (un-mass-compensated for the primary arc)
                    float speed = Mathf.Clamp(1.68f * Mathf.Sqrt(distance * gravity), 8f, 55f); 
                    
                    // Discover the skip table for diagnostics
                    string tableStr = "";
                    float minSkipForce = 20f;
                    if (config != null && config.skipAmounts != null)
                    {
                        foreach (var pair in config.skipAmounts)
                        {
                            tableStr += $"[{pair.force:F2}:{pair.skips}] ";
                            if (pair.skips >= 1 && pair.force < minSkipForce) minSkipForce = pair.force;
                        }
                    }
                    
                    // Safety check: ensure we at least hit the skip threshold
                    float minSkipSpeed = (minSkipForce / mass) * 1.1f;
                    speed = Mathf.Max(speed, minSkipSpeed);
                    
                    Vector3 dynamicVelocity = direction * speed;
                    Vector3 estimatedForce = dynamicVelocity * mass;

                    string stoneId = (item.collectableItemConfig != null) ? item.collectableItemConfig.id : "Unknown";
                    MelonLogger.Msg($"[Debug] Stone: {stoneId}, Mass: {mass:F2}, SkipTable: {tableStr}");
                    MelonLogger.Msg($"[Debug] TargetForce: {estimatedForce.magnitude:F2}, MinSkipForce: {minSkipForce:F2}");
                    
                    // Predict skip count using the Force estimate
                    int expectedSkips = 0;
                    try 
                    {
                        var method = typeof(ThrownObject).GetMethod("ExpectedSkips", BindingFlags.NonPublic | BindingFlags.Static);
                        if (method != null) expectedSkips = (int)method.Invoke(null, new object[] { estimatedForce, item });
                    } catch {}
                    
                    MelonLogger.Msg($"[Throw] Dist: {distance:F2}m, Speed: {speed:F2}, Expected Skips: {expectedSkips}");

                    // Back to ThrowWithVelocity
                    thrownObj.ThrowWithVelocity(dynamicVelocity, avatar, false, item, new Il2CppSystem.Nullable<Vector3>(spawnPos), true);
                }
            }
            else
            {
                MelonLogger.Msg("Warning: Avatar.ThrowObject failed!");
            }
        }

        private void CollectNearbyDoobers(AvatarController avatar)
        {
            var doobers = UnityEngine.Object.FindObjectsOfType<Doober>();
            foreach (var doober in doobers)
            {
                if (doober != null && Vector3.Distance(avatar.transform.position, doober.transform.position) < 12f)
                {
                    doober.Pickup();
                }
            }
        }

        private void ShowBark(AvatarController avatar, string text)
        {
            if (avatar != null && avatar.speechBubble != null)
            {
                avatar.speechBubble.Show(text, SpriteInfo.Invalid, 2.5f);
            }
        }
    }

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
                {
                    UnityEngine.Object.Destroy(avatar.transform.GetChild(i).gameObject);
                }
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
                    if (parts.Length >= 3 && float.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float otherDist))
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
