using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using UnityEngine;
using Il2Cpp;
using Il2CppSpryFox.Common;
using UnityEngine.SceneManagement;

namespace Simon.CozyGrove.AutoHarvest
{
    public class MyMod : MelonMod
    {
        private bool _isEnabled = false;
        private Coroutine _autoHarvestCoroutine;
        private AvatarController _cachedAvatar = null;
        private bool _isInGameScene = false;

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _isInGameScene = (sceneName == "Game");
            _cachedAvatar = null;
            if (!_isInGameScene)
            {
                _isEnabled = false;
                if (_autoHarvestCoroutine != null)
                {
                    MelonCoroutines.Stop(_autoHarvestCoroutine);
                    _autoHarvestCoroutine = null;
                }
            }
        }

        private AvatarController GetAvatar()
        {
            if (_cachedAvatar == null && _isInGameScene)
            {
                _cachedAvatar = GameObject.FindObjectOfType<AvatarController>();
            }
            return _cachedAvatar;
        }

        public override void OnUpdate()
        {
            if (!_isInGameScene) return;

            var avatar = GetAvatar();
            if (avatar == null) return;

            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.H))
            {
                _isEnabled = !_isEnabled;
                MelonLogger.Msg($"Auto Harvest: {(_isEnabled ? "Enabled" : "Disabled")}");
                ShowBark(avatar, $"AutoHarvest: {(_isEnabled ? "Enabled" : "Disabled")}");

                if (_isEnabled)
                {
                    _autoHarvestCoroutine = (Coroutine)MelonCoroutines.Start(AutoHarvestCoro());
                }
                else if (_autoHarvestCoroutine != null)
                {
                    MelonCoroutines.Stop(_autoHarvestCoroutine);
                    _autoHarvestCoroutine = null;
                }
            }
        }

        private IEnumerator AutoHarvestCoro()
        {
            while (_isEnabled && _isInGameScene)
            {
                var avatar = GetAvatar();
                if (avatar != null && !IsAvatarBusy(avatar))
                {
                    // Show tracking status
                    if (avatar.speechBubble != null && !avatar.speechBubble.isShown)
                    {
                        ShowBark(avatar, "AutoHarvest: ON");
                    }

                    var target = FindNearestTarget(avatar);
                    if (target != null)
                    {
                        yield return HandleTarget(target, avatar);
                    }
                }

                yield return new WaitForSeconds(1f);
            }
        }

        private bool IsAvatarBusy(AvatarController avatar)
        {
            if (avatar == null || avatar.actionsController == null) return true;

            // Wait for user to manually dismiss any popups (e.g. bag full)
            if (GameUI.Instance.IsAnyModalUIOpen() || GameUI.Instance.InDialog()) return true;

            return avatar.actionsController.HasAnyActions();
        }

        private GameObject FindNearestTarget(AvatarController avatar)
        {
            var collectables = System.Linq.Enumerable.ToList(GameObject.FindObjectsOfType<CollectableItem>())
                .Where(c => c.gameObject.activeInHierarchy)
                .Where(c => IsTargetValid(c, avatar))
                .Select(c => c.gameObject);

            var doobers = System.Linq.Enumerable.ToList(GameObject.FindObjectsOfType<Doober>())
                .Where(d => d.gameObject.activeInHierarchy)
                .Select(d => d.gameObject);

            var allTargets = collectables.Concat(doobers).ToList();

            if (allTargets.Count == 0) return null;

            GameObject nearest = null;
            float minDistance = float.MaxValue;
            Vector3 avatarPos = avatar.transform.position;

            foreach (var target in allTargets)
            {
                float dist = Vector3.Distance(avatarPos, target.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    nearest = target;
                }
            }

            return nearest;
        }

        private bool IsTargetValid(CollectableItem c, AvatarController avatar)
        {
            if (c == null || c.config == null) return false;
            
            // Check if it's a harvestable
            if (c.config.IsTypeMatch(CollectableItemType.harvestable))
            {
                var harvestable = c.GetComponent<HarvestableItem>();
                return harvestable != null && harvestable.harvestableState != null && 
                       harvestable.harvestableState.hasLoot && harvestable.harvestableState.hasMetConditionals && HasProperTool(c, avatar);
            }
            
            // Otherwise it's a regular collectable
            return c.CanBePickedUp && !c.GetObjectTags().Has(CollectableItemConfig.TAG_DECORATION_TAG) && HasProperTool(c, avatar);
        }

        private bool HasProperTool(CollectableItem collectable, AvatarController avatar)
        {
            if (avatar == null || avatar.inventory == null || collectable.config == null) return false;

            var config = collectable.config;
            if (config.requiresTool == null || config.requiresTool.Length == 0)
            {
                return true;
            }

            foreach (var reqToolId in config.requiresTool)
            {
                var reqInfo = GetToolInfo(reqToolId);

                foreach (var slot in avatar.inventory.slots)
                {
                    if (slot != null && slot.item != null)
                    {
                        var playerToolInfo = GetToolInfo(slot.item.configID.Value);
                        if (playerToolInfo.type == reqInfo.type && playerToolInfo.level >= reqInfo.level)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private (string type, int level) GetToolInfo(string toolId)
        {
            if (string.IsNullOrEmpty(toolId)) return (null, 0);

            // Standard format: tool_pickaxe_1
            int lastUnderscore = toolId.LastIndexOf('_');
            if (lastUnderscore > 0 && int.TryParse(toolId.Substring(lastUnderscore + 1), out int lvl))
            {
                return (toolId.Substring(0, lastUnderscore), lvl);
            }

            return (toolId, 1); // Default to level 1
        }

        private bool HasProperTool(GameObject target, AvatarController avatar)
        {
            var collectable = target.GetComponent<CollectableItem>();
            if (collectable != null) return HasProperTool(collectable, avatar);
            return true; // Doobers don't require tools
        }

        private IEnumerator HandleTarget(GameObject target, AvatarController avatar)
        {
            // Walk to target if too far
            float dist = Vector3.Distance(avatar.transform.position, target.transform.position);
            if (dist > 1.5f)
            {
                avatar.WalkToPosition(target.transform.position, true, true);
                
                float timeout = 5f;
                while (Vector3.Distance(avatar.transform.position, target.transform.position) > 1.5f && timeout > 0)
                {
                    if (!_isEnabled) yield break;
                    timeout -= Time.deltaTime;
                    yield return null;
                }
            }

            // Double check tool again just in case it broke or was dropped
            if (!HasProperTool(target, avatar)) yield break;

            // Interact
            var harvestable = target.GetComponent<HarvestableItem>();
            var collectable = target.GetComponent<CollectableItem>();
            
            if (harvestable != null && harvestable.harvestableState != null && harvestable.harvestableState.hasLoot)
            {
                // Prioritize harvest if it has loot
                harvestable.Interact(avatar, null, null);
            }
            else if (collectable != null)
            {
                collectable.Interact(avatar, null);
            }
            else
            {
                var doober = target.GetComponent<Doober>();
                if (doober != null)
                {
                    try {
                        var method = doober.GetIl2CppType().GetMethod("Pickup");
                        if (method != null) method.Invoke(doober, null);
                    } catch {
                        // Fallback
                    }
                }
            }

            // Wait a bit for the action to complete or doober to fly
            yield return new WaitForSeconds(0.5f);
            
            // Wait while busy
            while (IsAvatarBusy(avatar))
            {
                yield return (object)null;
            }
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
