using Il2Cpp;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Il2CppSpryFox.Common;

namespace Simon.CozyGrove.AutoFishing
{
    // Checks whether a fish's loot table can yield uncommon, rare, ultra_rare, epic, legendary,
    // unique, or recipe items. Loot table rewards are tag expressions like
    // "randomWithTags[fish,medium,slow]" or "randomRecipe[...]". We parse the tags and query
    // CollectableItem.GetItemsFilteredByTagsCached with each value rarity to detect a match.
    // Results cached per loot-table name; cleared after each catch.
    public partial class MyMod
    {
        private static readonly string[] _valueRarityTags =
            { "uncommon", "rare", "ultra_rare", "epic", "legendary", "unique" };

        private readonly Dictionary<string, bool> _lootTableNewCache = new Dictionary<string, bool>();
        private bool _resolvedTableThisTick = false;

        private void ResetLootTableTickGate() => _resolvedTableThisTick = false;

        private bool HasValueItemInLootTable(string lootTableName, LootSystem lootSys)
        {
            if (_lootTableNewCache.TryGetValue(lootTableName, out bool cached))
                return cached;

            if (_resolvedTableThisTick)
                return true; // fail-open; resolved next tick

            _resolvedTableThisTick = true;

            try
            {
                var tables = lootSys.lootTables;
                if (tables == null || !tables.ContainsKey(lootTableName))
                {
                    MelonLogger.Warning($"[FishFilter] '{lootTableName}': not in lootTables.");
                    return true;
                }

                var tableConfig = tables[lootTableName];
                if (tableConfig == null) return true;

                // Collect the raw item strings (tag expressions) via our own HashSet.
                var rawItems = new Il2CppSystem.Collections.Generic.HashSet<string>();
                lootSys.AddRewardItemStrings(tableConfig.reward, rawItems, 0, lootTableName);

                bool hasValue = false;
                var internalList = new Il2CppSystem.Collections.Generic.List<ConfigID>();
                var enumerator = rawItems.GetEnumerator();
                while (enumerator.MoveNext())
                {
                    string expr = enumerator.Current;
                    if (string.IsNullOrEmpty(expr)) continue;

                    // Recipes are always considered valuable.
                    if (expr.StartsWith("randomRecipe"))
                    {
                        MelonLogger.Msg($"[FishFilter] '{lootTableName}': recipe slot → value.");
                        hasValue = true;
                        break;
                    }

                    var match = Regex.Match(expr, @"\[(.+)\]");
                    if (!match.Success) continue;

                    // Parse include/exclude tags.
                    string[] parts = match.Groups[1].Value.Split(',');
                    var inclTags = new System.Collections.Generic.List<string>();
                    var exclTags = new System.Collections.Generic.List<string>();
                    foreach (var p in parts)
                    {
                        string t = p.Trim();
                        if (t.StartsWith("!")) exclTags.Add(t.Substring(1));
                        else inclTags.Add(t);
                    }

                    if (inclTags.Count == 0) continue;

                    TagSet exclSet = exclTags.Count > 0 ? TagSet.Create(exclTags.ToArray()) : TagSet.Empty;

                    // Check if the pool contains items at any value rarity tier.
                    foreach (string rarity in _valueRarityTags)
                    {
                        var rarityInclTags = new System.Collections.Generic.List<string>(inclTags) { rarity };
                        TagSet inclSet = TagSet.Create(rarityInclTags.ToArray());
                        var weighted = CollectableItem.GetItemsFilteredByTagsCached(
                            internalList, inclSet, exclSet,
                            ignoreSchedule: false, ignoreUnlockState: false,
                            allowUnlearnedCraftable: false, filterFunc: null);
                        if (weighted.HasItems())
                        {
                            MelonLogger.Msg($"[FishFilter] '{lootTableName}': {rarity} items in [{string.Join(",", inclTags)}] → value.");
                            hasValue = true;
                            break;
                        }
                    }
                    if (hasValue) break;
                }

                if (!hasValue)
                    MelonLogger.Msg($"[FishFilter] '{lootTableName}': only common items — skipping.");

                _lootTableNewCache[lootTableName] = hasValue;
                return hasValue;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[FishFilter] '{lootTableName}': exception — {ex.Message}");
                return true;
            }
        }
    }
}
