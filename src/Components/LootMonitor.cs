﻿using System.Linq;
using AdvancedCompany.Service;
using AdvancedCompany.Utils;

namespace AdvancedCompany.Components;

internal class LootMonitor : BaseMonitor
{
    public static LootMonitor Instance;

    protected override void PostStart()
    {
        Instance = this;
        _logger = new ACLogger(nameof(LootMonitor));

        UpdateMonitor();
    }

    public static void UpdateMonitor()
    {
        if (GameUtils.ShipGameObject == null)
        {
            Instance._logger.LogError("ShipGameObject is null");
            return;
        }

        var num = ScrapUtils.GetShipTotalRawScrapValue();
        Instance?.UpdateMonitorText("LOOT", num);
    }

    // private static int CalculateShipScrapLoot()
    // {
    //     return ScrapUtils.GetAllScrapInShip()
    //         .Where(item => item.itemProperties.isScrap && !item.isHeld && !item.isPocketed)
    //         .Sum(item => item.scrapValue);
    // }
}