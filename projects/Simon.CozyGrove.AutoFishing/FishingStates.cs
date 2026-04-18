using Il2Cpp;
using MelonLoader;
using UnityEngine;
using System;

namespace Simon.CozyGrove.AutoFishing
{
    // Idle → Bidding → WalkingToFish → ThrowingRod → Fishing → Collecting
    public partial class MyMod
    {
        private void UpdateIdle(AvatarController avatar)
        {
            _stateTimer += Time.deltaTime;

            if (IsAvatarBusy(avatar)) return;

            if (!IsRodEquipped(avatar, out var equippedRod))
            {
                if (_stateTimer > 3.0f)
                {
                    MelonLogger.Msg("Idle: Rod not detected as equipped. Attempting to equip...");
                    TryEquipRodOnActivation(avatar);
                    _stateTimer = 0f;
                }
                return;
            }

            _checkTimer += Time.deltaTime;
            if (_checkTimer < CheckInterval) return;
            _checkTimer = 0f;

            var fishes = UnityEngine.Object.FindObjectsOfType<Fish>();
            Fish bestFish = null;
            float bestDistSq = float.MaxValue;
            Vector3 avatarPos = avatar.transform.position;

            LootSystem lootSys = null;
            if (_findNewFishOnly)
            {
                lootSys = Il2Cpp.Main.Instance?.lootSystem;
                if (lootSys == null)
                    MelonLogger.Warning("[FishFilter] LootSystem not available — filter skipped this tick.");
                // Reset per-tick gate so exactly one uncached table is resolved this frame.
                ResetLootTableTickGate();
            }

            foreach (var fish in fishes)
            {
                if (fish != null && fish.gameObject.activeInHierarchy)
                {
                    if (_findNewFishOnly && lootSys != null)
                    {
                        string lootTable = fish.typeConfig?.lootTable;
                        if (string.IsNullOrEmpty(lootTable) || !HasValueItemInLootTable(lootTable, lootSys))
                            continue;
                    }

                    float distSq = (fish.transform.position - avatarPos).sqrMagnitude;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestFish = fish;
                    }
                }
            }

            if (bestFish != null)
            {
                _targetFish = bestFish;
                _currentState = FishingState.Bidding;
                _stateTimer = 0f;
                AutonomyManager.UpdateBid(avatar, "AutoFishing", Mathf.Sqrt(bestDistSq));
            }
        }

        private void UpdateBidding(AvatarController avatar)
        {
            _stateTimer += Time.deltaTime;
            if (_stateTimer < 0.2f) return;

            if (_targetFish == null
                || !AutonomyManager.IsMyBidLowest(avatar, "AutoFishing", Vector3.Distance(avatar.transform.position, _targetFish.transform.position))
                || IsAvatarBusy(avatar))
            {
                AutonomyManager.ClearBid(avatar, "AutoFishing");
                ResetState();
                return;
            }

            AutonomyManager.ClearBid(avatar, "AutoFishing");
            AutonomyManager.AcquireLock(avatar, "AutoFishing");

            _currentState = FishingState.WalkingToFish;
            _stateTimer = 0f;
            _stuckTimer = 0f;
            _lastStuckCheckTime = 0f;
            _lastAvatarPos = avatar.transform.position;
            MelonLogger.Msg("Won bid, moving to cast position...");

            Func<Vector3> trackFunc = new Func<Vector3>(() =>
            {
                if (_targetFish != null && _targetFish.gameObject.activeInHierarchy)
                    return _targetFish.transform.position;
                return avatar.transform.position;
            });

            avatar.WalkToPosition(_targetFish.transform.position, true, false, 0.5f, trackFunc);
        }

