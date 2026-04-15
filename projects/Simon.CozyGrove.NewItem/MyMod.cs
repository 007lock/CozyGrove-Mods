using Il2Cpp;
using MelonLoader;
using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Simon.CozyGrove.NewItem
{
    public class MyMod : MelonMod
    {
        private AvatarController _cachedAvatar = null;
        private bool _isInGameScene = false;
        private readonly HashSet<int> _processedDooberIds = new();
        private float _lastInventoryCheck = 0f;
        private const float InventoryCheckInterval = 0.3f;
        private Dictionary<string, int> _lastItemCounts = new();
        private bool _isFirstScan = true;

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _isInGameScene = sceneName == "Game";
            _cachedAvatar = null;
            _processedDooberIds.Clear();
            _lastItemCounts.Clear();
            _isFirstScan = true;
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

            // Always scan for new loot
            _lastInventoryCheck += Time.deltaTime;
            if (_lastInventoryCheck >= InventoryCheckInterval)
            {
                _lastInventoryCheck = 0f;
                CheckForNewLoot(avatar);
            }
        }

        private void CheckForNewLoot(AvatarController avatar)
        {
            if (avatar == null || avatar.inventory == null) return;

            var currentCounts = GetInventoryItemCounts(avatar);

            // On first scan, just establish baseline - don't notify
            if (_isFirstScan)
            {
                _lastItemCounts = new Dictionary<string, int>(currentCounts);
                _isFirstScan = false;
                MelonLogger.Msg($"[NewItem] Baseline established: {_lastItemCounts.Count} unique items");
                return;
            }

            // Detect NEW items (IDs not seen before) or QUANTITY increase
            foreach (var kvp in currentCounts)
            {
                string itemId = kvp.Key;
                int currentCount = kvp.Value;

                if (_lastItemCounts.TryGetValue(itemId, out int lastCount))
                {
                    // Item existed before - check if quantity increased
                    if (currentCount > lastCount)
                    {
                        // New stock arrived
                        NotifyLoot(avatar, itemId, currentCount - lastCount);
                    }
                }
                else
                {
                    // Completely new item type in inventory
                    NotifyLoot(avatar, itemId, currentCount);
                }
            }

            // Update tracking
            _lastItemCounts = currentCounts;
        }

        private void NotifyLoot(AvatarController avatar, string itemId, int quantity)
        {
            bool canDonate = CheckCanDonate(itemId);

            // ONLY show speech bubble if item can be donated
            if (canDonate)
            {
                string qty = quantity > 1 ? $"x{quantity} " : "";
                string notification = $"+{qty}{itemId}";
                ShowBark(avatar, notification);
            }

            // Always log to console for reference
            string qtyLog = quantity > 1 ? $"x{quantity} " : "";
            MelonLogger.Msg($"New loot: {qtyLog}{itemId} - Can donate: {canDonate}");
        }

        private Dictionary<string, int> GetInventoryItemCounts(AvatarController avatar)
        {
            var counts = new Dictionary<string, int>();
            if (avatar == null || avatar.inventory == null || avatar.inventory.slots == null)
                return counts;

            foreach (var slot in avatar.inventory.slots)
            {
                if (slot?.item?.collectableItemConfig?.id != null)
                {
                    string itemId = slot.item.collectableItemConfig.id;
                    if (counts.ContainsKey(itemId))
                        counts[itemId]++;
                    else
                        counts[itemId] = 1;
                }
            }

            return counts;
        }

        private bool CheckCanDonate(string itemId)
        {
            try
            {
                if (string.IsNullOrEmpty(itemId)) return false;

                var playerCollections = Il2Cpp.Main.Instance?.GameState?.PlayerCollections;
                if (playerCollections == null) return false;

                return playerCollections.CanBeDonated(new ConfigID(itemId));
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"Error checking donate status: {ex.Message}");
                return false;
            }
        }

        private void ShowBark(AvatarController avatar, string text)
        {
            if (avatar != null && avatar.speechBubble != null)
            {
                avatar.speechBubble.Show(text, SpriteInfo.Invalid, 2.5f);
            }
        }
    }
}