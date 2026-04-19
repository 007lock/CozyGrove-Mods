using Il2Cpp;
using UnityEngine;
using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Simon.CozyGrove.SkippingShell
{
    public class SkippingController
    {
        private bool _isActive;
        private Dictionary<StoneSkippingTarget, float> _targetIgnoreUntil = new();
        private const int MaxWalkingSteps = 10;
        private const int MaxPossibleSkips = 4;
        private const float Gravity = 9.81f;
        private const float BasicSpeedMultiplier = 1.68f;
        private const float ProSpeedMultiplier = 1.42f;
        private const float SpecialSpeedMultiplier = 1.30f;

        public SkippingController(bool active)
        {
            _isActive = active;
        }

        public void SetActive(bool active)
        {
            _isActive = active;
            if (!active) _targetIgnoreUntil.Clear();
        }

        public IEnumerator SkippingShellCoro(MyMod mod, Func<AvatarController> getAvatar)
        {
            while (true)
            {
                if (!_isActive || !mod.IsInGameScene)
                {
                    yield return new WaitForSeconds(1f);
                    continue;
                }

                var avatar = getAvatar();
                if (avatar == null)
                {
                    yield return new WaitForSeconds(1f);
                    continue;
                }

                if (ModHelpers.IsAvatarBusy(avatar))
                {
                    AutonomyManager.ClearBid(avatar, "SkippingShell");
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }

                Item stoneItem = null;
                if (ModHelpers.IsStoneEquipped(avatar, out Item equippedStone))
                {
                    stoneItem = equippedStone;
                }
                else
                {
                    stoneItem = ModHelpers.FindSkippingStone(avatar);
                }

                if (stoneItem == null)
                {
                    ModHelpers.ShowBark(avatar, "No skipping stones found!");
                    yield return new WaitForSeconds(5f);
                    continue;
                }

                var target = FindNearestShell(avatar);
                if (target == null)
                {
                    AutonomyManager.ClearBid(avatar, "SkippingShell");
                    yield return new WaitForSeconds(5f);
                    continue;
                }

                float targetDist = Vector3.Distance(avatar.transform.position, target.transform.position);
                AutonomyManager.UpdateBid(avatar, "SkippingShell", targetDist);
                yield return new WaitForSeconds(0.2f);
                
                if (avatar == null || !avatar.gameObject.activeInHierarchy || 
                    ModHelpers.IsAvatarBusy(avatar) || 
                    !AutonomyManager.IsMyBidLowest(avatar, "SkippingShell", targetDist))
                {
                    AutonomyManager.ClearBid(avatar, "SkippingShell");
                    continue;
                }

                AutonomyManager.ClearBid(avatar, "SkippingShell");
                AutonomyManager.AcquireLock(avatar, "SkippingShell");

                int maxSkips = 1;
                var config = ThrownObject.GetConfig(stoneItem);
                if (config != null && config.skipAmounts != null)
                {
                    foreach (var pair in config.skipAmounts)
                    {
                        if (pair.skips > maxSkips) maxSkips = pair.skips;
                    }
                }
                maxSkips = Mathf.Clamp(maxSkips, 0, MaxPossibleSkips);

                float dynamicMaxRange = Mathf.Max(35f, maxSkips * 40f);
                Vector3 optimalPos = ShoreOptimizer.FindOptimalShorePosition(avatar, target, stoneItem);

                int step = 0;
                while (step < MaxWalkingSteps && _isActive)
                {
                    float d = Vector3.Distance(avatar.transform.position, optimalPos);
                    if (d <= 2f) break;

                    ModHelpers.ShowBark(avatar, "Walking to optimal spot...");
                    Vector3 dir = (optimalPos - avatar.transform.position).normalized;
                    Vector3 stepPos = avatar.transform.position + dir * Mathf.Min(15f, d);
                    avatar.WalkToPosition(stepPos, true, false, 10.0f, null);

                    yield return null;
                    while (avatar.actionsController.HasAnyActions()) yield return null;

                    if (Mathf.Abs(d - Vector3.Distance(avatar.transform.position, optimalPos)) < 0.2f) break;
                    step++;
                }

                // Calculate spawn position used in ThrowWithNativeAction for accurate distance
                Vector3 avatarPos = avatar.transform.position;
                Vector3 targetPos = target.transform.position;
                Vector3 lookDir = (targetPos - avatarPos).normalized;
                Vector3 spawnPos = avatarPos + lookDir * 0.6f;

                float finalDist = Vector3.Distance(spawnPos, targetPos);

                // Log for debugging
                MelonLogger.Msg($"[SkippingShell] Raw distance: {Vector3.Distance(avatarPos, targetPos):F1}m, Throw distance: {finalDist:F1}m");

                if (finalDist > dynamicMaxRange + 15f)
                {
                    ModHelpers.ShowBark(avatar, "Too far to reach!");
                    _targetIgnoreUntil[target] = Time.time + 30f;
                    AutonomyManager.ReleaseLock(avatar, "SkippingShell");
                    continue;
                }

                ModHelpers.ShowBark(avatar, "Throwing!");
                yield return ThrowWithNativeAction(avatar, stoneItem, target, spawnPos);

                // Wait for all avatar actions to finish (prevents double throw)
                while (avatar.actionsController.HasAnyActions())
                    yield return null;

                // Wait for the stone to finish skipping and disappear
                yield return new WaitForSeconds(1.0f);
                while (UnityEngine.Object.FindObjectsOfType<ThrownObject>().Length > 0)
                {
                    yield return new WaitForSeconds(0.5f);
                }

                // Check for success (hit shell)
                var gaveRewardsField = typeof(StoneSkippingTarget).GetField("gaveRewards", BindingFlags.Instance | BindingFlags.NonPublic);
                bool success = false;
                if (gaveRewardsField != null)
                {
                    // Reward assignment can lag behind collision by a few frames.
                    for (int i = 0; i < 8; i++)
                    {
                        success = (bool)gaveRewardsField.GetValue(target);
                        if (success) break;
                        yield return new WaitForSeconds(0.25f);
                    }
                }

                if (success)
                {
                    ModHelpers.ShowBark(avatar, "Success! Target hit.");
                    yield return new WaitForSeconds(2f);
                    mod.ToggleActive(false); // Stop the mod after successful hit
                    yield break;
                }

                _targetIgnoreUntil[target] = Time.time + 30f;
                yield return new WaitForSeconds(5.0f); // Approved 5s wait
                
                while (ModHelpers.IsAvatarBusy(avatar)) yield return null;

                ModHelpers.CollectNearbyDoobers(avatar);
                yield return new WaitForSeconds(1f);
                AutonomyManager.ReleaseLock(avatar, "SkippingShell");
            }
        }

        private StoneSkippingTarget FindNearestShell(AvatarController avatar)
        {
            StoneSkippingTarget best = null;
            float bestDistSq = float.MaxValue;
            Vector3 pos = avatar.transform.position;
            var gaveRewardsField = typeof(StoneSkippingTarget).GetField("gaveRewards", BindingFlags.Instance | BindingFlags.NonPublic);

            foreach (var target in StoneSkippingTarget.allTargets)
            {
                if (target == null) continue;
                if (_targetIgnoreUntil.ContainsKey(target) && Time.time < _targetIgnoreUntil[target]) continue;
                
                bool gaveRewards = false;
                if (gaveRewardsField != null) gaveRewards = (bool)gaveRewardsField.GetValue(target);
                if (gaveRewards) { _targetIgnoreUntil[target] = float.MaxValue; continue; }

                float d2 = (target.transform.position - pos).sqrMagnitude;
                if (d2 < bestDistSq) { bestDistSq = d2; best = target; }
            }
            return best;
        }

        private IEnumerator ThrowWithNativeAction(AvatarController avatar, Item item, StoneSkippingTarget target, Vector3 spawnPos)
        {
            Vector3 targetPos = target.transform.position;
            Vector3 avatarPos = avatar.transform.position;

            Vector3 lookDir = targetPos - avatarPos;
            lookDir.y = 0;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                avatar.animationController.SetFacingDirection(lookDir);
                avatar.SetHorizontalFlipping(lookDir.x < 0);
            }

            // Do NOT add AvatarActionThrowing — its Steps() coroutine calls ThrowObject
            // internally at the animation throw-point, which would spawn a second stone
            // before our own ThrowObject call below.
            yield return null; // one frame for facing to settle

            if (avatar.ThrowObject(Vector3.zero, item, out ThrownObject thrownObj, out bool isBlocking, true, new Il2CppSystem.Nullable<Vector3>(spawnPos)))
            {
                if (thrownObj != null)
                {
                    try
                    {
                        var endPosField = typeof(ThrownObject).GetField("throwEndPosition", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (endPosField != null) endPosField.SetValue(thrownObj, targetPos);
                    }
                    catch { }

                    // Full 2D direction toward the shell. This game uses X/Y as the flat
                    // 2D world plane (Z is always 0); there is no 3D height axis to zero out.
                    // Zeroing direction.y strips the screen-Y component and causes the stone
                    // to travel in the wrong direction.
                    Vector3 direction = (targetPos - spawnPos).normalized;
                    float distance = Vector3.Distance(spawnPos, targetPos);

                    var config = ThrownObject.GetConfig(item);
                    float mass = (config != null) ? config.mass : 1.0f;
                    bool isSpecialStone = ModHelpers.IsSpecialSkippingStone(item);
                    bool isProStone = !isSpecialStone && ModHelpers.IsProSkippingStone(item);

                    float speedMultiplier = isSpecialStone ? SpecialSpeedMultiplier :
                                            (isProStone ? ProSpeedMultiplier : BasicSpeedMultiplier);
                    string stoneType = isSpecialStone ? "Special" : (isProStone ? "Pro" : "Basic");

                    float speed = speedMultiplier * Mathf.Sqrt(Mathf.Max(0f, distance) * Gravity);

                    int predictedSkips = CalculatePossibleSkips(distance, config, mass, speedMultiplier, out float impactForce, out float minDistanceForFirstSkip);

                    Vector3 velocity = direction * speed;

                    MelonLogger.Msg($"[SkippingController] Stone: {stoneType} | Dist: {distance:F1}m | Speed: {speed:F1} | ImpactForce: {impactForce:F2} | Mass: {mass:F2} | Mult: {speedMultiplier:F2} (Skips: {predictedSkips})");

                    thrownObj.wasThrownByAvatar = true;
                    // bounceToDestination=false: stone travels through 2D world space with real
                    // physics velocity. When it crosses water tiles the game's collision handler
                    // (CheckCollisionsProcessWater) fires and each water contact = one skip.
                    // bounceToDestination=true animates the stone in a guided arc straight to
                    // throwEndPosition, bypassing water collision entirely — always 0 skips.
                    thrownObj.ThrowWithVelocity(velocity, avatar, true, item, new Il2CppSystem.Nullable<Vector3>(spawnPos), false);
                }
            }
        }

        private static int CalculatePossibleSkips(float distance, ThrownObjectConfig config, float mass, float speedMultiplier, out float impactForce, out float minDistanceForFirstSkip)
        {
            float safeDistance = Mathf.Max(0f, distance);
            float safeMass = Mathf.Max(0.0001f, mass);
            float safeMultiplier = Mathf.Max(0.01f, speedMultiplier);
            float speed = safeMultiplier * Mathf.Sqrt(safeDistance * Gravity);
            impactForce = speed * safeMass;
            minDistanceForFirstSkip = 0f;

            if (config == null || config.skipAmounts == null || config.skipAmounts.Length == 0)
            {
                return 0;
            }

            float minForceForAnySkip = float.MaxValue;
            int possibleSkips = 0;

            foreach (var pair in config.skipAmounts)
            {
                if (pair.skips <= 0) continue;
                if (pair.force < minForceForAnySkip) minForceForAnySkip = pair.force;
                if (impactForce >= pair.force && pair.skips > possibleSkips)
                {
                    possibleSkips = pair.skips;
                }
            }

            if (minForceForAnySkip < float.MaxValue)
            {
                float minSpeed = minForceForAnySkip / safeMass;
                float denom = safeMultiplier * safeMultiplier * Gravity;
                minDistanceForFirstSkip = (denom > 0f) ? minSpeed * minSpeed / denom : 0f;

                // Too near to reach the first skip threshold.
                if (safeDistance + 0.01f < minDistanceForFirstSkip)
                {
                    return 0;
                }
            }

            return Mathf.Clamp(possibleSkips, 0, MaxPossibleSkips);
        }
    }
}
