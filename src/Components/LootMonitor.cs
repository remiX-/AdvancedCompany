﻿using QualityCompany.Manager.ShipTerminal;
using QualityCompany.Service;
using QualityCompany.Utils;

namespace QualityCompany.Components;

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
            Instance?._logger.LogError("ShipGameObject is null");
            return;
        }

        Instance?.UpdateMonitorText($"LOOT:\n${ScrapUtils.GetShipSettledTotalRawScrapValue()}\nCREDITS:\n${AdvancedTerminal.Terminal.groupCredits}");
    }
}