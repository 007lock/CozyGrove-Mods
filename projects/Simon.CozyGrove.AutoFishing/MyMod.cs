using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;

namespace Simon.CozyGrove.AutoFishing
{
    public class MyMod : MelonMod
    {
        private float _checkTimer = 0f;
        private const float CheckInterval = 0.5f;
        private bool _isActive = false;
        private AvatarController _cachedAvatar = null;
        private bool _isInGameScene = false;

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _isInGameScene = (sceneName == "Game");
            _cachedAvatar = null;
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

        private enum FishingState
        {
            Idle,
            WalkingToFish,
            ThrowingRod,
            Fishing,
            Collecting
        }

        private FishingState _currentState = FishingState.Idle;
        private Fish _targetFish = null;
        private float _stateTimer = 0f;
        private AvatarActionFishing _currentFishingAction = null;
        private Vector3 _lastAvatarPos;
        private float _stuckTimer = 0f;
        private string _pendingEquipId = null;

        // Configuration
        private const float CastDistanceMax = 15.0f; 
        private const float CastDistanceMin = 5.0f;
        private const float ActionTimeout = 15f;
        private const float CollectDelay = 1.0f;
        public override void OnUpdate()
        {
            if (!_isInGameScene) return;

            var avatar = GetAvatar();
            if (avatar == null) return;

            // Toggle logic - Use KeyCode.F as requested
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.F))
            {
                _isActive = !_isActive;
                if (!_isActive) ResetState();
                MelonLogger.Msg($"AutoFishing is now {(_isActive ? "ON" : "OFF")}");
                ShowBark(avatar, $"AutoFishing is now {(_isActive ? "ON" : "OFF")}");
            }

            if (!_isActive) return;
            if (avatar == null) return;

