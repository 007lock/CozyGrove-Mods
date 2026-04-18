using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace Simon.CozyGrove.SkippingShell
{
    public class MyMod : MelonMod
    {
        private bool _isActive = false;
        private object _coroInstance = null;
        private AvatarController _cachedAvatar = null;
        private bool _isInGameScene = false;
        private SkippingController _controller;

        public bool IsInGameScene => _isInGameScene;

        public override void OnInitializeMelon()
        {
            _controller = new SkippingController(_isActive);
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _isInGameScene = sceneName == "Game";
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

        public void ToggleActive(bool active)
        {
            _isActive = active;
            _controller.SetActive(_isActive);
            if (!_isActive)
            {
                ResetState();
            }
            MelonLogger.Msg($"SkippingShell is now {(_isActive ? "ON" : "OFF")}");
            var avatar = GetAvatar();
            if (avatar != null) ModHelpers.ShowBark(avatar, $"SkippingShell: {(_isActive ? "ON" : "OFF")}");
        }

        public override void OnUpdate()
        {
            if (!_isInGameScene) return;

            var avatar = GetAvatar();
            if (avatar == null) return;

            // Toggle logic (Ctrl+S)
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.S))
            {
                ToggleActive(!_isActive);
            }

            if (!_isActive) return;

            _coroInstance ??= MelonCoroutines.Start(_controller.SkippingShellCoro(this, GetAvatar));
        }

        private void ResetState()
        {
            if (_coroInstance != null)
            {
                MelonCoroutines.Stop(_coroInstance);
                _coroInstance = null;
            }
            var avatar = GetAvatar();
            if (avatar != null)
            {
                AutonomyManager.ClearBid(avatar, "SkippingShell");
                AutonomyManager.ReleaseLock(avatar, "SkippingShell");
            }
        }
    }
}
