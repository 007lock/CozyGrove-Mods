using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Linq;
using System.Collections;

namespace Simon.CozyGrove.AutoNet
{
    public class MyMod : MelonMod
    {
        private bool _isActive = false;
        private Critter _targetCritter = null;
        private object _coroInstance = null;

        // Configuration
        private const float CatchDistance = 2.0f; // Distance from critter to trigger net throw
        private const float CatchTimeout = 10f; // Max time to spend trying to catch one critter
        private const float CollectDelay = 1.0f; // Time to wait for doobers to spawn before collecting

        public override void OnUpdate()
        {
            if (SceneManager.GetActiveScene().name != "Game") return;

            var avatar = UnityEngine.Object.FindObjectOfType<AvatarController>();

            // Toggle logic (Ctrl+T)
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.T))
            {
                _isActive = !_isActive;
                if (!_isActive)
                {
                    ResetState();
                }
                MelonLogger.Msg($"AutoNet is now {(_isActive ? "ON" : "OFF")}");
                ShowBark(avatar, $"AutoNet: {(_isActive ? "ON" : "OFF")}");
            }

            if (!_isActive) return;

            // Start coroutine if not running
            if (_coroInstance == null)
            {
                _coroInstance = MelonCoroutines.Start(AutoNetCoro());
            }
        }

        private void ResetState()
        {
            if (_coroInstance != null)
            {
                MelonCoroutines.Stop(_coroInstance);
                _coroInstance = null;
            }
            _targetCritter = null;
        }

        private IEnumerator AutoNetCoro()
        {
            while (true)
            {
                if (!_isActive || SceneManager.GetActiveScene().name != "Game")
                {
                    yield return new WaitForSeconds(1f);
                    continue;
                }

                var avatar = UnityEngine.Object.FindObjectOfType<AvatarController>();
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

                _targetCritter = FindNearestCritter(avatar);
                if (_targetCritter != null)
                {
                    yield return HandleTarget(avatar, _targetCritter);
                }

                yield return new WaitForSeconds(0.5f);
            }
        }

        private bool IsAvatarBusy(AvatarController avatar)
        {
            if (avatar == null || avatar.actionsController == null) return true;

            // Wait for user to manually dismiss any popups
            if (GameUI.Instance.IsAnyModalUIOpen() || GameUI.Instance.InDialog()) return true;
            if (avatar.speechBubble != null && avatar.speechBubble.isShown) return true;

            return avatar.actionsController.HasAnyActions();
        }

        private Critter FindNearestCritter(AvatarController avatar)
        {
            var critters = UnityEngine.Object.FindObjectsOfType<Critter>();
            Critter bestCritter = null;
            float bestDistSq = float.MaxValue;
            Vector3 avatarPos = avatar.transform.position;

            foreach (var critter in critters)
            {
                if (critter != null && critter.gameObject.activeInHierarchy)
                {
                    float distSq = (critter.transform.position - avatarPos).sqrMagnitude;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestCritter = critter;
                    }
                }
            }
            return bestCritter;
        }

        private IEnumerator HandleTarget(AvatarController avatar, Critter target)
        {
            // Walk to target if too far
            float dist = Vector3.Distance(avatar.transform.position, target.transform.position);
            if (dist > CatchDistance)
            {
                // Follow critter while walking
                Func<Vector3> trackFunc = () => {
                    if (target != null && target.gameObject.activeInHierarchy)
                        return target.transform.position;
                    return avatar.transform.position;
                };

                avatar.WalkToPosition(target.transform.position, true, false, CatchDistance * 0.8f, trackFunc);
                
                float timeout = CatchTimeout;
                while (target != null && target.gameObject.activeInHierarchy && 
                       Vector3.Distance(avatar.transform.position, target.transform.position) > CatchDistance && timeout > 0)
                {
                    if (!_isActive) yield break;
                    
                    // Show tracking status
                    if (!avatar.speechBubble.isShown) ShowBark(avatar, "AutoNet: ON");
                    
                    timeout -= Time.deltaTime;
                    yield return null;
                }
            }

            if (target == null || !target.gameObject.activeInHierarchy) yield break;

            // Check tool
            if (!HasNet(avatar))
            {
                ShowBark(avatar, "No Net!");
                yield break;
            }

            // Ensure net is equipped
            if (avatar.activeItem == null || !avatar.activeItem.isNet)
            {
                Item netItem = null;
                var slots = avatar.inventory.slots;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i] != null && slots[i].item != null && slots[i].item.isNet)
                    {
                        netItem = slots[i].item;
                        break;
                    }
                }
                if (netItem != null)
                {
                    avatar.inventory.UseItem(netItem, false);
                    yield return new WaitForSeconds(0.5f); // Wait for equipment
                }
            }

            // Interact using the high-level method if possible, otherwise manual throw
            var harvestable = target.GetComponent<HarvestableItem>();
            if (harvestable != null)
            {
                harvestable.Interact(avatar, null, null);
            }
            else
            {
                // Manual catch action as fallback
                Vector3 throwForce = (target.transform.position - avatar.transform.position).normalized * 5f;
                var catchAction = new AvatarActionCritterCatching(avatar, avatar.activeItem, throwForce);
                avatar.actionsController.Add(catchAction.Cast<IAvatarAction>());
            }

            // Wait for action to finish and possible doobers to spawn
            yield return new WaitForSeconds(1.5f);
            while (IsAvatarBusy(avatar)) yield return null;
            yield return new WaitForSeconds(0.5f);

            // Collect doobers
            var doobers = UnityEngine.Object.FindObjectsOfType<Doober>();
            foreach (var doober in doobers)
            {
                if (doober != null && doober.gameObject.activeInHierarchy)
                {
                    doober.Pickup();
                }
            }
        }

        private bool HasNet(AvatarController avatar)
        {
            if (avatar == null || avatar.inventory == null) return false;
            foreach (var slot in avatar.inventory.slots)
            {
                if (slot != null && slot.item != null && slot.item.isNet)
                {
                    return true;
                }
            }
            return false;
        }

        private void ShowBark(AvatarController avatar, string text)
        {
            if (avatar != null && avatar.speechBubble != null)
            {
                // durationSeconds = 2.0f for a quick notification
                avatar.speechBubble.Show(text, SpriteInfo.Invalid, 2.0f);
            }
        }
    }
}
