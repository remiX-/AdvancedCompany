﻿using QualityCompany.Manager;
using QualityCompany.Manager.Saves;
using QualityCompany.Manager.ShipTerminal;
using QualityCompany.Modules.Ship;
using QualityCompany.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace QualityCompany.TerminalCommands;

internal class SellCommands
{
    private static List<GrabbableObject> _recommendedScraps = new();
    private static int _sellScrapFor;
    private static int _sellScrapActualTarget;

    [TerminalCommand]
    private static TerminalCommandBuilder Run()
    {
        if (!Plugin.Instance.PluginConfig.TerminalSellCommandsEnabled) return null;

        return new TerminalCommandBuilder("sell")
            .WithDescription("> SELL [ALL|QUOTA|TARGET|2h|<AMOUNT>|<ITEM>]\nTo sell items on the ship. Will not sell ignored items from config.")
            .WithText("Please enter [ALL|QUOTA|TARGET|2h|<AMOUNT>|<ITEM>]")
            .WithSubCommand(CreateAllSubCommand())
            .WithSubCommand(CreateQuotaSubCommand())
            .WithSubCommand(CreateTargetSubCommand())
            .WithSubCommand(CreateAmountSubCommand())
            .WithSubCommand(Create2HandedSubCommand())
            .WithSubCommand(CreateItemSubCommand())
            .AddTextReplacement("[sellScrapFor]", () => _sellScrapFor.ToString())
            .AddTextReplacement("[sellScrapTarget]", () => _sellScrapActualTarget.ToString())
            .AddTextReplacement("[numScrapSold]", () => _recommendedScraps.Count.ToString())
            .AddTextReplacement("[shipTotalScrapCount]", () => ScrapUtils.GetShipSellableScrapCount().ToString())
            .AddTextReplacement("[shipTotalScrapValue]", () => ScrapUtils.GetShipTotalSellableScrapValue().ToString())
            .AddTextReplacement("[sellScrapActualTotal]", () => ScrapUtils.SumScrapListSellValue(_recommendedScraps).ToString())
            .AddTextReplacement("[companyBuyItemsCombo]", () => _recommendedScraps?.Select(x => $"{x.itemProperties.name}: {x.ActualSellValue()}").Aggregate((first, next) => $"{first}\n{next}"))
            .WithCondition("landedAtCompany", "ERROR: Usage of this feature is only permitted within Company bounds\n\nPlease land at 71-Gordion and repeat command.", GameUtils.IsLandedOnCompany)
            .WithCondition("hasScrapItems", "Bruh, you don't even have any items.", () => ScrapUtils.GetShipSellableScrapCount() > 0)
            .WithCondition("notEnoughScrap", "Not enough scrap to meet [sellScrapFor] credits.\nTotal value: [shipTotalScrapValue].", () => _sellScrapFor < ScrapUtils.GetShipTotalSellableScrapValue())
            .WithCondition("quotaAlreadyMet", "Quota already met.", () => TimeOfDay.Instance.profitQuota - TimeOfDay.Instance.quotaFulfilled > 0)
            .WithCondition("hasMatchingScrapItems", "No matching items found for input.", () => _recommendedScraps.Count > 0)
            .WithCondition("targetCommandDisabled", "Target command has been disabled", () => Plugin.Instance.PluginConfig.TerminalTargetCommandsEnabled);
    }

    private static TerminalSubCommandBuilder CreateAllSubCommand()
    {
        return new TerminalSubCommandBuilder("all")
            .WithDescription("Sell ALL items on the ship.")
            .WithMessage("[companyBuyingRateWarning]Requesting to sell ALL scrap ([shipTotalScrapCount]) for $[shipTotalScrapValue] credits.")
            .EnableConfirmDeny(confirmMessage: "Transaction complete. Sold [shipTotalScrapCount] scrap for $[shipTotalScrapValue] credits.\n\nThe company is not responsible for any calculation errors.")
            .WithConditions("landedAtCompany", "hasScrapItems")
            .WithPreAction(() => _sellScrapFor = ScrapUtils.GetShipTotalSellableScrapValue())
            .WithAction(() =>
            {
                TargetManager.SellAllScrap();
            });
    }

