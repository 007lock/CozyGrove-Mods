using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Linq;
using System.Collections;
using System.Reflection;

namespace Simon.CozyGrove.AutoNet
{
    public class MyMod : MelonMod
    {
        private bool _isActive = false;
        private Critter _targetCritter = null;
        private object _coroInstance = null;
        private AvatarController _cachedAvatar = null;
        private bool _isInGameScene = false;

        // Configuration
        private const float CatchDistance = 2.0f; // Distance from critter to trigger net throw
        private const float CatchTimeout = 10f; // Max time to spend trying to catch one critter
        private const float CollectDelay = 1.0f; // Time to wait for doobers to spawn before collecting

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

        public override void OnUpdate()
        {
            if (!_isInGameScene) return;

            var avatar = GetAvatar();
            if (avatar == null) return;

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
            if (_cachedAvatar != null)
            {
                AutonomyManager.ClearBid(_cachedAvatar, "AutoNet");
                AutonomyManager.ReleaseLock(_cachedAvatar, "AutoNet");
            }
            _targetCritter = null;
        }

        private IEnumerator AutoNetCoro()
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
                    AutonomyManager.ClearBid(avatar, "AutoNet");
                    yield return new WaitForSeconds(0.5f);
                    continue;
                }

                // Ensure net is equipped while idle
                if (!HasNetEquipped(avatar))
                {
                    TryEquipNet(avatar);
                    yield return new WaitForSeconds(1.0f); // Wait for equipment
                    continue; 
                }

                _targetCritter = FindNearestCritter(avatar);
                if (_targetCritter != null)
                {
                    float dist = Vector3.Distance(avatar.transform.position, _targetCritter.transform.position);
                    AutonomyManager.UpdateBid(avatar, "AutoNet", dist);

                    yield return new WaitForSeconds(0.2f);
                    
                    if (avatar == null || !avatar.gameObject.activeInHierarchy || 
                        IsAvatarBusy(avatar) || 
                        !AutonomyManager.IsMyBidLowest(avatar, "AutoNet", dist))
                    {
                        AutonomyManager.ClearBid(avatar, "AutoNet");
                        continue;
                    }

                    AutonomyManager.ClearBid(avatar, "AutoNet");
                    AutonomyManager.AcquireLock(avatar, "AutoNet");

                    yield return HandleTarget(avatar, _targetCritter);

                    AutonomyManager.ReleaseLock(avatar, "AutoNet");
                }
                else
                {
                    AutonomyManager.ClearBid(avatar, "AutoNet");
                }

                yield return new WaitForSeconds(0.3f);
            }
        }

        private bool IsAvatarBusy(AvatarController avatar)
        {
            if (avatar == null || avatar.actionsController == null) return true;

            // Wait for user to manually dismiss any popups
            if (GameUI.Instance.IsAnyModalUIOpen() || GameUI.Instance.InDialog()) return true;

            if (AutonomyManager.IsLockedByAnother(avatar, "AutoNet")) return true;

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
                yield return null; // Wait 1 frame so walk action is registered
                while (target != null && target.gameObject.activeInHierarchy && 
                       Vector3.Distance(avatar.transform.position, target.transform.position) > CatchDistance && timeout > 0)
                {
                    if (!_isActive) yield break;

                    // If the avatar has stopped moving completely but the critter is still too far
                    // (e.g. critter moved away and WalkToPosition path completed), break so we don't idle-stall for 10 seconds.
                    if (!avatar.actionsController.HasAnyActions())
                    {
                        break;
                    }
                    
                    // Show tracking status
                    if (!avatar.speechBubble.isShown) ShowBark(avatar, "AutoNet: ON");
                    
                    timeout -= Time.deltaTime;
                    yield return null;
                }
            }

            // Verify the target is still valid after the tracking attempt
            if (target == null || !target.gameObject.activeInHierarchy) yield break;

            // If the avatar stopped but is STILL too far away (e.g. critter moved out of range), abort this block
            // to allow the main Coroutine to pick up a fresh target rather than swinging at air or getting stuck.
            if (Vector3.Distance(avatar.transform.position, target.transform.position) > CatchDistance)
            {
                yield break;
            }

            // Ensure net is equipped before action
            if (!HasNetEquipped(avatar))
            {
                TryEquipNet(avatar);
                yield return new WaitForSeconds(0.5f);
                if (!HasNetEquipped(avatar)) yield break;
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

        private bool HasNetEquipped(AvatarController avatar)
        {
            if (avatar == null) return false;
            
            // Primary check
            if (avatar.activeItem != null && avatar.activeItem.isNet) return true;
            
            // Inventory active item check
            if (avatar.inventory != null && avatar.inventory.ActiveItem != null && avatar.inventory.ActiveItem.isNet) return true;

            // Scan slots for equipped flag (covers cases where the model isn't active yet)
            if (avatar.inventory != null)
            {
                foreach (var slot in avatar.inventory.slots)
                {
                    if (slot != null && slot.item != null && slot.item.equipped && slot.item.isNet) return true;
                }
            }

            return false;
        }

        private void TryEquipNet(AvatarController avatar)
        {
            if (avatar == null) return;

            // Diagnostic: Find net in inventory
            Item netItem = null;
            if (avatar.inventory != null)
            {
                foreach (var slot in avatar.inventory.slots)
                {
                    if (slot != null && slot.item != null && slot.item.isNet)
                    {
                        netItem = slot.item;
                        break;
                    }
                }
            }

            if (netItem == null)
            {
                ShowBark(avatar, "No Net Found!");
                return;
            }

            // Attempt native hotkey if possible using reflection to avoid compile errors
            // and handle slightly different property names
            var inputActions = avatar.GetComponent<InputActionsController>() ?? UnityEngine.Object.FindObjectOfType<InputActionsController>();
            if (inputActions != null)
            {
                try 
                {
                    var props = typeof(InputActionsController).GetProperties(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var prop in props)
                    {
                        if (prop.Name.Contains("HotkeyEquip") && (prop.Name.Contains("Net") || prop.Name.Contains("Critter")))
                        {
                            var eventObj = prop.GetValue(inputActions);
                            if (eventObj != null)
                            {
                                // Specify Type.EmptyTypes to avoid "Ambiguous match found" if multiple Invoke overloads exist
                                var invokeMethod = eventObj.GetType().GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                                if (invokeMethod != null)
                                {
                                    MelonLogger.Msg($"Invoking native {prop.Name} event...");
                                    invokeMethod.Invoke(eventObj, null);
                                    return;
                                }
                                else 
                                {
                                    // Fallback search for any Invoke method without parameters
                                    invokeMethod = eventObj.GetType().GetMethods().FirstOrDefault(m => m.Name == "Invoke" && m.GetParameters().Length == 0);
                                    if (invokeMethod != null)
                                    {
                                        MelonLogger.Msg($"Invoking native {prop.Name} event (fallback lookup)...");
                                        invokeMethod.Invoke(eventObj, null);
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"Reflection lookup failed: {ex.Message}");
                }
            }

            // Fallback to direct inventory use
            MelonLogger.Msg("Using inventory fallback for net equipment...");
            avatar.inventory.UseItem(netItem, false);
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
