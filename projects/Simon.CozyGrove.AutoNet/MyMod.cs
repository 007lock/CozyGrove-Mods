using Il2Cpp;
using MelonLoader;
using UnityEngine;
using System.Collections;

namespace Simon.CozyGrove.AutoNet
{
    // Core lifecycle, shared fields, and state machine dispatch.
    // Other partial files contain the coroutine, target logic, and net helpers.
    public partial class MyMod : MelonMod
    {
        // --- Scene / avatar ---
        private bool _isInGameScene = false;
        private AvatarController _cachedAvatar = null;

        // --- State ---
        private bool _isActive = false;
        private bool _findNewCritterOnly = false;
        private Critter _targetCritter = null;
        private object _coroInstance = null;

        // --- Configuration ---
        private const float CatchDistance = 2.0f;
        private const float CatchTimeout = 10f;
        private const float CollectDelay = 1.0f;

        // ----------------------------------------------------------------

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _isInGameScene = sceneName == "Game";
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

            // Toggle (Ctrl+T)
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.T))
            {
                _isActive = !_isActive;
                if (!_isActive) ResetState();
                MelonLogger.Msg($"AutoNet is now {(_isActive ? "ON" : "OFF")} (NewCritterOnly={_findNewCritterOnly})");
                ShowBark(avatar, $"AutoNet: {(_isActive ? "ON" : "OFF")}");
            }

            // New Critter Only filter toggle (Alt+T) — does NOT activate/deactivate AutoNet
            if ((Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.T))
            {
                _findNewCritterOnly = !_findNewCritterOnly;
                MelonLogger.Msg($"NewCritterOnly filter is now {(_findNewCritterOnly ? "ON" : "OFF")}");
                ShowBark(avatar, $"NewCritterOnly: {(_findNewCritterOnly ? "ON" : "OFF")}");
            }

            if (!_isActive) return;

            if (_coroInstance == null)
                _coroInstance = MelonCoroutines.Start(AutoNetCoro());
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
                AutonomyManager.ClearBid(_cachedAvatar, "AutoNet");
                AutonomyManager.ReleaseLock(_cachedAvatar, "AutoNet");
            }
            _targetCritter = null;
        }

        private bool IsAvatarBusy(AvatarController avatar)
        {
            if (avatar == null || avatar.actionsController == null) return true;
            if (GameUI.Instance.IsAnyModalUIOpen() || GameUI.Instance.InDialog()) return true;
            if (AutonomyManager.IsLockedByAnother(avatar, "AutoNet")) return true;
            return avatar.actionsController.HasAnyActions();
        }

        private void ShowBark(AvatarController avatar, string text)
        {
            if (avatar != null && avatar.speechBubble != null)
                avatar.speechBubble.Show(text, SpriteInfo.Invalid, 2.0f);
        }
    }
}
