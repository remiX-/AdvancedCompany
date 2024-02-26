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
    private static readonly ModLogger Logger = new(nameof(TerminalPatch));

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
        Logger.LogDebug("TextPostProcessPatch.start");

        foreach (var (key, func) in AdvancedTerminal.GlobalTextReplacements)
        {
            if (!__result.Contains(key)) continue;

            Logger.LogDebug($" > found global: {key}");
            __result = __result.Replace(key, func());
        }

        foreach (var command in AdvancedTerminal.Commands)
        {
            foreach (var (key, func) in command.TextProcessPlaceholders)
            {
                if (!__result.Contains(key)) continue;

                Logger.LogDebug($" > found command: {command.CommandText}.{key}");
                __result = __result.Replace(key, func());
            }
        }

        Logger.LogDebug("TextPostProcessPatch.end");

        return __result;
    }

    [HarmonyPostfix]
    [HarmonyPatch("ParsePlayerSentence")]
    public static TerminalNode ParsePlayerSentencePatch(TerminalNode __result, Terminal __instance)
    {
        if (__result is null) return null;

        var terminalInput = __instance.screenText.text[^__instance.textAdded..].ToLower().Trim();
        var inputCommand = terminalInput.Contains(" ") ? terminalInput[..terminalInput.LastIndexOf(' ')] : terminalInput;
        var inputCommandArgs = terminalInput.Contains(" ") ? terminalInput[(terminalInput.LastIndexOf(' ') + 1)..] : null;
        Logger.LogDebug($"TryReturnSpecialNodes: {__result.name} | input: {terminalInput} | {inputCommand} | {inputCommandArgs}");

        // Try to find the matching primary command first
        var filteredCommands = AdvancedTerminal.Commands
            // .Where(x => inputCommand.Length < 3 ? x.CommandText == inputCommand : x.CommandText.StartsWith(inputCommand))
            // .Where(
            //     x => x.CommandText.Contains(' ')
            //     ? true
            //     : inputCommand.Length < 3 ? x.CommandText == inputCommand : x.CommandText.StartsWith(inputCommand)
            // )
            .Where(x =>
            {
                if (x.CommandText.Contains(' '))
                {
                    return inputCommand.Length < 4 ? x.CommandText == inputCommand : x.CommandText.StartsWith(inputCommand);
                }

                return inputCommand.Length < 3 ? x.CommandText == inputCommand : x.CommandText.StartsWith(inputCommand);
            })
            .ToList();

        if (!filteredCommands.Any())
        {
            Logger.LogDebug($" > No commands found matching input '{terminalInput}' | '{inputCommand}' with args '{inputCommandArgs}'");
            return __result;
        }

        if (filteredCommands.Count > 1)
        {
            Logger.LogError($" > Found multiple commands! HOW? Only using first one. Found: {filteredCommands.Select(x => x.CommandText).Aggregate((first, second) => $"{first}, {second}")}");
        }

        var advancedCommand = filteredCommands.First();
        Logger.LogDebug($" > command: {advancedCommand.CommandText} | event: /{__result.terminalEvent ?? "empty"}/");

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

        Logger.LogDebug($"TryReturnSpecialNodes.end");
        return __result;
    }

    private static TerminalNode ExecuteSimpleCommand(TerminalCommandBuilder command)
    {
        Logger.LogDebug(" > executing SimpleCommand");
        Logger.LogDebug("  > checking conditions");
        foreach (var (node, condition) in command.SpecialNodes)
        {
            Logger.LogDebug($"   > condition: {node.name}");

            if (!condition()) continue;

            Logger.LogDebug($"    > FAILED");
            return node;
        }

        return command.Node;
    }

    private static TerminalNode ExecuteComplexCommand(TerminalCommandBuilder command, TerminalNode __result, string inputCommand, string inputCommandArgs)
    {
        Logger.LogDebug(" > executing ComplexCommand");

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

                Logger.LogDebug($"  > INPUT: '{inputCommand}' | '{inputCommandArgs}' | REGEX: {subCmd.VariableRegexMatchPattern}");
                var regex = new Regex(subCmd.VariableRegexMatchPattern);
                var match = regex.Match(inputCommandArgs);
                return match.Success;
            });
            subCommand?.VariablePreAction(inputCommandArgs);
        }

        // If nothing was found, just return the main commands node
        if (subCommand is null)
        {
            Logger.LogDebug($"  > No matching sub command found for input: '{inputCommand}' | '{inputCommandArgs}'");
            return command.Node;
        }

        Logger.LogDebug("  > checking special conditions");
        foreach (var conditionString in subCommand.Conditions)
        {
            var specialCondition = command.SpecialNodes.FirstOrDefault(x => x.node.name == conditionString);
            if (specialCondition == default)
            {
                //Logger.LogError($"> SubCommand {subCommand.Name} has special criteria for '{conditionString}' but it is not found as part of the commands' special conditions list.");
                break;
            }

            Logger.LogDebug($"   > {conditionString} = {specialCondition.condition()}");

            if (specialCondition.condition()) continue;

            Logger.LogDebug($"   > FAILED: {specialCondition.node.name}");
            return specialCondition.node;
        }

        // Finally, if all is well & conditions are happy, return the subCommands node.
        return subCommand.Node;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Terminal.RunTerminalEvents))]
    public static void RunTerminalEventsPatch(TerminalNode node)
    {
        Logger.LogDebug($"RunTerminalEvents: node: {node?.ToString() ?? "null"} | terminalEvent: {node?.terminalEvent ?? "empty"}");

        if (node.terminalEvent.IsNullOrWhiteSpace()) return;

        foreach (var command in AdvancedTerminal.Commands)
        {
            Logger.LogDebug($"  > Checking {command.Node.name}");

            if (command.IsSimpleCommand && command.ActionEvent == node.terminalEvent)
            {
                Logger.LogDebug("  > IsSimpleCommand, SKIP??");
                Logger.LogDebug($" > EXEC simple command: {command.Node.name}");
                var res = command.Action();
                node.displayText = res + AdvancedTerminal.EndOfMessage;

                break;
            }

            var match = command.SubCommands.FirstOrDefault(x => x.ActionEvent == node.terminalEvent);
            if (match == default) continue;

            Logger.LogDebug($"  > Found {match.Node.name}");
            match.Action();

            break;
        }
    }
}