    private static TerminalSubCommandBuilder CreateQuotaSubCommand()
    {
        return new TerminalSubCommandBuilder("quota")
            .WithDescription("Sell only to meet the current quota.")
            .WithMessage("[companyBuyingRateWarning]Requesting to sell scrap as close to current quota ($[sellScrapFor] credits) as possible...\nThe Company wants the follow items for a total of [sellScrapActualTotal]:\n[companyBuyItemsCombo]")
            .EnableConfirmDeny(confirmMessage: "Transaction complete. Sold [shipTotalScrapCount] scrap for $[shipTotalScrapValue] credits.\n\nThe company is not responsible for any calculation errors.")
            .WithConditions("landedAtCompany", "hasScrapItems", "notEnoughScrap", "quotaAlreadyMet")
            .WithPreAction(() =>
            {
                _sellScrapFor = TimeOfDay.Instance.profitQuota - TimeOfDay.Instance.quotaFulfilled;
                _recommendedScraps = ScrapUtils.GetScrapForAmount(_sellScrapFor);
            })
            .WithAction(() =>
            {
                TargetManager.SellAllTargetedScrap(_recommendedScraps);
            });
    }

    private static TerminalSubCommandBuilder CreateTargetSubCommand()
    {
        return new TerminalSubCommandBuilder("target")
            .WithDescription("Sell only to meet the current set target. Will sell quota if target is below target.")
            .WithMessage("[companyBuyingRateWarning]Requesting to sell scrap as close to current target ($[sellScrapTarget], needing $[sellScrapFor]) as possible...\nThe Company wants the follow items for a total of [sellScrapActualTotal]:\n[companyBuyItemsCombo]")
            .EnableConfirmDeny(confirmMessage: "Transaction complete. Sold [shipTotalScrapCount] scrap for $[shipTotalScrapValue] credits.\n\nThe company is not responsible for any calculation errors.")
            .WithConditions("targetCommandDisabled", "landedAtCompany", "hasScrapItems", "notEnoughScrap", "targetAlreadyMet")
            .WithPreAction(() =>
            {
                _sellScrapActualTarget = SaveManager.SaveData.TargetForSelling;
                _sellScrapFor = InfoMonitor.Instance.CalculatedNeededToReachTarget;
                _recommendedScraps = ScrapUtils.GetScrapForAmount(_sellScrapFor)
                    .OrderBy(x => x.itemProperties.name)
                    .ThenByDescending(x => x.scrapValue)
                    .ToList();
            })
            .WithAction(() =>
            {
                TargetManager.SellAllTargetedScrap(_recommendedScraps);
            });
    }

    private static TerminalSubCommandBuilder CreateAmountSubCommand()
    {
        return new TerminalSubCommandBuilder("<amount>")
            .WithDescription("Sell as close as possible to input amount")
            .WithMessage("[companyBuyingRateWarning]Requesting to sell scrap as close to $[sellScrapFor] as possible...\n\nThe Company wants the follow items for a total of $[sellScrapActualTotal]:\n[companyBuyItemsCombo]")
            .EnableConfirmDeny(confirmMessage: "Sold [numScrapSold] scrap for $[sellScrapActualTotal].\n\nThe company is not responsible for any calculation errors.")
            .WithConditions("landedAtCompany", "hasScrapItems", "notEnoughScrap")
            .WithInputMatch(@"^(\d+)$")
            .WithPreAction(input =>
            {
                _sellScrapFor = Convert.ToInt32(input);

                if (_sellScrapFor <= 0) return false;

                _recommendedScraps = ScrapUtils.GetScrapForAmount(_sellScrapFor)
                    .OrderBy(x => x.itemProperties.name)
                    .ThenByDescending(x => x.scrapValue)
                    .ToList();

                // Nothing found, return notEnoughScrapNode
                if (_recommendedScraps.Count == 0) return false;

                // A combination has been found, return info with confirm/deny node
                return true;
            })
            .WithAction(() =>
            {
                TargetManager.SellAllTargetedScrap(_recommendedScraps);
            });
    }

