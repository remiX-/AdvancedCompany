﻿using QualityCompany.Manager.ShipTerminal;
using QualityCompany.Network;
using QualityCompany.Service;
using System;
using Unity.Netcode;
using UnityEngine;

namespace QualityCompany.TerminalCommands;

internal class DebugCommands
{
    private static readonly ACLogger _logger = new(nameof(DebugCommands));

    private static int scrapCountToHack;

    [TerminalCommand]
    private static TerminalCommandBuilder Run()
    {
        if (!Plugin.Instance.PluginConfig.TerminalDebugCommandsEnabled) return null;

        return new TerminalCommandBuilder("hack")
            .WithDescription(">hack <count>\nSpawn some ez lewt.")
            .WithText("Please enter a number of scrap items to spawn.\neg: hack 5")
            .WithCondition("isHost", "You are not host.", () => NetworkManager.Singleton.IsHost)
            .AddTextReplacement("[scrapCountToHack]", () => scrapCountToHack.ToString())
            .WithSubCommand(new TerminalSubCommandBuilder("<ha>")
                .WithMessage("Hacked in [scrapCountToHack] items")
                .WithConditions("isHost")
                .WithInputMatch(@"(\d+$)$")
                .WithPreAction(input =>
                {
                    _logger.LogDebug($"Hack: IsHost? {NetworkManager.Singleton.IsHost}");
                    // TODO: bug here, this shouldn't be in "PreAction"
                    if (!NetworkManager.Singleton.IsHost) return false;

                    scrapCountToHack = Convert.ToInt32(input);

                    if (scrapCountToHack <= 0) return false;
                    for (var i = 0; i < StartOfRound.Instance.allItemsList.itemsList.Count; i++)
                    {
                        var item = StartOfRound.Instance.allItemsList.itemsList[i];
                        _logger.LogDebug($" > {i}:{item.name}: {item.isScrap} | {item.minValue} -> {item.maxValue}");
                    }

                    var currentPlayerLocation = GameNetworkManager.Instance.localPlayerController.transform.position;
                    for (var i = 0; i < scrapCountToHack; i++)
                    {
                        _logger.LogDebug($"Hacking in item #{i}");

                        var rand = new System.Random();
                        var nextScrap = rand.Next(16, 68);
                        var itemToSpawn = StartOfRound.Instance.allItemsList.itemsList[nextScrap].spawnPrefab;

                        var scrap = UnityEngine.Object.Instantiate(itemToSpawn, currentPlayerLocation, Quaternion.identity);
                        var itemGrabObj = scrap.GetComponent<GrabbableObject>();

                        if (itemGrabObj is null)
                        {
                            _logger.LogDebug($"{itemToSpawn.name}: did not have a GrabbableObject component");
                            continue;
                        }

                        var scrapValue = rand.Next(itemGrabObj.itemProperties.minValue, itemGrabObj.itemProperties.maxValue);
                        _logger.LogDebug($" > spawned in {itemToSpawn.name} for {scrapValue}");
                        scrap.GetComponent<NetworkObject>().Spawn();

                        // RoundManager.Instance.scrapCollectedThisRound.Add(scrap.GetComponent<GrabbableObject>());
                        NetworkHandler.Instance.SyncValuesClientRpc(scrapValue, new NetworkBehaviourReference(itemGrabObj));

                        _logger.LogDebug(" > done");
                    }

                    return true;
                })
                .WithAction(() =>
                {
                    _logger.LogDebug("Hack: WithAction?");
                })
            );
    }

    private string GetTime()
    {
        return !StartOfRound.Instance.currentLevel.planetHasTime || !StartOfRound.Instance.shipDoorsEnabled ?
            "You're not on a moon. There is no time here.\n" :
            $"The time is currently {HUDManager.Instance.clockNumber.text.Replace('\n', ' ')}.\n";
    }
}