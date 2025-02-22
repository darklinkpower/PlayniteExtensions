﻿using AmazonGamesLibrary.Services;
using Playnite.Common;
using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace AmazonGamesLibrary
{
    [LoadPlugin]
    public class AmazonGamesLibrary : LibraryPluginBase<AmazonGamesLibrarySettingsViewModel>
    {
        internal readonly string TokensPath;

        public AmazonGamesLibrary(IPlayniteAPI api) : base(
            "Amazon Games",
            Guid.Parse("402674cd-4af6-4886-b6ec-0e695bfa0688"),
            new LibraryPluginProperties { CanShutdownClient = true, HasSettings = true },
            new AmazonGamesLibraryClient(),
            AmazonGames.Icon,
            (_) => new AmazonGamesLibrarySettingsView(),
            api)
        {
            SettingsViewModel = new AmazonGamesLibrarySettingsViewModel(this, PlayniteApi);
            TokensPath = Path.Combine(GetPluginUserDataPath(), "tokens.json");
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            SettingsViewModel.IsFirstRunUse = firstRunSettings;
            return SettingsViewModel;
        }

        public override LibraryMetadataProvider GetMetadataDownloader()
        {
            return new AmazonGamesMetadataProvider();
        }

        public override IEnumerable<InstallController> GetInstallActions(GetInstallActionsArgs args)
        {
            if (args.Game.PluginId != Id)
            {
                yield break;
            }

            yield return new AmazonInstallController(args.Game);
        }

        public override IEnumerable<UninstallController> GetUninstallActions(GetUninstallActionsArgs args)
        {
            if (args.Game.PluginId != Id)
            {
                yield break;
            }

            yield return new AmazonUninstallController(args.Game);
        }

        public override IEnumerable<PlayController> GetPlayActions(GetPlayActionsArgs args)
        {
            if (args.Game.PluginId != Id)
            {
                yield break;
            }

            var gameConfig = AmazonGames.GetGameConfiguration(args.Game.InstallDirectory);
            if (AmazonGames.GetGameRequiresClient(gameConfig) || !SettingsViewModel.Settings.StartGamesWithoutLauncher)
            {
                yield return new AutomaticPlayController(args.Game)
                {
                    Type = AutomaticPlayActionType.Url,
                    TrackingMode = TrackingMode.Directory,
                    Name = "Start using Amazon client",
                    TrackingPath = args.Game.InstallDirectory,
                    Path = $"amazon-games://play/{args.Game.GameId}"
                };
            }
            else
            {
                var controller = new AutomaticPlayController(args.Game)
                {
                    Type = AutomaticPlayActionType.File,
                    TrackingMode = TrackingMode.Directory,
                    Name = $"Start {args.Game.Name}",
                    TrackingPath = args.Game.InstallDirectory,
                    Path = Path.Combine(args.Game.InstallDirectory, gameConfig.Main.Command)
                };

                if (gameConfig.Main.Args.HasNonEmptyItems())
                {
                    controller.Arguments = string.Join(" ", gameConfig.Main.Args);
                }

                if (!gameConfig.Main.WorkingSubdirOverride.IsNullOrEmpty())
                {
                    controller.WorkingDir = Path.Combine(args.Game.InstallDirectory, gameConfig.Main.WorkingSubdirOverride);
                }

                yield return controller;
            }
        }

        internal Dictionary<string, GameInfo> GetInstalledGames()
        {
            var games = new Dictionary<string, GameInfo>();
            var programs = Programs.GetUnistallProgramsList();
            foreach (var program in programs)
            {
                if (program.UninstallString?.Contains("Amazon Game Remover.exe") != true)
                {
                    continue;
                }

                if (!Directory.Exists(program.InstallLocation))
                {
                    continue;
                }

                var match = Regex.Match(program.UninstallString, @"-p\s+([a-zA-Z0-9\-]+)");
                var gameId = match.Groups[1].Value;
                if (!games.ContainsKey(gameId))
                {
                    var game = new GameInfo()
                    {
                        InstallDirectory = Paths.FixSeparators(program.InstallLocation),
                        GameId = gameId,
                        Source = "Amazon",
                        Name = program.DisplayName.RemoveTrademarks(),
                        IsInstalled = true
                    };

                    games.Add(game.GameId, game);
                }
            }

            return games;
        }

        public List<GameInfo> GetLibraryGames()
        {
            var games = new List<GameInfo>();
            var client = new AmazonAccountClient(this);
            var entitlements = client.GetAccountEntitlements().GetAwaiter().GetResult();
            foreach (var item in entitlements)
            {
                if (item.product.productLine == "Twitch:FuelEntitlement")
                {
                    continue;
                }

                var game = new GameInfo()
                {
                    Source = "Amazon",
                    GameId = item.product.id,
                    Name = item.product.title.RemoveTrademarks()
                };

                games.Add(game);
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
                    Logger.Debug($"Found {installedGames.Count} installed Amazon games.");
                    allGames.AddRange(installedGames.Values.ToList());
                }
                catch (Exception e) when (!PlayniteApi.ApplicationInfo.ThrowAllErrors)
                {
                    Logger.Error(e, "Failed to import installed Amazon games.");
                    importError = e;
                }
            }

            if (SettingsViewModel.Settings.ConnectAccount)
            {
                try
                {
                    var libraryGames = GetLibraryGames();
                    Logger.Debug($"Found {libraryGames.Count} library Amazon games.");

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
                        }
                        else
                        {
                            allGames.Add(game);
                        }
                    }
                }
                catch (Exception e) when (!PlayniteApi.ApplicationInfo.ThrowAllErrors)
                {
                    Logger.Error(e, "Failed to import linked account Amazon games details.");
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
    }
}