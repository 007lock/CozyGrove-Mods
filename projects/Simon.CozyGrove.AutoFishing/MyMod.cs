using Il2Cpp;
using MelonLoader;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace Simon.CozyGrove.AutoFishing
{
    // Core lifecycle, shared fields and state machine dispatch.
    // The other partial files contain the actual state implementations.
    public partial class MyMod : MelonMod
    {
        // --- Scene / avatar ---
        private bool _isInGameScene = false;
        private AvatarController _cachedAvatar = null;

        // --- State machine ---
        private enum FishingState { Idle, Bidding, WalkingToFish, ThrowingRod, Fishing, Collecting }
        private FishingState _currentState = FishingState.Idle;
        private Fish _targetFish = null;
        private float _stateTimer = 0f;
        private AvatarActionFishing _currentFishingAction = null;
        private Vector3 _lastAvatarPos;
        private float _stuckTimer = 0f;
        private float _lastStuckCheckTime = 0f;

        // --- Rod / equip ---
        private string _pendingEquipId = null;
        private float _checkTimer = 0f;

        // --- Modes ---
        private bool _isActive = false;
        private bool _findNewFishOnly = false;

        // --- Configuration ---
        private const float CheckInterval = 0.5f;
        private const float CastDistanceMax = 15.0f;
        private const float CastDistanceMin = 5.0f;
        private const float ActionTimeout = 15f;
        private const float CollectDelay = 1.0f;

        // ----------------------------------------------------------------

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _isInGameScene = (sceneName == "Game");
            _cachedAvatar = null;
            if (!_isInGameScene) ResetState();
        }

        private AvatarController GetAvatar()
        {
            if (_cachedAvatar == null && _isInGameScene)
                _cachedAvatar = UnityEngine.Object.FindObjectOfType<AvatarController>();
            return _cachedAvatar;
        }

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
                MelonLogger.Msg($"AutoFishing is now {(_isActive ? "ON" : "OFF")} (NewFishOnly={_findNewFishOnly})");
                ShowBark(avatar, $"AutoFishing {(_isActive ? "ON" : "OFF")}");
            }
            
            // New Fish Only filter toggle - Alt+F (does NOT activate/deactivate fishing)
            if ((Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.F))
            {
                _findNewFishOnly = !_findNewFishOnly;
                MelonLogger.Msg($"NewFishOnly filter is now {(_findNewFishOnly ? "ON" : "OFF")}");
                ShowBark(avatar, $"NewFishOnly {(_findNewFishOnly ? "ON" : "OFF")}");
            }

            if (!_isActive) return;
            if (avatar == null) return;

            switch (_currentState)
            {
                case FishingState.Idle:
                    UpdateIdle(avatar);
                    break;
                case FishingState.Bidding:
                    UpdateBidding(avatar);
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
            _lastStuckCheckTime = 0f;
            _pendingEquipId = null;

            if (_cachedAvatar != null)
            {
                AutonomyManager.ClearBid(_cachedAvatar, "AutoFishing");
                AutonomyManager.ReleaseLock(_cachedAvatar, "AutoFishing");
            }

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

        private bool IsAvatarBusy(AvatarController avatar)
        {
            if (avatar == null || avatar.actionsController == null) return true;
            if (GameUI.Instance.IsAnyModalUIOpen() || GameUI.Instance.InDialog()) return true;
            if (AutonomyManager.IsLockedByAnother(avatar, "AutoFishing")) return true;
            return avatar.actionsController.HasAnyActions();
        }

        private void ShowBark(AvatarController avatar, string text)
        {
            if (avatar != null && avatar.speechBubble != null)
                avatar.speechBubble.Show(text, SpriteInfo.Invalid, 2.0f);
        }
    }
}
