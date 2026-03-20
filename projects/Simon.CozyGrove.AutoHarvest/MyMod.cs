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
        private AvatarController _avatar;

        public override void OnUpdate()
        {
            if (SceneManager.GetActiveScene().name != "Game") return;

            if (_avatar == null)
            {
                _avatar = GameObject.FindObjectOfType<AvatarController>();
            }

            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.H))
            {
                _isEnabled = !_isEnabled;
                MelonLogger.Msg($"Auto Harvest: {(_isEnabled ? "Enabled" : "Disabled")}");
                ShowBark(_avatar, $"AutoHarvest: {(_isEnabled ? "Enabled" : "Disabled")}");

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
            while (_isEnabled)
            {
                if (_avatar != null && !IsAvatarBusy())
                {
                    // Show tracking status
                    if (_avatar.speechBubble != null && !_avatar.speechBubble.isShown)
                    {
                        ShowBark(_avatar, "AutoHarvest: ON");
                    }

                    var target = FindNearestTarget();
                    if (target != null)
                    {
                        yield return HandleTarget(target);
                    }
                }

                yield return new WaitForSeconds(1f);
            }
        }

        private bool IsAvatarBusy()
        {
            if (_avatar == null || _avatar.actionsController == null) return true;

            // Wait for user to manually dismiss any popups (e.g. bag full)
            if (GameUI.Instance.IsAnyModalUIOpen() || GameUI.Instance.InDialog()) return true;

            return _avatar.actionsController.HasAnyActions();
        }

        private GameObject FindNearestTarget()
        {
            var collectables = System.Linq.Enumerable.ToList(GameObject.FindObjectsOfType<CollectableItem>())
                .Where(c => c.gameObject.activeInHierarchy)
                .Where(c => IsTargetValid(c))
                .Select(c => c.gameObject);

            var doobers = System.Linq.Enumerable.ToList(GameObject.FindObjectsOfType<Doober>())
                .Where(d => d.gameObject.activeInHierarchy)
                .Select(d => d.gameObject);

            var allTargets = collectables.Concat(doobers).ToList();

            if (allTargets.Count == 0) return null;

            GameObject nearest = null;
            float minDistance = float.MaxValue;
            Vector3 avatarPos = _avatar.transform.position;

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

        private bool IsTargetValid(CollectableItem c)
        {
            if (c == null || c.config == null) return false;
            
            // Check if it's a harvestable
            if (c.config.IsTypeMatch(CollectableItemType.harvestable))
            {
                var harvestable = c.GetComponent<HarvestableItem>();
                return harvestable != null && harvestable.harvestableState != null && 
                       harvestable.harvestableState.hasLoot && harvestable.harvestableState.hasMetConditionals;
            }
            
            // Otherwise it's a regular collectable
            return c.CanBePickedUp && !c.GetObjectTags().Has(CollectableItemConfig.TAG_DECORATION_TAG) && HasProperTool(c);
        }

        private bool HasProperTool(CollectableItem collectable)
        {
            if (_avatar == null || _avatar.inventory == null || collectable.config == null) return false;

            var config = collectable.config;
            if (config.requiresTool == null || config.requiresTool.Length == 0)
            {
                return true;
            }

            foreach (var toolId in config.requiresTool)
            {
                foreach (var slot in _avatar.inventory.slots)
                {
                    if (slot != null && slot.item != null && slot.item.configID.Value == toolId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool HasProperTool(GameObject target)
        {
            var collectable = target.GetComponent<CollectableItem>();
            if (collectable != null) return HasProperTool(collectable);
            return true; // Doobers don't require tools
        }

        private IEnumerator HandleTarget(GameObject target)
        {
            // Walk to target if too far
            float dist = Vector3.Distance(_avatar.transform.position, target.transform.position);
            if (dist > 1.5f)
            {
                _avatar.WalkToPosition(target.transform.position, true, true);
                
                float timeout = 5f;
                while (Vector3.Distance(_avatar.transform.position, target.transform.position) > 1.5f && timeout > 0)
                {
                    if (!_isEnabled) yield break;
                    timeout -= Time.deltaTime;
                    yield return null;
                }
            }

            // Double check tool again just in case it broke or was dropped
            if (!HasProperTool(target)) yield break;

            // Interact
            var harvestable = target.GetComponent<HarvestableItem>();
            var collectable = target.GetComponent<CollectableItem>();
            
            if (harvestable != null && harvestable.harvestableState != null && harvestable.harvestableState.hasLoot)
            {
                // Prioritize harvest if it has loot
                harvestable.Interact(_avatar, null, null);
            }
            else if (collectable != null)
            {
                collectable.Interact(_avatar, null);
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
            while (IsAvatarBusy())
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
