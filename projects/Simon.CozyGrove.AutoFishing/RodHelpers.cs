using Il2Cpp;
using MelonLoader;
using UnityEngine;
using System;
using System.Reflection;

namespace Simon.CozyGrove.AutoFishing
{
    // Rod detection, equip helpers, and rod type checks.
    public partial class MyMod
    {
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
            if (avatar.inventory?.ActiveItem != null && IsFishingRod(avatar.inventory.ActiveItem))
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

            // 4. Reflection fallback for private usingItem field
            try
            {
                var field = typeof(AvatarController).GetField("usingItem", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field?.GetValue(avatar) is Item hiddenItem && IsFishingRod(hiddenItem))
                {
                    MelonLogger.Msg($"[Diagnostic] Found rod in private 'usingItem' field: {hiddenItem.collectableItemConfig.id}");
                    equippedRod = hiddenItem;
                    return true;
                }
            }
            catch (Exception ex) { MelonLogger.Msg($"[Diagnostic] Reflection failed: {ex.Message}"); }

            return false;
        }

        private void TryEquipRodOnActivation(AvatarController avatar)
        {
            MelonLogger.Msg("Mod Activation: Scanning inventory for fishing rods...");
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

            var inputActions = GetAvatar()?.GetComponent<InputActionsController>()
                ?? UnityEngine.Object.FindObjectOfType<InputActionsController>();
            if (inputActions?.HotkeyEquipFishingRod != null)
            {
                MelonLogger.Msg("Invoking native HotkeyEquipFishingRod event...");
                inputActions.HotkeyEquipFishingRod.Invoke();
                if (rod != null) _pendingEquipId = rod.collectableItemConfig.id;
            }
            else
            {
                MelonLogger.Error("Could not find InputActionsController or HotkeyEquipFishingRod event!");
            }
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
                if (isSea && !string.IsNullOrEmpty(ConfigData.fishing.fishingRodTagOcean))
                    return rod.collectableItemConfig.IsTagMatch(ConfigData.fishing.fishingRodTagOcean, false);
                if (!isSea && !string.IsNullOrEmpty(ConfigData.fishing.fishingRodTagPond))
                    return rod.collectableItemConfig.IsTagMatch(ConfigData.fishing.fishingRodTagPond, false);
            }

            return true;
        }
    }
}
