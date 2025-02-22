﻿using Playnite.SDK.Metadata;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GogLibrary.Models
{
    public class GogGameMetadata : GameMetadata
    {
        public ProductApiDetail GameDetails { get; set; }
        public StorePageResult.ProductDetails StoreDetails { get; set; }
        public MetadataFile Icon { get; set; }
        public MetadataFile CoverImage { get; set; }
        public MetadataFile BackgroundImage { get; set; }
    }
}
