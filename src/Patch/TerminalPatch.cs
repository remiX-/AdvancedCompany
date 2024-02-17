﻿using BepInEx;
using HarmonyLib;
using QualityCompany.Manager.ShipTerminal;
using QualityCompany.Modules.Ship;
using QualityCompany.Service;
using QualityCompany.Utils;
using System.Linq;
using System.Text.RegularExpressions;
using static QualityCompany.Service.GameEvents;

#pragma warning disable IDE0060
#pragma warning disable Harmony003

namespace QualityCompany.Patch;

[HarmonyPatch(typeof(Terminal))]
internal class TerminalPatch
{
    private static readonly ACLogger Logger = new(nameof(TerminalPatch));

    [HarmonyPostfix]
    [HarmonyPatch("Awake")]
    private static void AwakePatch(Terminal __instance)
    {
        GameUtils.Terminal = __instance;

        // TODO: this should be auto via attribute maybe?
        AdvancedTerminal.Init();

        OnTerminalAwakeEvent(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch("SyncGroupCreditsClientRpc")]
    private static void SyncGroupCreditsClientRpcPatch()
    {
        LootMonitor.UpdateMonitor();
    }

    [HarmonyPostfix]
    [HarmonyPatch("TextPostProcess")]
    public static string TextPostProcessPatch(string __result)
    {
        foreach (var (key, func) in AdvancedTerminal.GlobalTextReplacements)
        {
            if (!__result.Contains(key)) continue;

            __result = __result.Replace(key, func());
        }

        foreach (var command in AdvancedTerminal.Commands)
        {
            foreach (var (key, func) in command.TextProcessPlaceholders)
            {
                if (!__result.Contains(key)) continue;

                __result = __result.Replace(key, func());
            }
        }

        return __result;
    }

    [HarmonyPostfix]
    [HarmonyPatch("ParsePlayerSentence")]
    public static TerminalNode ParsePlayerSentencePatch(TerminalNode __result, Terminal __instance)
    {
        if (__result is null) return null;

        var terminalInput = __instance.screenText.text.Substring(__instance.screenText.text.Length - __instance.textAdded).ToLower();
        var inputArray = terminalInput.Split(" ").Where(x => !x.IsNullOrWhiteSpace()).ToList();
        var inputCommand = inputArray[0];
        var inputCommandArgs = inputArray.Count > 1 ? inputArray[1] : null;
        //_logger.LogDebug($"TryReturnSpecialNodes: {__result.name} | input: {terminalInput}");

        // Try to find the matching primary command first
        var filteredCommands = AdvancedTerminal.Commands.Where(x => x.CommandText == inputCommand).ToList();
        if (!filteredCommands.Any())
        {
            //_logger.LogDebug($" > No commands found matching '{inputCommand}' with args '{inputCommandArgs}'");
            return __result;
        }

        if (filteredCommands.Count > 1)
        {
            Logger.LogError($" > Found multiple commands! HOW? Only using first one. Found: {filteredCommands.Select(x => x.CommandText).Aggregate((first, second) => $"{first}, {second}")}");
        }

        var advancedCommand = filteredCommands.First();
        //_logger.LogDebug($" > command: {advancedCommand.CommandText} | event: /{__result.terminalEvent}/");

        if (advancedCommand.IsSimpleCommand)
        {
            var resNode = ExecuteSimpleCommand(advancedCommand);
            if (resNode != null) return resNode;
        }
        else
        {

            var resNode = ExecuteComplexCommand(advancedCommand, __result, inputCommand, inputCommandArgs);
            if (resNode != null) return resNode;
        }

        //_logger.LogDebug($"TryReturnSpecialNodes.end");
        return __result;
    }

    private static TerminalNode ExecuteSimpleCommand(TerminalCommandBuilder command)
    {
        //_logger.LogDebug(" > executing SimpleCommand");
        //_logger.LogDebug("  > checking conditions");
        foreach (var (node, condition) in command.SpecialNodes)
        {
            //_logger.LogDebug($"   > condition: {node.name}");

            if (!condition()) continue;

            //_logger.LogDebug($"    > FAILED");
            return node;
        }

        return null;
    }

    private static TerminalNode ExecuteComplexCommand(TerminalCommandBuilder command, TerminalNode __result, string inputCommand, string inputCommandArgs)
    {
        //_logger.LogDebug(" > executing ComplexCommand");

        // Try to find a matching non-variable (input) command
        var subCommand = command.SubCommands.FirstOrDefault(subCmd => !subCmd.IsVariableCommand && subCmd.Name == inputCommandArgs);
        if (subCommand is not null)
        {
            // Found matching 'simple' sub command
            subCommand.PreConditionAction();
        }
        else
        {
            // If not input args are provided, return primary command main node
            if (inputCommandArgs.IsNullOrWhiteSpace()) return command.Node;

            // Now try to find a matching variable input command
            subCommand = command.SubCommands.FirstOrDefault(subCmd =>
            {
                if (!subCmd.IsVariableCommand) return false;

                //_logger.LogDebug($"  > INPUT: '{inputCommand}' | '{inputCommandArgs}' | REGEX: {subCmd.VariableRegexMatchPattern}");
                var regex = new Regex(subCmd.VariableRegexMatchPattern);
                var match = regex.Match(inputCommandArgs);
                return match.Success;
            });
            subCommand?.VariablePreAction(inputCommandArgs);
        }

        // If nothing was found, just return the main commands node
        if (subCommand is null)
        {
            //_logger.LogDebug($"  > No matching sub command found for input: '{inputCommand}' | '{inputCommandArgs}'");
            return command.Node;
        }

        //_logger.LogDebug("  > checking special conditions");
        foreach (var conditionString in subCommand.Conditions)
        {
            var specialCondition = command.SpecialNodes.FirstOrDefault(x => x.node.name == conditionString);
            if (specialCondition == default)
            {
                Logger.LogError($"> SubCommand {subCommand.Name} has special criteria for '{conditionString}' but it is not found as part of the commands' special conditions list.");
                break;
            }

            //_logger.LogDebug($"   > {conditionString} = {specialCondition.condition()}");

            if (specialCondition.condition()) continue;

            //_logger.LogDebug($"   > FAILED: {specialCondition.node.name}");
            return specialCondition.node;
        }

        // Finally, if all is well & conditions are happy, return the subCommands node.
        return subCommand.Node;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Terminal.RunTerminalEvents))]
    public static void RunTerminalEventsPatch(TerminalNode node)
    {
        //_logger.LogDebug($"RunTerminalEvents: node: {node?.ToString() ?? "null"} | terminalEvent: {node?.terminalEvent ?? "empty"}");

        if (node.terminalEvent.IsNullOrWhiteSpace()) return;

        foreach (var command in AdvancedTerminal.Commands)
        {
            //_logger.LogDebug($"  > Checking {command.node.name}");

            if (command.IsSimpleCommand && command.ActionEvent == node.terminalEvent)
            {
                //_logger.LogDebug("  > IsSimpleCommand, SKIP??");
                //_logger.LogDebug($" > EXEC simple command: {command.node.name}");
                var res = command.Action();
                node.displayText = res + AdvancedTerminal.EndOfMessage;

                break;
            }

            var match = command.SubCommands.FirstOrDefault(x => x.ActionEvent == node.terminalEvent);
            if (match == default) continue;

            //_logger.LogDebug($"  > Found {match.Node.name}");
            match.Action();

            break;
        }
    }
}
