﻿using OriginLibrary.Models;
using OriginLibrary.Services;
using Playnite.SDK;
using Playnite.SDK.Metadata;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OriginLibrary
{
    public class OriginMetadataProvider : LibraryMetadataProvider
    {
        private readonly IPlayniteAPI api;

        public OriginMetadataProvider(IPlayniteAPI api)
        {
            this.api = api;
        }

        public override GameMetadata GetMetadata(Game game)
        {
            var resources = api.Resources;
            var storeMetadata = DownloadGameMetadata(game.GameId);
            var gameInfo = new GameInfo
            {
                Name = StringExtensions.NormalizeGameName(storeMetadata.StoreDetails.i18n.displayName),
                Description = storeMetadata.StoreDetails.i18n.longDescription,
                Links = new List<Link>()
                {
                    new Link(resources.GetString("LOCCommonLinksStorePage"), @"https://www.origin.com/store" + storeMetadata.StoreDetails.offerPath),
                    new Link("PCGamingWiki", @"http://pcgamingwiki.com/w/index.php?search=" + game.Name)
                }
            };

            var releaseDate = storeMetadata.StoreDetails.platforms.FirstOrDefault(a => a.platform == "PCWIN")?.releaseDate;
            if (releaseDate != null)
            {
                gameInfo.ReleaseDate = new ReleaseDate(releaseDate.Value);
            }

            if (!storeMetadata.StoreDetails.publisherFacetKey.IsNullOrEmpty())
            {
                gameInfo.Publishers = new List<string>() { storeMetadata.StoreDetails.publisherFacetKey };
            }

            if (!storeMetadata.StoreDetails.developerFacetKey.IsNullOrEmpty())
            {
                gameInfo.Developers = new List<string>() { storeMetadata.StoreDetails.developerFacetKey };
            }

            if (!storeMetadata.StoreDetails.genreFacetKey.IsNullOrEmpty())
            {
                gameInfo.Genres = new List<string>(storeMetadata.StoreDetails.genreFacetKey?.Split(','));
            }

            var metadata = new GameMetadata()
            {
                GameInfo = gameInfo
            };

            gameInfo.CoverImage = storeMetadata.CoverImage;
            gameInfo.BackgroundImage = storeMetadata.BackgroundImage;
            if (!string.IsNullOrEmpty(storeMetadata.StoreDetails.i18n.gameForumURL))
            {
                gameInfo.Links.Add(new Link(resources.GetString("LOCCommonLinksForum"), storeMetadata.StoreDetails.i18n.gameForumURL));
            }

            if (!string.IsNullOrEmpty(storeMetadata.StoreDetails.i18n.gameManualURL))
            {
                game.Manual = storeMetadata.StoreDetails.i18n.gameManualURL;
            }

            return metadata;
        }

        public OriginGameMetadata DownloadGameMetadata(string id)
        {
            var data = new OriginGameMetadata()
            {
                StoreDetails = OriginApiClient.GetGameStoreData(id)
            };

            data.CoverImage = new MetadataFile(data.StoreDetails.imageServer + data.StoreDetails.i18n.packArtLarge);
            if (!string.IsNullOrEmpty(data.StoreDetails.offerPath))
            {
                data.StoreMetadata = OriginApiClient.GetStoreMetadata(data.StoreDetails.offerPath);
                var bkData = data.StoreMetadata?.gamehub.components.items?.FirstOrDefault(a => a.ContainsKey("origin-store-pdp-hero"));
                if (bkData != null)
                {
                    dynamic test = bkData["origin-store-pdp-hero"];
                    var background = test["background-image"];
                    if (background != null)
                    {
                        data.BackgroundImage = new MetadataFile(background.ToString());
                    }
                }
            }

            return data;
        }
    }
}