    private static TerminalSubCommandBuilder Create2HandedSubCommand()
    {
        return new TerminalSubCommandBuilder("2h")
            .WithDescription("Sell all 2handed items")
            .WithMessage("[companyBuyingRateWarning]Requesting to sell all two-handed scrap.\n\nThe Company wants the follow items for a total of $[sellScrapActualTotal]:\n[companyBuyItemsCombo]")
            .EnableConfirmDeny(confirmMessage: "Sold [numScrapSold] scrap for $[sellScrapActualTotal].\n\nThe company is not responsible for any calculation errors.")
            .WithConditions("landedAtCompany", "hasScrapItems", "notEnoughScrap")
            .WithPreAction(() =>
            {
                _recommendedScraps = ScrapUtils.GetAllSellableScrapInShip()
                    .Where(x => x.itemProperties.twoHanded)
                    .OrderBy(x => x.itemProperties.name)
                    .ThenByDescending(x => x.scrapValue)
                    .ToList();
            })
            .WithAction(() =>
            {
                TargetManager.SellAllTargetedScrap(_recommendedScraps);
            });
    }

    private static readonly Regex RemoveWhitespaceRegex = new(@"\s+");
    private static TerminalSubCommandBuilder CreateItemSubCommand()
    {
        return new TerminalSubCommandBuilder("<item>")
            .WithDescription(@"Sell all items matching the input. You can also use conditional value checks after the item name.
Note: This will bypass the ignore list.
Examples:
> sell mask      - sells 'ComedyMask' and 'TragedyMask'
> sell shot>50   - sells shotguns with a value of more than 50
> sell lamp<=100 - lamps less than or equal to 100"
            )
            .WithMessage("[companyBuyingRateWarning]Requesting to sell specified items.\n\nThe Company wants the follow items for a total of $[sellScrapActualTotal]:\n[companyBuyItemsCombo]")
            .EnableConfirmDeny(confirmMessage: "Sold [numScrapSold] scrap for $[sellScrapActualTotal].\n\nThe company is not responsible for any calculation errors.")
            .WithConditions("landedAtCompany", "hasScrapItems", "hasMatchingScrapItems")
            .WithInputMatch("^(.+)$")
            .WithPreAction(input =>
            {
                input = RemoveWhitespaceRegex.Replace(input, "");

                Plugin.Instance.ACLogger.LogDebug($"> '{input}'");

                var reg = new Regex(@"^(\w+)(>|<|>=|<=|=)(\d+)$");
                var match = reg.Match(input);
                if (!match.Success)
                {
                    // Just try sell raw item
                    _recommendedScraps = ScrapUtils.GetAllScrapInShip()
                        .Where(x => x.itemProperties.name.ToLower().Contains(input))
                        .ToList();
                }
                else
                {
                    // conditional selling
                    foreach (Group group in match.Groups)
                    {
                        Plugin.Instance.ACLogger.LogDebug($"> '{group.Value}'");
                    }
                    var itemName = match.Groups[1].Value;
                    var greaterThan = match.Groups[2].Value == ">";
                    var smallerThan = match.Groups[2].Value == "<";
                    var greaterThanEqualTo = match.Groups[2].Value == ">=";
                    var smallerThanEqualTo = match.Groups[2].Value == ">=";

                    var amount = int.Parse(match.Groups[3].Value);
                    _recommendedScraps = ScrapUtils.GetAllScrapInShip()
                        .Where(x => x.itemProperties.name.ToLower().Contains(itemName))
                        .Where(x =>
                            greaterThan ? x.scrapValue > amount :
                            smallerThan ? x.scrapValue < amount :
                            greaterThanEqualTo ? x.scrapValue >= amount :
                            smallerThanEqualTo ? x.scrapValue <= amount :
                            x.scrapValue == amount
                        )
                        .OrderByDescending(x => x.scrapValue)
                        .ToList();
                }

                return true;
            })
            .WithAction(() =>
            {
                TargetManager.SellAllTargetedScrap(_recommendedScraps);
            });
    }
}