        private void UpdateWalkingToFish(AvatarController avatar)
        {
            _stateTimer += Time.deltaTime;

            if (avatar != null && avatar.speechBubble != null && !avatar.speechBubble.isShown)
                ShowBark(avatar, "AutoFishing: ON");

            if (_targetFish == null || !_targetFish.gameObject.activeInHierarchy)
            {
                MelonLogger.Msg("Fish lost, resetting...");
                ResetState();
                return;
            }

            Vector3 avatarPos = avatar.transform.position;
            Vector3 fishPos = _targetFish.transform.position;
            float dist2D = Vector2.Distance(new Vector2(avatarPos.x, avatarPos.z), new Vector2(fishPos.x, fishPos.z));

            if (_stateTimer - _lastStuckCheckTime > 0.25f)
            {
                if (Vector3.Distance(avatarPos, _lastAvatarPos) < 0.1f)
                    _stuckTimer += (_stateTimer - _lastStuckCheckTime);
                else
                    _stuckTimer = 0f;

                _lastAvatarPos = avatarPos;
                _lastStuckCheckTime = _stateTimer;
            }

            bool hitShore = _stuckTimer > 0.5f;
            bool actionComplete = _stateTimer > 0.5f && !avatar.actionsController.HasAnyActions();
            bool timeout = _stateTimer > ActionTimeout;

            if (hitShore || actionComplete || timeout)
            {
                if (dist2D > 25.0f)
                {
                    MelonLogger.Msg($"Reached destination but fish is still too far ({dist2D:F2}m). Resetting.");
                    ResetState();
                    return;
                }

                if (timeout) MelonLogger.Msg("Walking timeout, casting...");
                else if (hitShore) MelonLogger.Msg($"Hit shore ({dist2D:F2}m), casting...");
                else MelonLogger.Msg($"Walk action complete ({dist2D:F2}m), casting...");

                avatar.actionsController.CancelAll();
                _currentState = FishingState.ThrowingRod;
                _stateTimer = 0f;
            }
        }

        private void UpdateFishing(AvatarController avatar)
        {
            _stateTimer += Time.deltaTime;

            if (avatar != null && avatar.speechBubble != null && !avatar.speechBubble.isShown)
                ShowBark(avatar, "AutoFishing: ON");

            var currentAction = avatar.actionsController.GetCurrent();
            if (currentAction == null)
            {
                if (_stateTimer > 2.0f)
                {
                    MelonLogger.Msg("Fishing action ended.");
                    _currentState = FishingState.Collecting;
                    _stateTimer = 0f;
                }
                return;
            }

            var fishingAction = currentAction.TryCast<AvatarActionFishing>();
            if (fishingAction != null)
            {
                _currentFishingAction = fishingAction;

                if (fishingAction._stateMachine != null)
                {
                    if (_targetFish != null)
                    {
                        bool shouldBeFlipped = (_targetFish.transform.position.x - avatar.transform.position.x) < 0;
                        if (avatar.IsHorizontalFlipped != shouldBeFlipped)
                            avatar.SetHorizontalFlipping(shouldBeFlipped);
                        fishingAction._isAvatarFlippedHorizontally = shouldBeFlipped;
                    }

                    string stateName = null;
                    try { stateName = fishingAction._stateMachine.GetCurrentStateName(); }
                    catch (Exception) { }

                    if (string.IsNullOrEmpty(stateName)) return;

                    if (Mathf.FloorToInt(_stateTimer) % 5 == 0 && _stateTimer % 1.0f < 0.02f)
                        MelonLogger.Msg($"Fishing state: {stateName}");

                    if (stateName == "FishingStateFishBiting")
                    {
                        MelonLogger.Msg("Fish bit! Reeling in...");
                        fishingAction.OnClick();
                    }
                }
            }
            else if (_stateTimer > 20.0f)
            {
                MelonLogger.Msg("Fishing interrupted or timed out.");
                _currentState = FishingState.Collecting;
                _stateTimer = 0f;
            }
        }

        private void UpdateCollecting(AvatarController avatar)
        {
            _stateTimer += Time.deltaTime;
            if (_stateTimer < CollectDelay) return;

            if (GameUI.Instance.IsAnyModalUIOpen() || GameUI.Instance.InDialog()) return;
            if (avatar != null && avatar.speechBubble != null && avatar.speechBubble.isShown) return;

            var doobers = UnityEngine.Object.FindObjectsOfType<Doober>();
            int collected = 0;
            foreach (var doober in doobers)
            {
                if (doober != null && doober.gameObject.activeInHierarchy)
                {
                    doober.Pickup();
                    collected++;
                }
            }

            if (collected > 0)
            {
                MelonLogger.Msg($"Collected {collected} doober(s)");
                _lootTableNewCache.Clear(); // collection state may have changed
            }

            ResetState();
        }
    }
}
