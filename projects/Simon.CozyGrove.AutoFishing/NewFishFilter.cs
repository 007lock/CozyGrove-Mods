using Il2Cpp;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Simon.CozyGrove.AutoFishing
{
    // Tracks whether a fish's loot table contains items not yet in the captain's collection.
    // Uses reflection to enumerate Il2Cpp IEnumerable<string> (does not bridge to CLR IEnumerable).
    // Results are cached per loot-table name and cleared after each collection.
    public partial class MyMod
    {
        // true = has at least one uncollected item, false = all items already donated
        private readonly Dictionary<string, bool> _lootTableNewCache = new Dictionary<string, bool>();

        private bool HasNewItemInLootTable(string lootTableName, CollectionsState collections, LootSystem lootSys)
        {
            if (_lootTableNewCache.TryGetValue(lootTableName, out bool cached))
                return cached;

            bool hasNew = true; // fail-open: treat as "new" on any error
            try
            {
                var rewards = lootSys.GetAllPossibleItemRewardStrings(lootTableName);
                if (rewards == null)
                {
                    MelonLogger.Warning($"[NewFish] GetAllPossibleItemRewardStrings returned null for '{lootTableName}' — treating as new.");
                }
                else
                {
                    // GetAllPossibleItemRewardStrings returns an Il2Cpp proxy that does NOT bridge to
                    // System.Collections.Generic.IEnumerable<string> at runtime. Use reflection.
                    Type rewardType = rewards.GetType();
                    MethodInfo getEnumerator = rewardType.GetMethod("GetEnumerator");
                    if (getEnumerator == null)
                    {
                        MelonLogger.Warning($"[NewFish] No GetEnumerator on {rewardType.Name} for '{lootTableName}' — treating as new.");
                    }
                    else
                    {
                        object enumerator = getEnumerator.Invoke(rewards, null);
                        Type enumType = enumerator?.GetType();
                        MethodInfo moveNext = enumType?.GetMethod("MoveNext");
                        PropertyInfo current = enumType?.GetProperty("Current");

                        if (moveNext == null || current == null)
                        {
                            MelonLogger.Warning($"[NewFish] Enumerator for '{lootTableName}' missing MoveNext/Current — treating as new.");
                        }
                        else
                        {
                            hasNew = false;
                            int count = 0;
                            while ((bool)moveNext.Invoke(enumerator, null))
                            {
                                string itemId = current.GetValue(enumerator)?.ToString();
                                count++;
                                if (!string.IsNullOrEmpty(itemId) && collections.CanBeDonated(new ConfigID(itemId)))
                                {
                                    hasNew = true;
                                    break;
                                }
                            }
                            MelonLogger.Msg($"[NewFish] '{lootTableName}': checked {count} item(s), hasNew={hasNew}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[NewFish] Exception checking '{lootTableName}': {ex.Message} — treating as new.");
            }

            _lootTableNewCache[lootTableName] = hasNew;
            return hasNew;
        }
    }
}
