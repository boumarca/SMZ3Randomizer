﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

using Randomizer.SMZ3;

namespace Randomizer.App.ViewModels
{
    /// <summary>
    /// Represents user-configurable options for influencing seed generation.
    /// </summary>
    public class SeedOptions
    {
        [JsonIgnore]
        public string Seed { get; set; }

        [JsonIgnore]
        public string ConfigString { get; set; }

        public ItemPlacement SwordLocation { get; set; }

        public ItemPlacement MorphLocation { get; set; }

        public ItemPlacement MorphBombsLocation { get; set; }

        public ItemPlacement PegasusBootsLocation { get; set; }

        public ItemPlacement SpaceJumpLocation { get; set; }

        public ItemPool ShaktoolItem { get; set; }

        public ItemPool PegWorldItem { get; set; }

        public bool Keysanity { get; set; }

        public bool Race { get; set; }
    }
}
