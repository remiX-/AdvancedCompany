﻿using Newtonsoft.Json;
using QualityCompany.Service;
using System;
using System.IO;
using UnityEngine;

namespace QualityCompany.Manager.Saves;

internal class SaveManager
{
    private static readonly ACLogger Logger = new(nameof(SaveManager));

    internal static GameSaveData SaveData { get; private set; } = new();

    private static bool IsHost => GameNetworkManager.Instance.isHostingGame;
    private static bool HasNetworking => Plugin.Instance.PluginConfig.NetworkingEnabled;

    private static string _saveFileName;
    private static string _saveFilePath;

    internal static void Load()
    {
        // client + networking = rely on host save
        if (!IsHost && HasNetworking) return;

        if (IsHost)
        {
            var saveNum = GameNetworkManager.Instance.saveFileNum;
            Logger.LogDebug($"HOST: using save data file in slot number {saveNum}");
            _saveFileName = $"{PluginMetadata.PLUGIN_NAME}_{saveNum}.json";
        }
        else if (!HasNetworking)
        {
            Logger.LogDebug("CLIENT: networking is disabling, using .local save data file");
            _saveFileName = $"{PluginMetadata.PLUGIN_NAME}.local.json";
        }
        else
        {
            // super safe return, but should be handled by earlier early return
            return;
        }

        _saveFilePath = Path.Combine(Application.persistentDataPath, _saveFileName);

        if (File.Exists(_saveFilePath))
        {
            Logger.LogDebug($"Loading save file: {_saveFileName}");
            var json = File.ReadAllText(_saveFilePath);
            LoadSaveJson(json);
        }
        else
        {
            Logger.LogDebug($"No save file found: {_saveFileName}, creating new");
            SaveData = new GameSaveData();
            Save();
        }
    }

    internal static void Save()
    {
        // client + networking = host does the saving
        if (!IsHost && HasNetworking) return;

        Logger.LogDebug($"Saving save data to {_saveFileName}");
        var json = JsonConvert.SerializeObject(SaveData);
        File.WriteAllText(_saveFilePath, json);
    }

    internal static void ClientLoadFromString(string saveJson)
    {
        Logger.LogDebug("CLIENT: Save file received from host, updating.");
        LoadSaveJson(saveJson);
    }

    private static void LoadSaveJson(string saveJson)
    {
        try
        {
            var jsonSaveData = JsonConvert.DeserializeObject<SaveData>(saveJson);

            SaveData = new GameSaveData
            {
                TotalShipLootAtStartForCurrentQuota = jsonSaveData.TotalShipLootAtStartForCurrentQuota,
                TotalDaysPlayedForCurrentQuota = jsonSaveData.TotalDaysPlayedForCurrentQuota,
                TargetForSelling = jsonSaveData.TargetForSelling,
            };
        }
        catch (Exception ex)
        {
            // save file has been edited / corrupted
            Logger.LogError($"Save file has been corrupted or edited, resetting. Error: {ex.Message}");
            SaveData = new GameSaveData();
            Save();
        }
    }
}

[Serializable]
file class SaveData
{
    public int TotalShipLootAtStartForCurrentQuota { get; set; }
    public int TotalDaysPlayedForCurrentQuota { get; set; }
    public int TargetForSelling { get; set; }
}