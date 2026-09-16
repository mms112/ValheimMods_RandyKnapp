using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AdvancedPortals
{
    [HarmonyPatch]
    public static class Teleport_Patch
    {
        public static AdvancedPortal CurrentAdvancedPortal;
        public static bool AllowAllPortal = false;

        public static void TargetPortal_HandlePortalClick_Prefix()
        {
            if (Player.m_localPlayer == null)
            {
                return;
            }
            Vector3 playerPos = Player.m_localPlayer.transform.position;
            const float searchRadius = 2.0f;
            Collider[] colliders = Physics.OverlapSphere(playerPos, searchRadius);
            TeleportWorld closestTeleport = null;
            float minDistSquared = searchRadius * searchRadius + 1;
            foreach (Collider collider in colliders)
            {
                TeleportWorldTrigger twt = collider.gameObject.GetComponent<TeleportWorldTrigger>();
                if (twt == null)
                {
                    continue;
                }

                TeleportWorld tw = twt.GetComponentInParent<TeleportWorld>();
                if (tw == null)
                {
                    continue;
                }

                Vector3 d = collider.transform.position - playerPos;
                float distSquared = d.x * d.x + d.y * d.y + d.z * d.z;
                if (distSquared < minDistSquared)
                {
                    closestTeleport = tw;
                    minDistSquared = distSquared;
                }
            }

            if (closestTeleport != null)
            {
                Generic_Prefix(closestTeleport);
            }
        }

        public static void Generic_Prefix(TeleportWorld __instance)
        {
            CurrentAdvancedPortal = __instance.GetComponent<AdvancedPortal>();
            AllowAllPortal = __instance.m_allowAllItems;
        }

        public static void Generic_Postfix()
        {
            CurrentAdvancedPortal = null;
            AllowAllPortal = false;
        }

        private static float calculateDurabilityCost(Inventory inventory, float minDur)
        {
            float durCost = 0f;

            foreach (var item in inventory.GetAllItems()) {
                if (item.m_shared.m_useDurability && !item.m_shared.m_destroyBroken) {
                    float durPercent = item.GetDurabilityPercentage();
                    if (durPercent < minDur) {
                        durCost += (minDur - durPercent) * 100;
                    }
                }
            }

            return durCost;
        }

        private static bool hasEnoughThunderstoneDurability(Inventory inventory, float minDur)
        {
            float totalTSDur = 0f;

            foreach (var item in inventory.GetAllItems()) {
                if (item.m_shared.m_name == "$item_thunderstone") {
                    totalTSDur += item.m_durability;
                }
            }

            return totalTSDur >= calculateDurabilityCost(inventory, minDur);
        }

        private static int ThunderstoneSort(ItemDrop.ItemData x, ItemDrop.ItemData y)
        {
            return x.m_durability.CompareTo(y.m_durability);
        }

        private static void DrainThunderstones(Humanoid player, float minDur) 
        {
            List<ItemDrop.ItemData> thunderstones = new List<ItemDrop.ItemData>();

            foreach (var item in player.GetInventory().GetAllItems()) {
                if (item.m_shared.m_name == "$item_thunderstone") {
                    thunderstones.Add(item);
                }
            }

            thunderstones.Sort(ThunderstoneSort);

            float durability = calculateDurabilityCost(player.GetInventory(), minDur);

            foreach (var item in thunderstones) {
                float drain = Math.Min(durability, item.m_durability);
                player.DrainEquipedItemDurability(item, drain);
                durability -= drain;

                if (durability <= 0)
                    break;
            }
        }

        private static bool isAllowedItem(string itemName) 
        {
            foreach (var allowedName in Portals.allowedItems)
            {
                if (itemName.Contains(allowedName))
                    return true;
            }
            return false;
        }

        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.UpdatePortal))]
        [HarmonyPrefix]
        public static void TeleportWorld_UpdatePortal_Prefix(TeleportWorld __instance)
        {
            CurrentAdvancedPortal = __instance.GetComponent<AdvancedPortal>();
            AllowAllPortal = __instance.m_allowAllItems;
        }

        // Finalizer, not postfix: UpdatePortal runs on an InvokeRepeating timer for every portal in the
        // scene, and a postfix does not run when the original (or another mod's patch) throws -- which
        // used to leave CurrentAdvancedPortal stuck, applying one portal's allow-list to every later
        // Inventory.IsTeleportable call in the game.
        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.UpdatePortal))]
        [HarmonyFinalizer]
        public static void TeleportWorld_UpdatePortal_Finalizer()
        {
            CurrentAdvancedPortal = null;
            AllowAllPortal = false;
        }

        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
        [HarmonyPrefix]
        public static void TeleportWorld_Teleport_Prefix(TeleportWorld __instance)
        {
            CurrentAdvancedPortal = __instance.GetComponent<AdvancedPortal>();
            AllowAllPortal = __instance.m_allowAllItems;
        }

        [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport))]
        [HarmonyFinalizer]
        public static void TeleportWorld_Teleport_Finalizer()
        {
            CurrentAdvancedPortal = null;
            AllowAllPortal = false;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
        [HarmonyPostfix]
        private static void Player_TeleportTo_Postfix(Player __instance)
        {
            if (!AllowAllPortal && !Environment.StackTrace.Contains("Interact"))
            {
                StatusEffect RestedEffect = __instance.GetSEMan().GetStatusEffect(SEMan.s_statusEffectRested.GetHashCode());
                if (RestedEffect)
                {
                    float maxRestedTime = CurrentAdvancedPortal?.maxRestedTime ?? Portals.maxTeleportRestedTime.Value;
                    if ((RestedEffect.m_ttl - RestedEffect.m_time) > maxRestedTime)
                    {
                        RestedEffect.m_ttl = maxRestedTime;
                        RestedEffect.m_time = 0.0f;
                    }
                }

                if (CurrentAdvancedPortal == null || !CurrentAdvancedPortal.AllowEverything)
                {
                    float minTeleportItemDur = CurrentAdvancedPortal?.minItemDur ?? Portals.minTeleportItemDur.Value;
                    DrainThunderstones(__instance, minTeleportItemDur);
                }
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.IsTeleportable))]
        [HarmonyPrefix]
        public static bool Inventory_IsTeleportable_Prefix(Inventory __instance, ref bool __result)
        {
            if (CurrentAdvancedPortal == null)
            {
                if (!hasEnoughThunderstoneDurability(__instance, Portals.minTeleportItemDur.Value)) {
                    __result = false;
                    return false;
                }

                foreach (var itemData in __instance.GetAllItems())
                {
                    if ((itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && !isAllowedItem(itemData.m_dropPrefab.name))
                        || Portals.disallowedItems.Contains(itemData.m_dropPrefab.name))
                    {
                        __result = false;
                        return false;
                    }
                }

                return true;
            }

            if (CurrentAdvancedPortal.AllowEverything)
            {
                __result = true;
                return false;
            }

            if (!hasEnoughThunderstoneDurability(__instance, CurrentAdvancedPortal.minItemDur)) {
                __result = false;
                return false;
            }

            bool allowMinorMead = CurrentAdvancedPortal.AllowedItems.Contains("MinorMead");

            foreach (var itemData in __instance.GetAllItems())
            {
                if (allowMinorMead && itemData.m_shared.m_isDrink)
                {
                    if (itemData.m_dropPrefab.name.Contains("Minor") || itemData.m_dropPrefab.name.Contains("Tasty"))
                        continue;
                }

                if (itemData.m_dropPrefab != null && CurrentAdvancedPortal.AllowedItems.Contains(itemData.m_dropPrefab.name))
                    continue;

                if ((itemData.m_crafterID != 0L || Portals.disallowedItems.Contains(itemData.m_dropPrefab.name) || itemData.m_shared.m_isDrink) &&
                    itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable &&
                    !isAllowedItem(itemData.m_dropPrefab.name))
                {
                    __result = false;
                    return false;
                }

                if (!itemData.m_shared.m_teleportable)
                {
                    __result = false;
                    return false;
                }
            }

            __result = true;
            return false;
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new Type[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
        [HarmonyPrefix]
        public static void ItemDrop_GetTooltip_Prefix(ItemDrop.ItemData item, ref bool __state)
        {
            __state = item.m_shared.m_teleportable;

            if ((item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable) ||
                (item.m_shared.m_useDurability && (item.m_durability < (item.GetMaxDurability() * Portals.minTeleportItemDur.Value))))
            {
                item.m_shared.m_teleportable = false;
            }
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new Type[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
        [HarmonyPostfix]
        public static void ItemDrop_GetTooltip_Postfix(ItemDrop.ItemData item, bool __state)
        {
            item.m_shared.m_teleportable = __state;
        }
    }
}
