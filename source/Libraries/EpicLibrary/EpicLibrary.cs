﻿using EpicLibrary.Models;
using EpicLibrary.Services;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace EpicLibrary
{
    [LoadPlugin]
    public class EpicLibrary : LibraryPluginBase<EpicLibrarySettingsViewModel>
    {
        internal readonly string TokensPath;

        public EpicLibrary(IPlayniteAPI api) : base(
            "Epic",
            Guid.Parse("00000002-DBD1-46C6-B5D0-B1BA559D10E4"),
            new LibraryPluginProperties { CanShutdownClient = true, HasSettings = true },
            new EpicClient(),
            EpicLauncher.Icon,
            (_) => new EpicLibrarySettingsView(),
            api)
        {
            SettingsViewModel = new EpicLibrarySettingsViewModel(this, api);
            TokensPath = Path.Combine(GetPluginUserDataPath(), "tokens.json");
        }

        internal Dictionary<string, GameInfo> GetInstalledGames()
        {
            var games = new Dictionary<string, GameInfo>();
            var appList = EpicLauncher.GetInstalledAppList();
            var manifests = EpicLauncher.GetInstalledManifests();

            foreach (var app in appList)
            {
                if (app.AppName.StartsWith("UE_"))
                {
                    continue;
                }

                var manifest = manifests.FirstOrDefault(a => a.AppName == app.AppName);

                // DLC
                if (manifest.AppName != manifest.MainGameAppName)
                {
                    continue;
                }

                // UE plugins
                if (manifest.AppCategories?.Any(a => a == "plugins" || a == "plugins/engine") == true)
                {
                    continue;
                }

                var game = new GameInfo()
                {
                    Source = "Epic",
                    GameId = app.AppName,
                    Name = manifest?.DisplayName ?? Path.GetFileName(app.InstallLocation),
                    InstallDirectory = manifest?.InstallLocation ?? app.InstallLocation,
                    IsInstalled = true
                };

                game.Name = game.Name.RemoveTrademarks();
                games.Add(game.GameId, game);
            }

            return games;
        }

        internal List<GameInfo> GetLibraryGames()
        {
            var cacheDir = GetCachePath("catalogcache");
            var games = new List<GameInfo>();
            var accountApi = new EpicAccountClient(PlayniteApi, TokensPath);
            var assets = accountApi.GetAssets();
            if (!assets?.Any() == true)
            {
                Logger.Warn("Found no assets on Epic accounts.");
            }

            var playtimeItems = accountApi.GetPlaytimeItems();
            foreach (var gameAsset in assets.Where(a => a.@namespace != "ue"))
            {
                var cacheFile = Paths.GetSafePathName($"{gameAsset.@namespace}_{gameAsset.catalogItemId}_{gameAsset.buildVersion}.json");
                cacheFile = Path.Combine(cacheDir, cacheFile);
                var catalogItem = accountApi.GetCatalogItem(gameAsset.@namespace, gameAsset.catalogItemId, cacheFile);
                if (catalogItem?.categories?.Any(a => a.path == "applications") != true)
                {
                    continue;
                }

                if (catalogItem?.categories?.Any(a => a.path == "dlc") == true)
                {
                    continue;
                }

                var newGame = new GameInfo
                {
                    Source = "Epic",
                    GameId = gameAsset.appName,
                    Name = catalogItem.title.RemoveTrademarks()
                };

                var playtimeItem = playtimeItems?.FirstOrDefault(x => x.artifactId == gameAsset.appName);
                if (playtimeItem != null)
                {
                    newGame.Playtime = playtimeItem.totalTime;
                }

                games.Add(newGame);
            }

            return games;
        }

        public override IEnumerable<GameInfo> GetGames(LibraryGetGamesArgs args)
        {
            var allGames = new List<GameInfo>();
            var installedGames = new Dictionary<string, GameInfo>();
            Exception importError = null;

            if (SettingsViewModel.Settings.ImportInstalledGames)
            {
                try
                {
                    installedGames = GetInstalledGames();
                    Logger.Debug($"Found {installedGames.Count} installed Epic games.");
                    allGames.AddRange(installedGames.Values.ToList());
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Failed to import installed Epic games.");
                    importError = e;
                }
            }

            if (SettingsViewModel.Settings.ConnectAccount)
            {
                try
                {
                    var libraryGames = GetLibraryGames();
                    Logger.Debug($"Found {libraryGames.Count} library Epic games.");

                    if (!SettingsViewModel.Settings.ImportUninstalledGames)
                    {
                        libraryGames = libraryGames.Where(lg => installedGames.ContainsKey(lg.GameId)).ToList();
                    }

                    foreach (var game in libraryGames)
                    {
                        if (installedGames.TryGetValue(game.GameId, out var installed))
                        {
                            installed.Playtime = game.Playtime;
                            installed.LastActivity = game.LastActivity;
                            installed.Name = game.Name;
                        }
                        else
                        {
                            allGames.Add(game);
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Failed to import linked account Epic games details.");
                    importError = e;
                }
            }

            if (importError != null)
            {
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    ImportErrorMessageId,
                    string.Format(PlayniteApi.Resources.GetString("LOCLibraryImportError"), Name) +
                    System.Environment.NewLine + importError.Message,
                    NotificationType.Error,
                    () => OpenSettingsView()));
            }
            else
            {
                PlayniteApi.Notifications.Remove(ImportErrorMessageId);
            }

            return allGames;
        }

        public string GetCachePath(string dirName)
        {
            return Path.Combine(GetPluginUserDataPath(), dirName);
        }

        public override IEnumerable<InstallController> GetInstallActions(GetInstallActionsArgs args)
        {
            if (args.Game.PluginId != Id)
            {
                yield break;
            }

            yield return new EpicInstallController(args.Game);
        }

        public override IEnumerable<UninstallController> GetUninstallActions(GetUninstallActionsArgs args)
        {
            if (args.Game.PluginId != Id)
            {
                yield break;
            }

            yield return new EpicUninstallController(args.Game);
        }

        public override IEnumerable<PlayController> GetPlayActions(GetPlayActionsArgs args)
        {
            if (args.Game.PluginId != Id)
            {
                yield break;
            }

            yield return new AutomaticPlayController(args.Game)
            {
                Type = AutomaticPlayActionType.Url,
                TrackingMode = TrackingMode.Directory,
                TrackingPath = args.Game.InstallDirectory,
                Path = string.Format(EpicLauncher.GameLaunchUrlMask, args.Game.GameId),
                Name = "Start using EGS client"
            };
        }

        public override LibraryMetadataProvider GetMetadataDownloader()
        {
            return new EpicMetadataProvider(PlayniteApi);
        }
    }
}
