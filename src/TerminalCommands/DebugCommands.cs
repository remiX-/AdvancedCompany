﻿using Newtonsoft.Json;
using QualityCompany.Manager.ShipTerminal;
using QualityCompany.Network;
using QualityCompany.Service;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static QualityCompany.Service.ServiceRegistry;

namespace QualityCompany.TerminalCommands;

internal class DebugCommands
{
    private static readonly ModLogger Logger = new(nameof(DebugCommands));

    private static int scrapCountToHack;

    [TerminalCommand]
    private static TerminalCommandBuilder Run()
    {
        if (!Plugin.Instance.PluginConfig.TerminalDebugCommandsEnabled) return null;

        return new TerminalCommandBuilder("hack")
            .WithHelpDescription("Spawn some ez lewt.")
            .WithCommandDescription("Please enter a number of scrap items to spawn.\neg: hack 5")
            .WithCondition("isHost", "You are not host.", () => NetworkManager.Singleton.IsHost)
            .AddTextReplacement("[scrapCountToHack]", () => scrapCountToHack.ToString())
            .WithSubCommand(new TerminalSubCommandBuilder("<count>")
                .WithDescription("Hack in <count> number of items.")
                .WithMessage("Hacked in [scrapCountToHack] items")
                .WithConditions("isHost")
                .WithInputMatch(@"(\d+$)$")
                .WithPreAction(input =>
                {
                    Logger.TryLogDebug($"Hack: IsHost? {NetworkManager.Singleton.IsHost}");
                    // TODO: bug here, this shouldn't be in "PreAction"
                    if (!NetworkManager.Singleton.IsHost) return false;

                    scrapCountToHack = Convert.ToInt32(input);

                    if (scrapCountToHack <= 0) return false;
                    scrapCountToHack = Math.Min(100, scrapCountToHack);

#if DEBUG
                    var dict = StartOfRound.Instance.allItemsList.itemsList.ToDictionary<Item, string, dynamic>(item => item.name, item => new
                    {
                        item.name,
                        item.minValue,
                        item.maxValue,
                        item.batteryUsage,
                        item.isScrap,
                        item.twoHanded,
                        item.weight,
                        item.creditsWorth,
                        item.itemSpawnsOnGround,
                        item.itemId
                    });

                    Logger.TryLogDebug($"Saved item data to {Application.persistentDataPath}");
                    File.WriteAllText(Path.Combine(Application.persistentDataPath, "game_items.json"), JsonConvert.SerializeObject(dict));
#endif
                    var itemsList = StartOfRound.Instance.allItemsList.itemsList;
                    var currentPlayerLocation = GameNetworkManager.Instance.localPlayerController.transform.position;
                    for (var i = 0; i < scrapCountToHack; i++)
                    {
                        Logger.TryLogDebug($"Hacking in item #{i}");

                        var item = itemsList[Randomizer.GetInt(0, itemsList.Count)];
                        while (!item.isScrap)
                        {
                            item = itemsList[Randomizer.GetInt(0, itemsList.Count)];
                        }

                        var itemToSpawn = item.spawnPrefab;
                        Logger.TryLogDebug($" > spawning in {itemToSpawn.name}");

                        var scrap = UnityEngine.Object.Instantiate(itemToSpawn, currentPlayerLocation, Quaternion.identity);
                        var itemGrabObj = scrap.GetComponent<GrabbableObject>();

                        if (itemGrabObj is null)
                        {
                            Logger.TryLogDebug($"{itemToSpawn.name}: did not have a GrabbableObject component");
                            continue;
                        }

                        var min = itemGrabObj.itemProperties.minValue;
                        var max = itemGrabObj.itemProperties.maxValue;
                        if (min > max)
                        {
                            min = itemGrabObj.itemProperties.maxValue;
                            max = itemGrabObj.itemProperties.minValue;
                        }
                        var scrapValue = Randomizer.GetInt(min, max) / 2;
                        Logger.TryLogDebug($"  > value: {scrapValue}");
                        scrap.GetComponent<NetworkObject>().Spawn();

                        NetworkHandler.Instance.SyncValuesClientRpc(scrapValue, new NetworkBehaviourReference(itemGrabObj));

                        Logger.TryLogDebug(" > done");
                    }

                    return true;
                })
                .WithAction(() =>
                {
                    Logger.TryLogDebug("Hack: WithAction?");
                })
            );
    }
}