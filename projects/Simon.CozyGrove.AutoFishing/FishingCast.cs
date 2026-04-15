using Il2Cpp;
using MelonLoader;
using UnityEngine;
using System;
using System.Collections;

namespace Simon.CozyGrove.AutoFishing
{
    // Handles the ThrowingRod state and the manual bobber arc coroutine.
    public partial class MyMod
    {
        private void UpdateThrowingRod(AvatarController avatar)
        {
            _stateTimer += Time.deltaTime;

            if (avatar != null && avatar.speechBubble != null && !avatar.speechBubble.isShown)
                ShowBark(avatar, "AutoFishing: ON");

            if (!IsRodEquipped(avatar, out var rod))
            {
                if (_stateTimer < 5.0f)
                {
                    if (Mathf.FloorToInt(_stateTimer * 4) % 4 == 0 && _stateTimer % 0.25f < 0.02f)
                        MelonLogger.Msg("Casting: Waiting for rod to be fully detected (sync)...");
                    return;
                }
                MelonLogger.Warning("Casting: Rod detection timed out. Attempting cast anyway.");
            }

            _pendingEquipId = null;

            Vector3 targetPos = _targetFish.transform.position;
            Vector3 avatarPos = avatar.transform.position;

            try
            {
                ItemStorage storage = null;
                bool isSea = !_targetFish.isInPond;
                if (World.Instance != null) storage = World.Instance.seaItemStorage;

                Vector3 lookDir = targetPos - avatarPos;
                lookDir.y = 0;
                bool flipped = lookDir.x < 0;
                if (lookDir.sqrMagnitude > 0.001f)
                {
                    avatar.animationController.SetFacingDirection(lookDir);
                    avatar.SetHorizontalFlipping(flipped);
                }

                MelonLogger.Msg($"Starting manual cast for fish at {targetPos}. Flipped: {flipped}");
                avatar.animationController.PlaySpecialAnimation("fishing/fish cast", null, null, false, false);
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
            yield return new WaitForSeconds(0.4f);

            var bobberPrefab = ResourcesManager.LoadPrefab(AvatarActionFishing.ANIMATED_BOBBER_PREFAB);
            if (bobberPrefab == null)
            {
                MelonLogger.Error("Could not load animatedBobber prefab!");
                yield break;
            }

            var bobber = UnityEngine.Object.Instantiate(bobberPrefab);
            bobber.hideFlags |= HideFlags.DontSave;

            Vector3 avatarPos = avatar.transform.position;
            Vector3 startPos = avatarPos + Vector3.up * 1.8f;
            bobber.transform.position = startPos;

            MelonLogger.Msg($"Manual arc move: {startPos} -> {targetPos}");

            float duration = 0.6f;
            float elapsed = 0f;
            float peakHeight = 2.0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                Vector3 currentPos = Vector3.Lerp(startPos, targetPos, t);
                currentPos.y += 4f * peakHeight * t * (1f - t);
                bobber.transform.position = currentPos;
                yield return null;
            }

            // Mirror X to cancel the game's internal mirroring when calling StartFishing
            Vector3 finalCastPos = targetPos;
            finalCastPos.x = avatarPos.x - (targetPos.x - avatarPos.x);

            Vector3 lookDir = targetPos - avatarPos;
            lookDir.y = 0;
            if (lookDir.sqrMagnitude > 0.001f)
            {
                avatar.animationController.SetFacingDirection(lookDir);
                avatar.SetHorizontalFlipping(lookDir.x < 0);
            }

            MelonLogger.Msg($"Arc arrived. Calling StartFishing with mirrored target {finalCastPos}");
            avatar.StartFishing(bobber, finalCastPos, storage, isSea);
            avatar.SetHorizontalFlipping(lookDir.x < 0);
        }
    }
}
