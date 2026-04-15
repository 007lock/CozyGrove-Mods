using Il2Cpp;
using MelonLoader;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;

namespace Simon.CozyGrove.AutoNet
{
    // Net detection and equip helpers.
    public partial class MyMod
    {
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

            // Try native hotkey via reflection to avoid compile errors on varying property names
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
                                var invokeMethod = eventObj.GetType().GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                                if (invokeMethod != null)
                                {
                                    MelonLogger.Msg($"Invoking native {prop.Name} event...");
                                    invokeMethod.Invoke(eventObj, null);
                                    return;
                                }
                                else
                                {
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
                    return true;
            }
            return false;
        }
    }
}
