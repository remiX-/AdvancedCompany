﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace QualityCompany.Manager.ShipTerminal;

public class TerminalCommandBuilder
{
    internal string CommandText { get; set; }
    internal Func<string> Action;
    internal bool IsSimpleCommand;
    internal string ActionEvent;
    internal readonly TerminalNode Node = new();
    internal readonly TerminalKeyword NodeKeyword = new();
    internal readonly List<TerminalSubCommandBuilder> SubCommandsBuilders = new();
    internal List<TerminalSubCommand> SubCommands = new();
    internal readonly Dictionary<string, Func<string>> TextProcessPlaceholders = new();
    internal readonly List<(TerminalNode node, Func<bool> condition)> SpecialNodes = new();

    internal string Description = "";

    internal string ConfirmMessage = "Confirmed!";
    internal string DenyMessage = "Cancelled!";

    public TerminalCommandBuilder(string name)
    {
        CommandText = name;
        Node.name = name;
        Node.clearPreviousText = true;
        Node.displayText = $"{name} is empty";
        NodeKeyword = new TerminalKeyword
        {
            name = name,
            word = name,
            specialKeywordResult = Node
        };
    }

    public TerminalCommandBuilder WithDescription(string desc)
    {
        Description = desc + AdvancedTerminal.EndOfMessage;

        return this;
    }

    public TerminalCommandBuilder WithAction(Action action)
    {
        IsSimpleCommand = true;
        Action = () =>
        {
            action();
            return Node.displayText;
        };
        Node.terminalEvent = $"{Node.name}_event";

        return this;
    }

    public TerminalCommandBuilder WithAction(Func<string> action)
    {
        IsSimpleCommand = true;
        Action = action;
        // nodeKeyword.specialKeywordResult = node;

        return this;
    }

    public TerminalCommandBuilder WithSubCommand(TerminalSubCommandBuilder builder)
    {
        SubCommandsBuilders.Add(builder);

        return this;
    }

    public TerminalCommandBuilder WithText(string text)
    {
        Node.displayText = text + AdvancedTerminal.EndOfMessage;
        return this;
    }

    public TerminalCommandBuilder EnableConfirmDeny(string confirmMessage, string denyMessage)
    {
        ConfirmMessage = confirmMessage + AdvancedTerminal.EndOfMessage;
        DenyMessage = denyMessage + AdvancedTerminal.EndOfMessage;

        Node.isConfirmationNode = true;
        Node.overrideOptions = true;

        return this;
    }

    public TerminalCommandBuilder AddTextReplacement(string replacementKey, Func<string> action)
    {
        TextProcessPlaceholders.Add(replacementKey, action);

        return this;
    }

    /// <summary>
    /// A condition is an action that returns a bool specifying whether this command may proceed.
    /// Occurs before <see cref="Action"/> and after <see cref="TerminalSubCommand.PreConditionAction"/>.
    /// <example>Example: A simple example would be adding a condition to be able to run a command after 1PM in-game time.</example>
    /// </summary>
    /// <param name="name">The unique name for this overall command.</param>
    /// <param name="displayText">Text to be displayed if this condition resolves to <value>false</value></param>
    /// <param name="condition">The condition action to run that must resolve to <value>true</value> to pass this condition. If not, it will stop at this condition and display its displayText.</param>
    /// <returns></returns>
    public TerminalCommandBuilder WithCondition(string name, string displayText, Func<bool> condition)
    {
        var specialNode = new TerminalNode
        {
            name = name,
            displayText = displayText + AdvancedTerminal.EndOfMessage,
            clearPreviousText = true
        };
        SpecialNodes.Add((specialNode, condition));

        return this;
    }

    internal TerminalKeyword[] Build(TerminalKeyword confirmKeyword, TerminalKeyword denyKeyword)
    {
        ActionEvent = $"{Node.name}_event";

        if (SubCommandsBuilders.Any())
        {
            SubCommands = SubCommandsBuilders
                .Select(x => x.Build(Node.name, confirmKeyword, denyKeyword))
                .ToList();
            NodeKeyword.isVerb = SubCommands.Any();
            NodeKeyword.compatibleNouns = SubCommands.Select(keyword => new CompatibleNoun
            {
                noun = keyword.Keyword,
                result = keyword.Node
            }).ToArray();
        }

        if (Node.isConfirmationNode)
        {
            Node.displayText += "[confirmOrDeny]";
            Node.terminalOptions = new[]
            {
                new CompatibleNoun
                {
                    noun = confirmKeyword,
                    result = new TerminalNode
                    {
                        name = $"{Node.name}_confirm",
                        displayText = ConfirmMessage + AdvancedTerminal.EndOfMessage,
                        clearPreviousText = true,
                        terminalEvent = $"{Node.name}_event"
                    }
                },
                new CompatibleNoun
                {
                    noun = denyKeyword,
                    result = new TerminalNode
                    {
                        name = $"{Node.name}_deny",
                        displayText = DenyMessage + AdvancedTerminal.EndOfMessage,
                        clearPreviousText = true
                    }
                }
            };
        }
        else
        {
            Node.terminalEvent = $"{Node.name}_event";
        }

        return new List<TerminalKeyword> { NodeKeyword }
            .Concat(SubCommands.Where(x => !x.IsVariableCommand).Select(x => x.Keyword))
            .ToArray();
    }
}