            switch (_currentState)
            {
                case FishingState.Idle:
                    UpdateIdle(avatar);
                    break;
                case FishingState.WalkingToFish:
                    UpdateWalkingToFish(avatar);
                    break;
                case FishingState.ThrowingRod:
                    UpdateThrowingRod(avatar);
                    break;
                case FishingState.Fishing:
                    UpdateFishing(avatar);
                    break;
                case FishingState.Collecting:
                    UpdateCollecting(avatar);
                    break;
            }
        }

        private void ResetState()
        {
            _currentState = FishingState.Idle;
            _targetFish = null;
            _currentFishingAction = null;
            _stateTimer = 0f;
            _stuckTimer = 0f;
            _pendingEquipId = null;

            // Cleanup orphaned bobbers from failed manual casts
            var bobbers = UnityEngine.Object.FindObjectsOfType<GameObject>();
            foreach (var b in bobbers)
            {
                if (b != null && b.name.Contains("animatedBobber") && b.transform.parent == null)
                {
                    UnityEngine.Object.Destroy(b);
                }
            }
        }

        private void UpdateIdle(AvatarController avatar)
        {
            _stateTimer += Time.deltaTime;

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

            foreach (var fish in fishes)
            {
                if (fish != null && fish.gameObject.activeInHierarchy)
                {
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
                _currentState = FishingState.WalkingToFish;
                _stateTimer = 0f;
                _stuckTimer = 0f;
                _lastAvatarPos = avatar.transform.position;
                MelonLogger.Msg("Found fish, moving to cast position...");
                
                Func<Vector3> trackFunc = new Func<Vector3>(() => 
                {
                    if (_targetFish != null && _targetFish.gameObject.activeInHierarchy)
                        return _targetFish.transform.position;
                    return avatar.transform.position;
                });

                // Walk to a distance slightly smaller than CastDistanceMax
                avatar.WalkToPosition(_targetFish.transform.position, true, false, 10.0f, trackFunc);
            }
        }

        private void TryEquipRodOnActivation(AvatarController avatar)
        {
            MelonLogger.Msg("Mod Activation: Scanning inventory for fishing rods for diagnostics...");
            Item rod = null;
            var slots = avatar.inventory.slots;
            
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i]?.item != null && IsCorrectRod(slots[i].item, true))
                {
                    rod = slots[i].item;
                    MelonLogger.Msg($"[Diagnostic] Found preferred ocean rod: {rod.collectableItemConfig.id} in slot {i}");
                    break;
                }
            }

            if (rod == null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i]?.item != null && IsFishingRod(slots[i].item))
                    {
                        rod = slots[i].item;
                        MelonLogger.Msg($"[Diagnostic] Found generic rod: {rod.collectableItemConfig.id} in slot {i}");
                        break;
                    }
                }
            }

            if (rod == null)
            {
                MelonLogger.Warning("Mod Activation: No fishing rod found in inventory! AutoFishing might not work.");
            }

            // Attempt to equip using the game's native hotkey event
            var inputActions = GetAvatar()?.GetComponent<InputActionsController>() ?? UnityEngine.Object.FindObjectOfType<InputActionsController>();
            if (inputActions != null && inputActions.HotkeyEquipFishingRod != null)
            {
                MelonLogger.Msg("Invoking native HotkeyEquipFishingRod event...");
                inputActions.HotkeyEquipFishingRod.Invoke();
                if (rod != null) _pendingEquipId = rod.collectableItemConfig.id;
            }
            else
            {
                MelonLogger.Error("Could not find InputActionsController or HotkeyEquipFishingRod event! Native equipping failed.");
            }
        }

        private bool IsRodEquipped(AvatarController avatar, out Item equippedRod)
        {
            equippedRod = null;

            // 1. Check avatar.activeItem
            if (avatar.activeItem != null && IsFishingRod(avatar.activeItem))
            {
                equippedRod = avatar.activeItem;
                return true;
            }

            // 2. Check avatar.inventory.ActiveItem
            if (avatar.inventory != null && avatar.inventory.ActiveItem != null && IsFishingRod(avatar.inventory.ActiveItem))
            {
                equippedRod = avatar.inventory.ActiveItem;
                return true;
            }

            // 3. Scan inventory for 'equipped' flag
            if (avatar.inventory != null)
            {
                var slots = avatar.inventory.slots;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i]?.item != null && slots[i].item.equipped && IsFishingRod(slots[i].item))
                    {
                        equippedRod = slots[i].item;
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
                        if (hiddenItem != null && IsFishingRod(hiddenItem))
                        {
                            MelonLogger.Msg($"[Diagnostic] Found rod in private 'usingItem' field: {hiddenItem.collectableItemConfig.id}");
                            equippedRod = hiddenItem;
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex) { MelonLogger.Msg($"[Diagnostic] Reflection failed: {ex.Message}"); }

            return false;
        }

        private void UpdateWalkingToFish(AvatarController avatar)
        {
            _stateTimer += Time.deltaTime;

            // Show tracking status
            if (avatar != null && avatar.speechBubble != null && !avatar.speechBubble.isShown)
            {
                ShowBark(avatar, "AutoFishing: ON");
            }

            if (_targetFish == null || !_targetFish.gameObject.activeInHierarchy)
            {
                MelonLogger.Msg("Fish lost, resetting...");
                ResetState();
                return;
            }

            Vector3 avatarPos = avatar.transform.position;
            Vector3 fishPos = _targetFish.transform.position;
            float dist2D = Vector2.Distance(new Vector2(avatarPos.x, avatarPos.z), new Vector2(fishPos.x, fishPos.z));

            // Stuck detection (e.g. at the shore)
            if (Vector3.Distance(avatarPos, _lastAvatarPos) < 0.1f)
            {
                _stuckTimer += Time.deltaTime;
            }
            else
            {
                _stuckTimer = 0f;
                _lastAvatarPos = avatarPos;
            }

            // Transition if: 1. In range, 2. Stuck, 3. Timeout
            bool inRange = dist2D <= CastDistanceMax;
            bool stuckInRange = _stuckTimer > 1.0f && dist2D < 20.0f;
            bool timeout = _stateTimer > ActionTimeout;

            if (inRange || stuckInRange || timeout)
            {
                if (timeout) MelonLogger.Msg("Walking timeout, casting...");
                else if (stuckInRange) MelonLogger.Msg($"Stuck at shore ({dist2D:F2}m), casting...");
                else MelonLogger.Msg($"In range ({dist2D:F2}m), casting...");
                
                _currentState = FishingState.ThrowingRod;
                _stateTimer = 0f;
            }
        }

        private void UpdateThrowingRod(AvatarController avatar)
        {
            _stateTimer += Time.deltaTime;

            // Show tracking status
            if (avatar != null && avatar.speechBubble != null && !avatar.speechBubble.isShown)
            {
                ShowBark(avatar, "AutoFishing: ON");
            }

            if (!IsRodEquipped(avatar, out var rod))
            {
                if (_stateTimer < 5.0f)
                {
                    if (Mathf.FloorToInt(_stateTimer * 4) % 4 == 0 && _stateTimer % 0.25f < 0.02f)
                    {
                        MelonLogger.Msg("Casting: Waiting for rod to be fully detected (sync)...");
                    }
                    return;
                }
                else
                {
                    MelonLogger.Warning("Casting: Rod detection timed out. Attempting cast anyway.");
                }
            }
            
            _pendingEquipId = null;

            Vector3 targetPos = _targetFish.transform.position;
            Vector3 avatarPos = avatar.transform.position;
            
            try 
            {
                ItemStorage storage = null;
                bool isSea = !_targetFish.isInPond;
                
                if (World.Instance != null) storage = World.Instance.seaItemStorage;

                // Ensure avatar is facing the fish
                Vector3 lookDir = targetPos - avatarPos;
                lookDir.y = 0;
                bool flipped = lookDir.x < 0;
                if (lookDir.sqrMagnitude > 0.001f)
                {
                    avatar.animationController.SetFacingDirection(lookDir);
                    avatar.SetHorizontalFlipping(flipped);
                }

                MelonLogger.Msg($"Starting manual cast for fish at {targetPos}. Flipped: {flipped}");
                
                // Play the casting animation
                avatar.animationController.PlaySpecialAnimation("fishing/fish cast", null, null, false, false);

                // Start the manual throw coroutine
                MelonCoroutines.Start(DoManualCast(avatar, targetPos, storage, isSea, flipped));

                _currentState = FishingState.Fishing;
                _stateTimer = 0f;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"StartFishing sequence failed: {ex.Message}\n{ex.StackTrace}");
                ResetState();
            }
        }

        private IEnumerator DoManualCast(AvatarController avatar, Vector3 targetPos, ItemStorage storage, bool isSea, bool flipped)
        {
            // Wait for the animation to reach the "throw" point
            yield return new WaitForSeconds(0.4f);

            GameObject bobber = null;
            var bobberPrefab = ResourcesManager.LoadPrefab(AvatarActionFishing.ANIMATED_BOBBER_PREFAB);
            if (bobberPrefab == null)
            {
                MelonLogger.Error("Could not load animatedBobber prefab!");
                yield break;
            }

            bobber = UnityEngine.Object.Instantiate(bobberPrefab);
            bobber.hideFlags |= HideFlags.DontSave;

            Vector3 avatarPos = avatar.transform.position;
            // Start the bobber slightly above the avatar's head or rod tip
            Vector3 startPos = avatarPos + Vector3.up * 1.8f;
            bobber.transform.position = startPos;

            MelonLogger.Msg($"Manual arc move: {startPos} -> {targetPos}");

            float duration = 0.6f; // Adjust based on distance if needed
            float elapsed = 0f;
            float peakHeight = 2.0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                
                // Linear lerp for X/Z
                Vector3 currentPos = Vector3.Lerp(startPos, targetPos, t);
                
                // Parabolic height curve
                // h = 4 * peak * t * (1-t)
                float h = 4f * peakHeight * t * (1f - t);
                currentPos.y += h;
                
                bobber.transform.position = currentPos;
                yield return null;
            }

            // MIRRORING COMPENSATOR: The game mirrors X around avatar internally.
            // When we call StartFishing, it will mirror targetPos.x.
            // So we pass the mirrored targetPos to cancel it out.
            Vector3 finalCastPos = targetPos;
            finalCastPos.x = avatarPos.x - (targetPos.x - avatarPos.x);

            // Ensure character faces the REAL fish during the cast
            Vector3 lookDir = targetPos - avatarPos;
            lookDir.y = 0;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                avatar.animationController.SetFacingDirection(lookDir);
                avatar.SetHorizontalFlipping(lookDir.x < 0);
            }

            MelonLogger.Msg($"Arc arrived. Calling StartFishing with mirrored target {finalCastPos}");
            avatar.StartFishing(bobber, finalCastPos, storage, isSea);
            
            // Re-force flipping immediately after StartFishing to prevent frame-one flickers
            avatar.SetHorizontalFlipping(lookDir.x < 0);
        }

        private bool IsFishingRod(Item item)
        {
            if (item == null || item.collectableItemConfig == null) return false;
            return item.collectableItemConfig.IsTagMatch(CollectableItemConfig.TAG_FISHING_ROD, false);
        }

        private bool IsCorrectRod(Item rod, bool isSea)
        {
            if (rod == null || rod.collectableItemConfig == null) return false;
            if (!IsFishingRod(rod)) return false;

            if (ConfigData.fishing != null)
            {
                if (isSea)
                {
                    if (!string.IsNullOrEmpty(ConfigData.fishing.fishingRodTagOcean))
                        return rod.collectableItemConfig.IsTagMatch(ConfigData.fishing.fishingRodTagOcean, false);
                }
                else
                {
                    if (!string.IsNullOrEmpty(ConfigData.fishing.fishingRodTagPond))
                        return rod.collectableItemConfig.IsTagMatch(ConfigData.fishing.fishingRodTagPond, false);
                }
            }

            return true;
        }

        private void UpdateFishing(AvatarController avatar)
        {
            _stateTimer += Time.deltaTime;

            // Show tracking status
            if (avatar != null && avatar.speechBubble != null && !avatar.speechBubble.isShown)
            {
                ShowBark(avatar, "AutoFishing: ON");
            }
            var currentAction = avatar.actionsController.GetCurrent();
            if (currentAction == null)
            {
                if (_stateTimer > 2.0f) // Action ended naturally
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
                    // FORCE CORRECT FACING: Override game internal flipping that gets confused by mirrored target
                    if (_targetFish != null)
                    {
                        bool shouldBeFlipped = (_targetFish.transform.position.x - avatar.transform.position.x) < 0;
                        if (avatar.IsHorizontalFlipped != shouldBeFlipped)
                        {
                            avatar.SetHorizontalFlipping(shouldBeFlipped);
                        }
                        // Also sync the action's internal flag
                        fishingAction._isAvatarFlippedHorizontally = shouldBeFlipped;
                    }

                    string stateName = null;
                    try
                    {
                        // The game's GetCurrentStateName can throw NRE if currentState is null (common right after StartFishing)
                        stateName = fishingAction._stateMachine.GetCurrentStateName();
                    }
                    catch (Exception)
                    {
                        // Ignore for now, state machine is likely initializing
                    }

                    if (string.IsNullOrEmpty(stateName)) return;
                    
                    // Periodically log state while fishing to help debug
                    if (Mathf.FloorToInt(_stateTimer) % 5 == 0 && _stateTimer % 1.0f < 0.02f)
                    {
                        MelonLogger.Msg($"Fishing state: {stateName}");
                    }

                    if (stateName == "FishingStateFishBiting")
                    {
                        MelonLogger.Msg("Fish bit! Reeling in...");
                        fishingAction.OnClick();
                    }
                }
            }
            else if (_stateTimer > 20.0f) // Some other action took over
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

            // Wait for user to manually dismiss any popups (e.g. badge rewards)
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

            if (collected > 0) MelonLogger.Msg($"Collected {collected} doober(s)");
            ResetState();
        }

        private void ShowBark(AvatarController avatar, string text)
        {
            if (avatar != null && avatar.speechBubble != null)
            {
                avatar.speechBubble.Show(text, SpriteInfo.Invalid, 2.0f);
            }
        }
    }
}
