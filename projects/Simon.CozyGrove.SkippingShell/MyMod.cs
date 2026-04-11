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
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }

                // 1. Check if user has a stone equipped manually
                Item stoneItem = avatar.activeItem;
                if (!IsSkippingStone(stoneItem))
                {
                    // Check if they even have any to remind them
                    Item stoneInInv = FindSkippingStone(avatar);
                    if (stoneInInv == null)
                    {
                        ShowBark(avatar, "No skipping stones found!");
                        yield return new WaitForSeconds(5f);
                    }
                    else
                    {
                        ShowBark(avatar, "Please equip a skipping stone!");
                        yield return new WaitForSeconds(2f);
                    }
                    continue;
                }

                MelonLogger.Msg($"Status: Using equipped stone '{stoneItem.configID.Value}'");

                // 2. Find nearest shell
                var target = FindNearestShell(avatar);
                if (target == null)
                {
                    MelonLogger.Msg("Status: No shells found.");
                    ShowBark(avatar, "No shells found.");
                    yield return new WaitForSeconds(5f);
                    continue;
                }

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
                    yield break;
                }

                MelonLogger.Msg($"Status: At distance {finalDist:F2} from shell");

                if (!_isActive) yield break;

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
            }
        }

        private bool IsAvatarBusy(AvatarController avatar)
        {
            if (avatar == null || avatar.actionsController == null) return true;
            if (GameUI.Instance.IsAnyModalUIOpen() || GameUI.Instance.InDialog()) return true;
            return avatar.actionsController.HasAnyActions();
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
                if (gaveRewards) return;

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
            Vector3 spawnPos = avatarPos + Vector3.up * 0.5f; // Lower spawn pos for better visual
            
            // Initial velocity direction towards target
            Vector3 velocity = (targetPos - avatarPos).normalized * 3f; 
            
            ThrownObject thrownObj;
            bool isBlocking;
            // First spawn the object using the native controller (handles inventory and item matching). Pass Vector3.zero to avoid stacking velocity with ThrowWithForce.
            if (avatar.ThrowObject(Vector3.zero, item, out thrownObj, out isBlocking, true, new Il2CppSystem.Nullable<Vector3>(spawnPos)))
            {
                if (thrownObj != null)
                {
                    // The "direction.y = 0" from the previous attempt caused the direction vector
                    // to completely lose its North/South capability in 2D space, throwing it completely to the side!
                    Vector3 direction = (targetPos - spawnPos).normalized;
                    
                    // We dynamically scale the speed based on the shell's distance. 
                    // This ensures it hits the water with enough force to skip 3-4 times appropriately without shooting off-screen.
                    float distance = Vector3.Distance(spawnPos, targetPos);
                    float speed = Mathf.Clamp(distance * 0.8f, 12f, 35f); 
                    
                    Vector3 dynamicVelocity = direction * speed;
                    
                    // Throw using velocity. `bounceToDestination` allows any internal skips to auto-target.
                    thrownObj.ThrowWithVelocity(dynamicVelocity, avatar, true, item, new Il2CppSystem.Nullable<Vector3>(spawnPos), true);
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
}
