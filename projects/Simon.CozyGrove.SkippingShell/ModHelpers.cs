using Il2Cpp;
using UnityEngine;
using System;
using Il2CppSpryFox.Common;

namespace Simon.CozyGrove.SkippingShell
{
    public static class ModHelpers
    {
        public static bool IsAvatarBusy(AvatarController avatar)
        {
            if (avatar == null || avatar.actionsController == null) return true;
            if (GameUI.Instance.IsAnyModalUIOpen() || GameUI.Instance.InDialog()) return true;
            if (AutonomyManager.IsLockedByAnother(avatar, "SkippingShell")) return true;
            return avatar.actionsController.HasAnyActions();
        }

        public static bool IsStoneEquipped(AvatarController avatar, out Item equippedStone)
        {
            equippedStone = null;
            if (avatar.activeItem != null && IsSkippingStone(avatar.activeItem))
            {
                equippedStone = avatar.activeItem;
                return true;
            }
            if (avatar.inventory != null && avatar.inventory.ActiveItem != null && IsSkippingStone(avatar.inventory.ActiveItem))
            {
                equippedStone = avatar.inventory.ActiveItem;
                return true;
            }
            if (avatar.inventory != null)
            {
                var slots = avatar.inventory.slots;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i]?.item != null && slots[i].item.equipped && IsSkippingStone(slots[i].item))
                    {
                        equippedStone = slots[i].item;
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool IsSkippingStone(Item item)
        {
            if (item == null) return false;
            string id = item.configID.Value;
            if (item.collectableItemConfig != null && item.collectableItemConfig.tags != null)
            {
                if (item.collectableItemConfig.tags.Has(Tag.Create("skippable"))) return true;
                if (item.collectableItemConfig.tags.Has(Tag.Create("skipping_stone"))) return true;
                if (item.collectableItemConfig.tags.Has(Tag.Create("special_skipping_stone"))) return true;
            }
            return id.IndexOf("skipping_stone", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsProSkippingStone(Item item)
        {
            if (item == null) return false;

            if (item.collectableItemConfig != null && item.collectableItemConfig.tags != null)
            {
                if (item.collectableItemConfig.tags.Has(Tag.Create("pro_skipping_stone"))) return true;
            }

            string id = item.configID.Value;
            if (string.IsNullOrEmpty(id)) return false;
            return id.IndexOf("pro_skipping_stone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   id.IndexOf("skipping_stone_pro", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsSpecialSkippingStone(Item item)
        {
            if (item == null) return false;

            if (item.collectableItemConfig != null && item.collectableItemConfig.tags != null)
            {
                if (item.collectableItemConfig.tags.Has(Tag.Create("special_skipping_stone"))) return true;
            }

            string id = item.configID.Value;
            if (string.IsNullOrEmpty(id)) return false;
            return id.IndexOf("special_skipping_stone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   id.IndexOf("skipping_stone_special", StringComparison.OrdinalIgnoreCase) >= 0;
        }


        public static Item FindSkippingStone(AvatarController avatar)
        {
            if (avatar.inventory == null) return null;
            foreach (var slot in avatar.inventory.slots)
            {
                if (slot?.item != null && IsSkippingStone(slot.item)) return slot.item;
            }
            return null;
        }

        public static void CollectNearbyDoobers(AvatarController avatar)
        {
            var doobers = UnityEngine.Object.FindObjectsOfType<Doober>();
            foreach (var doober in doobers)
            {
                if (doober != null && Vector3.Distance(avatar.transform.position, doober.transform.position) < 12f)
                {
                    doober.Pickup();
                }
            }
        }

        public static void ShowBark(AvatarController avatar, string text)
        {
            if (avatar != null && avatar.speechBubble != null)
            {
                avatar.speechBubble.Show(text, SpriteInfo.Invalid, 2.5f);
            }
        }
    }
}
