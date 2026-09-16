using BepInEx;
using BepInEx.Configuration;
using Common;
using Jotunn.Configs;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AdvancedPortals
{
    /// <summary>
    /// Declares this mod's portals and owns the teleport half of their configuration.
    ///
    /// The build side -- enabled, crafting station, build category and build cost -- is handled entirely by
    /// the shared <see cref="PieceLoader"/>, which binds those as server-synced entries that apply live
    /// without a restart. Only the rule this mod adds on top, which items a portal lets through, lives here.
    /// </summary>
    internal static class Portals
    {
        internal class PortalDefinition
        {
            /// <summary>Prefab name in the asset bundle, and the name it is registered under.</summary>
            public string PrefabName;

            /// <summary>Config section holding both the piece entries and the teleport entries below.</summary>
            public string DisplayName;

            public ConfigEntry<string> AllowedItems;
            public ConfigEntry<bool> AllowEverything;
            public ConfigEntry<float> minTeleportItemDur;
            public ConfigEntry<int> maxTeleportRestedTime;

            /// <summary>Null on the first portal, which has no earlier portal to inherit from.</summary>
            public ConfigEntry<bool> UsePreviousPortalItems;
        }

        /// <summary>
        /// Declaration order is progression order: "Use All Previous" inherits the allow-list of every
        /// portal declared before it, so adding a tier means appending one entry in <see cref="RegisterAll"/>.
        /// </summary>
        private static readonly List<PortalDefinition> Definitions = new List<PortalDefinition>();

        /// <summary>Prefab names in declaration order, for registering the portal hashes with Game.</summary>
        internal static IEnumerable<string> PrefabNames => Definitions.Select(portal => portal.PrefabName);

        // Every teleport entry debounces against this one key rather than its own ConfigEntry. The
        // allow-lists chain through "Use All Previous", so a single edit has to re-apply every portal, and
        // a shared key collapses a burst of edits (typing, a file reload, a server sync) into one pass.
        private static readonly object TeleportRulesKey = new object();

        public static ConfigEntry<float> minTeleportItemDur;
        public static ConfigEntry<int> maxTeleportRestedTime;
        private static ConfigEntry<string> disallowedItemsList;
        public static List<string> disallowedItems;
        private static ConfigEntry<string> allowedItemsList;
        public static List<string> allowedItems;

        /// <summary>
        /// Registers every portal. Call once from Awake, after <see cref="ModContext.Initialize"/> and after
        /// the asset bundle is available.
        /// </summary>
        internal static void RegisterAll()
        {
            minTeleportItemDur = ConfigBinder.BindServerConfig("0 - Portal Defaults", "minTeleportItemDur", 0.65f, "Minimum durability to allow item to be teleported through without using a thunderstone.", false, 0.0f, 1.0f);
            maxTeleportRestedTime = ConfigBinder.BindServerConfig("0 - Portal Defaults", "maxTeleportRestedTime", 300, "Maximum rested duration after teleporting through base teleporter.", false, 0, 1800);
            disallowedItemsList = ConfigBinder.BindServerConfig("0 - Portal Defaults", "disallowedItems", "", "Items that can never be teleported, unless it is explicitly stated in the allow list or the teleporter allows all items.");
            disallowedItemsList.SettingChanged += TeleportRuleChanged;
            disallowedItems = GetListFromString(disallowedItemsList.Value);
            allowedItemsList = ConfigBinder.BindServerConfig("0 - Portal Defaults", "allowedItems", "", "If an item contains any member of this list as a substring, it is always allowed to be teleported.");
            allowedItemsList.SettingChanged += TeleportRuleChanged;
            allowedItems = GetListFromString(allowedItemsList.Value);

            Register("portal_ancient", "1 - Ancient Portal",
                new List<PieceLoader.PieceCost>
                {
                    new PieceLoader.PieceCost { Prefab = "ElderBark", Amount = 20 },
                    new PieceLoader.PieceCost { Prefab = "Iron", Amount = 5 },
                    new PieceLoader.PieceCost { Prefab = "SurtlingCore", Amount = 2 }
                },
                allowedItems: "Copper, CopperOre, CopperScrap, Tin, TinOre, Bronze, BronzeScrap",
                allowEverything: false,
                usePreviousPortalItems: null,
                minTeleportItemDur: 0.5f,
                maxTeleportRestedTime: 480);

            Register("portal_obsidian", "2 - Obsidian Portal",
                new List<PieceLoader.PieceCost>
                {
                    new PieceLoader.PieceCost { Prefab = "Obsidian", Amount = 20 },
                    new PieceLoader.PieceCost { Prefab = "Silver", Amount = 5 },
                    new PieceLoader.PieceCost { Prefab = "SurtlingCore", Amount = 2 }
                },
                allowedItems: "Iron, IronScrap, IronOre",
                allowEverything: false,
                usePreviousPortalItems: true,
                minTeleportItemDur: 0.35f,
                maxTeleportRestedTime: 600);

            Register("portal_blackmarble", "3 - Black Marble Portal",
                new List<PieceLoader.PieceCost>
                {
                    new PieceLoader.PieceCost { Prefab = "BlackMarble", Amount = 20 },
                    new PieceLoader.PieceCost { Prefab = "BlackMetal", Amount = 5 },
                    new PieceLoader.PieceCost { Prefab = "Eitr", Amount = 2 }
                },
                allowedItems: "Silver, SilverOre, BlackMetal, BlackMetalScrap",
                allowEverything: true,
                usePreviousPortalItems: true,
                minTeleportItemDur: 0.5f,
                maxTeleportRestedTime: 900);
        }

        private static void Register(string prefabName, string displayName, List<PieceLoader.PieceCost> pieceCost,
                                     string allowedItems, bool allowEverything, bool? usePreviousPortalItems, float minTeleportItemDur, int maxTeleportRestedTime)
        {
            // The build side. Name doubles as the config section, so the teleport entries bound below land
            // in the same section of the .cfg. Name, description and icon are deliberately left unset: the
            // bundle prefabs bake all three, and Jotunn's PieceConfig.Apply only overwrites them when the
            // config supplies a value.
            PieceLoader.Register(new PieceLoader.BuildPiece
            {
                Name = displayName,
                Prefab = prefabName,
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Misc,
                Workbench = "piece_workbench",
                PieceCost = pieceCost
            });

            PortalDefinition portal = new PortalDefinition
            {
                PrefabName = prefabName,
                DisplayName = displayName
            };

            portal.AllowedItems = ConfigBinder.BindServerConfig(displayName, "Allowed Items", allowedItems,
                "A comma separated list of the item types allowed through this portal. Find item ids: " +
                "https://valheim.fandom.com/wiki/Item_IDs");
            portal.AllowEverything = ConfigBinder.BindServerConfig(displayName, "Allow Everything", allowEverything,
                "Allow all items through this portal. Overrides Allowed Items and minimum durability.");
            if (usePreviousPortalItems.HasValue)
            {
                portal.UsePreviousPortalItems = ConfigBinder.BindServerConfig(displayName, "Use All Previous",
                    usePreviousPortalItems.Value,
                    "Additionally allow everything the portals listed before this one allow.");
            }
            portal.minTeleportItemDur = ConfigBinder.BindServerConfig(displayName, "minTeleportItemDur", minTeleportItemDur,
                "Minimum durability to allow item to be teleported through this Portal without using a thunderstone.", false, 0.0f, 1.0f);
            portal.maxTeleportRestedTime = ConfigBinder.BindServerConfig(displayName, "maxTeleportRestedTime", maxTeleportRestedTime,
                "Maximum rested duration after teleporting through this Portal.", false, 0, 1800);

            portal.AllowedItems.SettingChanged += TeleportRuleChanged;
            portal.AllowEverything.SettingChanged += TeleportRuleChanged;
            if (portal.UsePreviousPortalItems != null)
            {
                portal.UsePreviousPortalItems.SettingChanged += TeleportRuleChanged;
            }

            Definitions.Add(portal);
        }

        private static void TeleportRuleChanged(object sender, EventArgs e)
        {
            ConfigChangeDebouncer.Schedule(TeleportRulesKey, ApplyAllTeleportRules);
        }

        /// <summary>
        /// Pushes the configured allow-lists onto the portal prefabs' <see cref="AdvancedPortal"/> components
        /// and refreshes their build-menu descriptions. Safe to call repeatedly.
        /// </summary>
        internal static void ApplyAllTeleportRules()
        {
            disallowedItems = GetListFromString(disallowedItemsList.Value);
            allowedItems = GetListFromString(allowedItemsList.Value);

            for (int i = 0; i < Definitions.Count; i++)
            {
                PortalDefinition portal = Definitions[i];

                GameObject prefab = PrefabManager.Instance.GetPrefab(portal.PrefabName);
                if (prefab == null)
                {
                    ModLogger.LogError($"{portal.PrefabName} not found, could not update its teleport rules.");
                    continue;
                }

                if (!prefab.TryGetComponent(out AdvancedPortal component))
                {
                    ModLogger.LogError($"AdvancedPortal component not found on {portal.PrefabName}, " +
                        "could not update its teleport rules.");
                    continue;
                }

                component.AllowEverything = portal.AllowEverything.Value;
                component.AllowedItems = GetListFromString(portal.AllowedItems.Value);
                component.minItemDur = portal.minTeleportItemDur.Value;
                component.maxRestedTime = portal.maxTeleportRestedTime.Value;

                // Inherit from every portal declared earlier rather than from a named predecessor, so a new
                // tier appended to RegisterAll picks its ancestors up with no further code change.
                if (portal.UsePreviousPortalItems != null && portal.UsePreviousPortalItems.Value)
                {
                    for (int earlier = 0; earlier < i; earlier++)
                    {
                        component.AllowedItems.AddRange(GetListFromString(Definitions[earlier].AllowedItems.Value));
                    }
                }

                if (prefab.TryGetComponent(out Piece piece))
                {
                    piece.m_description = GetAdvancedPortalDescription(component.AllowEverything, component.AllowedItems);
                }
            }
        }

        /// <summary>
        /// Returns a UI description of the portal with the allowed teleportation rules.
        /// </summary>
        private static string GetAdvancedPortalDescription(bool allowEverything, List<string> items)
        {
            var allowedNames = new List<string>();

            foreach (var itemName in items) {
                string tempName = Localization.instance.Localize(PrefabManager.Instance.GetPrefab(itemName)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? string.Empty);
                if (!tempName.IsNullOrWhiteSpace())
                    allowedNames.Add(tempName);
            }

            if (allowEverything)
                return $"$piece_portal_description\n{Localization.instance.Localize("$txt_teleport_anything")}";
            return $"$piece_portal_description\n{Localization.instance.Localize("$txt_can_teleport")}: {string.Join(", ", allowedNames)}";
        }

        private static List<string> GetListFromString(string items)
        {
            return items.Replace(" ", "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }
    }
}
