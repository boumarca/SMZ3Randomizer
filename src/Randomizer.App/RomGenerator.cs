﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Microsoft.Extensions.Logging;

using Randomizer.App.Patches;
using Randomizer.Data.Options;
using Randomizer.Data.Services;
using Randomizer.Data.WorldData.Regions;
using Randomizer.Shared;
using Randomizer.Shared.Models;
using Randomizer.SMZ3;
using Randomizer.SMZ3.FileData;
using Randomizer.SMZ3.Generation;

namespace Randomizer.App
{
    /// <summary>
    /// Class to handle generating roms
    /// </summary>
    public class RomGenerator
    {
        private readonly RandomizerContext _dbContext;
        private readonly Smz3Randomizer _randomizer;
        private readonly Smz3Plandomizer _plandomizer;
        private readonly ILogger<RomGenerator> _logger;
        private readonly ITrackerStateService _stateService;
        private readonly MsuGeneratorService _msuGeneratorService;

        public RomGenerator(Smz3Randomizer randomizer,
            Smz3Plandomizer plandomizer,
            RandomizerContext dbContext,
            ILogger<RomGenerator> logger,
            ITrackerStateService stateService,
            MsuGeneratorService msuGeneratorService
        )
        {
            _randomizer = randomizer;
            _plandomizer = plandomizer;
            _dbContext = dbContext;
            _logger = logger;
            _stateService = stateService;
            _msuGeneratorService = msuGeneratorService;
        }

        /// <summary>
        /// Generates a randomizer ROM and returns details about the rom
        /// </summary>
        /// <param name="options">The randomizer generation options</param>
        /// <param name="attempts">The number of times the rom should be attempted to be generated</param>
        /// <returns>True if the rom was generated successfully, false otherwise</returns>
        public async Task<GeneratedRom?> GenerateRandomRomAsync(RandomizerOptions options, int attempts = 5)
        {
            var latestError = "";
            var seed = (SeedData?)null;
            var validated = false;

            for (var i = 0; i < attempts; i++)
            {
                try
                {
                    seed = GenerateSeed(options);
                    if (!_randomizer.ValidateSeedSettings(seed))
                    {
                        latestError = "";
                        validated = false;
                    }
                    else
                    {
                        validated = true;
                        break;
                    }
                }
                catch (RandomizerGenerationException e)
                {
                    seed = null;
                    latestError = $"Error generating rom\n{e.Message}\nPlease try again. If it persists, try modifying your seed settings.";
                }
            }

            if (seed != null)
            {
                if (!validated && MessageBox.Show("The seed generated is playable but does not contain all requested settings.\n" +
                        "Retrying to generate the seed may work, but the selected settings may be impossible to generate successfully and will need to be updated.\n" +
                        "Continue with the current seed that does not meet all requested settings?", "SMZ3 Cas’ Randomizer", MessageBoxButtons.YesNo) == DialogResult.No)
                {
                    return null;
                }
                else
                {
                    var results = await GenerateRomInternalAsync(seed, options, null);
                    if (!string.IsNullOrEmpty(results.MsuError))
                    {
                        MessageBox.Show(results.MsuError, "SMZ3 Cas’ Randomizer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    return results.Rom;
                }
            }
            else
            {
                MessageBox.Show(
                    !string.IsNullOrEmpty(latestError)
                        ? latestError
                        : "There was an unknown error creating the rom. Please check your settings and try again.",
                    "SMZ3 Cas’ Randomizer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

        }

        /// <summary>
        /// Generates a plando ROM and returns details about the rom
        /// </summary>
        /// <param name="options">The randomizer generation options</param>
        /// <param name="plandoConfig">Config with the details of how to generate the rom</param>
        /// <returns>True if the rom was generated successfully, false otherwise</returns>
        public async Task<GeneratedRom?> GeneratePlandoRomAsync(RandomizerOptions options, PlandoConfig plandoConfig)
        {
            try
            {
                var seed = GeneratePlandoSeed(options, plandoConfig);
                var results = await GenerateRomInternalAsync(seed, options, null);

                if (!string.IsNullOrEmpty(results.MsuError))
                {
                    MessageBox.Show("There was an error assigning the MSU\n" + results.MsuError, "SMZ3 Cas’ Randomizer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                return results.Rom;
            }
            catch (PlandoConfigurationException e)
            {
                var error = $"The plando configuration is invalid or incomplete.\n{e.Message}\nPlease check the plando configuration file you used and try again.";
                MessageBox.Show(error, "SMZ3 Cas’ Randomizer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        public async Task<GeneratedRom?> GeneratePreSeededRomAsync(RandomizerOptions options, SeedData seed, MultiplayerGameDetails multiplayerGameDetails)
        {
            var results = await GenerateRomInternalAsync(seed, options, multiplayerGameDetails);

            if (!string.IsNullOrEmpty(results.MsuError))
            {
                MessageBox.Show("There was an error assigning the MSU\n" + results.MsuError, "SMZ3 Cas’ Randomizer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }


            return results.Rom;
        }

        private async Task<GenerateRomResults> GenerateRomInternalAsync(SeedData seed, RandomizerOptions options, MultiplayerGameDetails? multiplayerGameDetails)
        {
            var bytes = GenerateRomBytes(options, seed);
            var config = seed.Playthrough.Config;
            var safeSeed = seed.Seed.ReplaceAny(Path.GetInvalidFileNameChars(), '_');

            var folderPath = Path.Combine(options.RomOutputPath, $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}_{safeSeed}");
            Directory.CreateDirectory(folderPath);

            // For BizHawk shuffler support, the file name is checked when running the BizHawk Auto Tracking Lua script
            // If the fom file name is changed, make sure to update the BizHawk emulator.lua script and the LuaConnector
            var fileSuffix = $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}_{safeSeed}";
            var romFileName = $"SMZ3_Cas_{fileSuffix}.sfc";
            var romPath = Path.Combine(folderPath, romFileName);
            _msuGeneratorService.EnableMsu1Support(options, bytes, romPath, seed.WorldGenerationData.LocalWorld, out var msuError);
            Rom.UpdateChecksum(bytes);
            await File.WriteAllBytesAsync(romPath, bytes);

            var spoilerLog = GetSpoilerLog(options, seed, config.Race || config.DisableSpoilerLog);
            var spoilerFileName = $"Spoiler_Log_{fileSuffix}.txt";
            var spoilerPath = Path.Combine(folderPath, spoilerFileName);
            await File.WriteAllTextAsync(spoilerPath, spoilerLog);

            var plandoConfigString = ExportPlandoConfig(seed);
            if (!string.IsNullOrEmpty(plandoConfigString))
            {
                var plandoFileName = $"Spoiler_Plando_{fileSuffix}.yml";
                var plandoPath = Path.Combine(folderPath, plandoFileName);
                await File.WriteAllTextAsync(plandoPath, plandoConfigString);
            }

            PrepareAutoTrackerFiles(options);

            var rom = await SaveSeedToDatabaseAsync(options, seed, romPath, spoilerPath, multiplayerGameDetails);

            return new GenerateRomResults()
            {
                Rom = rom,
                MsuError = msuError
            };
        }

        private string? ExportPlandoConfig(SeedData seed)
        {
            try
            {
                if (seed.WorldGenerationData.Count > 1)
                {
                    _logger.LogWarning("Attempting to export plando config for multi-world seed. Skipping.");
                    return null;
                }

                var world = seed.WorldGenerationData.LocalWorld.World;
                var plandoConfig = new PlandoConfig(world);

                var serializer = new YamlDotNet.Serialization.Serializer();
                return serializer.Serialize(plandoConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred while exporting the plando configuration for seed {Seed}. No plando config will be generated.", seed.Seed);
                return null;
            }
        }

        private void PrepareAutoTrackerFiles(RandomizerOptions options)
        {
            try
            {
#if DEBUG
                var autoTrackerSourcePath = Path.Combine(GetSourceDirectory(), "Randomizer.SMZ3.Tracking\\AutoTracking\\LuaScripts");
#else
                    var autoTrackerSourcePath = Environment.ExpandEnvironmentVariables("%LocalAppData%\\SMZ3CasRandomizer\\AutotrackerScripts");
#endif
                var autoTrackerDestPath = options.AutoTrackerScriptsOutputPath;

                if (!autoTrackerSourcePath.Equals(autoTrackerDestPath, StringComparison.OrdinalIgnoreCase))
                {
                    CopyDirectory(autoTrackerSourcePath, autoTrackerDestPath, true, true);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Unable to copy Autotracker Scripts");
            }
        }

        /// <summary>
        /// Generates a seed for a rom based on the given randomizer options
        /// </summary>
        /// <param name="options">The randomizer generation options</param>
        /// <param name="seed">The string seed to use for generating the rom</param>
        /// <returns>The seed data</returns>
        public SeedData GenerateSeed(RandomizerOptions options, string? seed = null)
        {
            var config = options.ToConfig();
            return _randomizer.GenerateSeed(config, seed ?? config.Seed, CancellationToken.None);
        }

        public SeedData GeneratePlandoSeed(RandomizerOptions options, PlandoConfig plandoConfig)
        {
            var config = options.ToConfig();
            config.PlandoConfig = plandoConfig;
            config.KeysanityMode = plandoConfig.KeysanityMode;
            config.GanonsTowerCrystalCount = plandoConfig.GanonsTowerCrystalCount;
            config.GanonCrystalCount = plandoConfig.GanonCrystalCount;
            config.OpenPyramid = plandoConfig.OpenPyramid;
            config.TourianBossCount = plandoConfig.TourianBossCount;
            config.LogicConfig = plandoConfig.Logic.Clone();
            return _plandomizer.GenerateSeed(config, CancellationToken.None);
        }

        public SeedData GenerateMultiworldSeed(List<Config> configs)
        {
            return _randomizer.GenerateSeed(configs, seed: null);
        }

        /// <summary>
        /// Uses the options to generate the rom
        /// </summary>
        /// <param name="options">The randomizer generation options</param>
        /// <param name="seed">The seed data to write to the ROM.</param>
        /// <returns>The bytes of the rom file</returns>
        protected byte[] GenerateRomBytes(RandomizerOptions options, SeedData seed)
        {
            if (string.IsNullOrEmpty(options.GeneralOptions.SMRomPath) ||
                string.IsNullOrEmpty(options.GeneralOptions.Z3RomPath))
                throw new InvalidOperationException("Super Metroid or Zelda rom path is not specified");

            byte[] rom;
            using (var smRom = File.OpenRead(options.GeneralOptions.SMRomPath))
            using (var z3Rom = File.OpenRead(options.GeneralOptions.Z3RomPath))
            {
                rom = Rom.CombineSMZ3Rom(smRom, z3Rom);
            }

            using (var ips = IpsPatch.Smz3())
            {
                Rom.ApplyIps(rom, ips);
            }
            Rom.ApplySeed(rom, seed.WorldGenerationData.LocalWorld.Patches);

            options.PatchOptions.SamusSprite.ApplyTo(rom);
            options.PatchOptions.LinkSprite.ApplyTo(rom);

            if (options.PatchOptions.CasPatches.Respin)
            {
                using var patch = IpsPatch.Respin();
                Rom.ApplySuperMetroidIps(rom, patch);
            }

            if (options.PatchOptions.CasPatches.NerfedCharge)
            {
                using var patch = IpsPatch.NerfedCharge();
                Rom.ApplySuperMetroidIps(rom, patch);
            }

            if (options.PatchOptions.CasPatches.RefillAtSaveStation)
            {
                using var patch = IpsPatch.RefillAtSaveStation();
                Rom.ApplySuperMetroidIps(rom, patch);
            }

            if (options.PatchOptions.CasPatches.FastDoors)
            {
                using var patch = IpsPatch.FastDoors();
                Rom.ApplySuperMetroidIps(rom, patch);
            }

            if (options.PatchOptions.CasPatches.FastElevators)
            {
                using var patch = IpsPatch.FastElevators();
                Rom.ApplySuperMetroidIps(rom, patch);
            }

            if (options.PatchOptions.CasPatches.AimAnyButton)
            {
                using var patch = IpsPatch.AimAnyButton();
                Rom.ApplySuperMetroidIps(rom, patch);
            }

            if (options.PatchOptions.CasPatches.Speedkeep)
            {
                using var patch = IpsPatch.SpeedKeep();
                Rom.ApplySuperMetroidIps(rom, patch);
            }

            if (options.PatchOptions.CasPatches.DisableFlashing)
            {
                using var patch = IpsPatch.DisableMetroidFlashing();
                Rom.ApplySuperMetroidIps(rom, patch);
            }

            if (options.PatchOptions.CasPatches.DisableScreenShake)
            {
                using var patch = IpsPatch.DisableMetroidScreenShake();
                Rom.ApplySuperMetroidIps(rom, patch);
            }

            if (options.PatchOptions.CasPatches.EasierWallJumps)
            {
                using var patch = IpsPatch.EasierWallJumps();
                Rom.ApplySuperMetroidIps(rom, patch);
            }

            if (options.PatchOptions.CasPatches.SnapMorph)
            {
                using var patch = IpsPatch.SnapMorph();
                Rom.ApplySuperMetroidIps(rom, patch);
            }

            if (options.PatchOptions.MetroidControls.RunButtonBehavior == RunButtonBehavior.AutoRun)
            {
                using var patch = IpsPatch.AutoRun();
                Rom.ApplySuperMetroidIps(rom, patch);
            }

            if (options.PatchOptions.MetroidControls.ItemCancelBehavior != ItemCancelBehavior.Vanilla)
            {
                using var patch = options.PatchOptions.MetroidControls.ItemCancelBehavior == ItemCancelBehavior.Toggle ? IpsPatch.ItemCancelToggle() : IpsPatch.ItemCancelHoldFire();
                Rom.ApplySuperMetroidIps(rom, patch);
            }

            if (options.PatchOptions.MetroidControls.AimButtonBehavior == AimButtonBehavior.UnifiedAim)
            {
                using var patch = IpsPatch.UnifiedAim();
                Rom.ApplySuperMetroidIps(rom, patch);
            }

            if (options.PatchOptions.ShipPatch.FileName != null)
            {

                var shipPatchFileName = Path.Combine(AppContext.BaseDirectory, "Sprites", "Ships", options.PatchOptions.ShipPatch.FileName);
                if (File.Exists(shipPatchFileName))
                {
                    using var customShipBasePatch = IpsPatch.CustomShip();
                    Rom.ApplySuperMetroidIps(rom, customShipBasePatch);

                    using var shipPatch = File.OpenRead(shipPatchFileName);
                    Rom.ApplySuperMetroidIps(rom, shipPatch);
                }
            }

            return rom;
        }

        /// <summary>
        /// Takes the given seed information and saves it to the database
        /// </summary>
        /// <param name="options">The randomizer generation options</param>
        /// <param name="seed">The generated seed data</param>
        /// <param name="romPath">The path to the rom file</param>
        /// <param name="spoilerPath">The path to the spoiler file</param>
        /// <param name="multiplayerGameDetails">Details of the connected multiplayer game</param>
        /// <returns>The db entry for the generated rom</returns>
        protected async Task<GeneratedRom> SaveSeedToDatabaseAsync(RandomizerOptions options, SeedData seed, string romPath, string spoilerPath, MultiplayerGameDetails? multiplayerGameDetails)
        {
            var settingsString = seed.Configs.Count() > 1
                ? Config.ToConfigString(seed.Configs)
                : Config.ToConfigString(seed.PrimaryConfig, true);

            var rom = new GeneratedRom()
            {
                Seed = seed.Seed,
                RomPath = Path.GetRelativePath(options.RomOutputPath, romPath),
                SpoilerPath = Path.GetRelativePath(options.RomOutputPath, spoilerPath),
                Date = DateTimeOffset.Now,
                Settings = settingsString,
                GeneratorVersion = Smz3Randomizer.Version.Major,
                MultiplayerGameDetails = multiplayerGameDetails,
            };
            _dbContext.GeneratedRoms.Add(rom);

            if (multiplayerGameDetails != null)
            {
                multiplayerGameDetails.GeneratedRom = rom;
            }

            await _stateService.CreateStateAsync(seed.WorldGenerationData.Worlds, rom);
            return rom;
        }

        /// <summary>
        /// Underlines text in the spoiler log
        /// </summary>
        /// <param name="text">The text to be underlined</param>
        /// <param name="line">The character to use for underlining</param>
        /// <returns>The text to be underlined followed by the underlining text</returns>
        private static string Underline(string text, char line = '-')
            => text + "\n" + new string(line, text.Length);

        /// <summary>
        /// Gets the spoiler log of a given seed
        /// </summary>
        /// <param name="options">The randomizer generation options</param>
        /// <param name="seed">The previously generated seed data</param>
        /// <param name="configOnly">If the spoiler log should only have config settings printed</param>
        /// <returns>The string output of the spoiler log file</returns>
        private string GetSpoilerLog(RandomizerOptions options, SeedData seed, bool configOnly)
        {
            var log = new StringBuilder();
            log.AppendLine(Underline($"SMZ3 Cas’ spoiler log", '='));
            log.AppendLine($"Generated on {DateTime.Now:F}");
            log.AppendLine($"Seed: {options.SeedOptions.Seed} (actual: {seed.Seed})");

            if (options.SeedOptions.Race)
            {
                log.AppendLine("[Race]");
            }

            log.AppendLine();
            log.AppendLine(Underline("Settings", '='));
            log.AppendLine();

            foreach (var world in seed.WorldGenerationData.Worlds)
            {
                if (world.Config.MultiWorld)
                {
                    log.AppendLine(Underline("Player: " + world.Player));
                    log.AppendLine();
                }

                log.AppendLine($"Settings String: {Config.ToConfigString(world.Config, true)}");
                log.AppendLine($"Early Items: {string.Join(',', ItemSettingOptions.GetEarlyItemTypes(world.Config).Select(x => x.ToString()).ToArray())}");
                log.AppendLine($"Starting Inventory: {string.Join(',', ItemSettingOptions.GetStartingItemTypes(world.Config).Select(x => x.ToString()).ToArray())}");

                var locationPrefs = new List<string>();
                foreach (var (locationId, value) in world.Config.LocationItems)
                {
                    var itemPref = value < Enum.GetValues(typeof(ItemPool)).Length ? ((ItemPool)value).ToString() : ((ItemType)value).ToString();
                    locationPrefs.Add($"{world.Locations.First(x => x.Id == locationId).Name} - {itemPref}");
                }
                log.AppendLine($"Location Preferences: {string.Join(',', locationPrefs.ToArray())}");

                var type = options.LogicConfig.GetType();
                var logicOptions = string.Join(',', type.GetProperties().Select(x => $"{x.Name}: {x.GetValue(world.Config.LogicConfig)}"));
                log.AppendLine($"Logic Options: {logicOptions}");

                if (world.Config.Keysanity)
                {
                    log.AppendLine("Keysanity: " + world.Config.KeysanityMode.ToString());
                }

                var gtCrystals = world.Config.GanonsTowerCrystalCount;
                var ganonCrystals = world.Config.GanonCrystalCount;
                var pyramid = world.Config.OpenPyramid ? "Open" : "Closed";
                var tourianBosses = world.Config.TourianBossCount;
                log.AppendLine($"Win Conditions: GT = {gtCrystals} Crystals, Ganon = {ganonCrystals} Crystals, Pyramid = {pyramid}, Tourian = {tourianBosses} Bosses");

                log.AppendLine();
            }

            if (File.Exists(options.PatchOptions.Msu1Path))
                log.AppendLine($"MSU-1 pack: {Path.GetFileNameWithoutExtension(options.PatchOptions.Msu1Path)}");
            log.AppendLine();

            if (configOnly)
            {
                return log.ToString();
            }

            log.AppendLine();
            log.AppendLine(Underline("Hints", '='));
            log.AppendLine();

            foreach (var worldGenerationData in seed.WorldGenerationData)
            {
                if (worldGenerationData.Config.MultiWorld)
                {
                    log.AppendLine(Underline("Player: " + worldGenerationData.World.Player));
                    log.AppendLine();
                }

                foreach (var hint in worldGenerationData.Hints)
                {
                    log.AppendLine(hint);
                }
                log.AppendLine();
            }

            log.AppendLine();
            log.AppendLine(Underline("Spheres", '='));
            log.AppendLine();

            var spheres = seed.Playthrough.GetPlaythroughText();
            for (var i = 0; i < spheres.Count; i++)
            {
                if (spheres[i].Count == 0)
                    continue;

                log.AppendLine(Underline($"Sphere {i + 1}"));
                log.AppendLine();
                foreach (var (location, item) in spheres[i])
                    log.AppendLine($"{location}: {item}");
                log.AppendLine();
            }

            log.AppendLine();
            log.AppendLine(Underline("Dungeons", '='));
            log.AppendLine();

            foreach (var world in seed.WorldGenerationData.Worlds)
            {
                log.AppendLine(world.Config.MultiWorld
                    ? Underline("Player " + world.Player + " Rewards")
                    : Underline("Rewards"));

                log.AppendLine();

                foreach (var region in world.Regions)
                {
                    if (region is IHasReward rewardRegion)
                        log.AppendLine($"{region.Name}: {rewardRegion.RewardType}");
                }
                log.AppendLine();

                log.AppendLine(world.Config.MultiWorld
                    ? Underline("Player " + world.Player + " Medallions")
                    : Underline("Medallions"));
                log.AppendLine();

                foreach (var region in world.Regions)
                {
                    if (region is INeedsMedallion medallionReegion)
                        log.AppendLine($"{region.Name}: {medallionReegion.Medallion}");
                }
                log.AppendLine();
            }

            log.AppendLine();
            log.AppendLine(Underline("All Items", '='));
            log.AppendLine();

            foreach (var world in seed.WorldGenerationData.Worlds)
            {
                if (world.Config.MultiWorld)
                {
                    log.AppendLine(Underline("Player: " + world.Player));
                    log.AppendLine();
                }

                foreach (var location in world.Locations)
                {
                    log.AppendLine(world.Config.MultiWorld
                        ? $"{location}: {location.Item} ({location.Item.World.Player})"
                        : $"{location}: {location.Item}");
                }
                log.AppendLine();
            }

            return log.ToString();
        }



        private string GetSourceDirectory()
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            var directory = Directory.GetParent(currentDirectory);
            while (directory != null && directory.Name != "src")
            {
                directory = Directory.GetParent(directory.FullName);
            }
            return directory?.FullName ?? currentDirectory;
        }

        private void CopyDirectory(string source, string dest, bool recursive, bool overwrite)
        {
            var sourceDir = new DirectoryInfo(source);

            var sourceSubDirs = sourceDir.GetDirectories();

            if (!Directory.Exists(dest))
            {
                Directory.CreateDirectory(dest);
            }

            foreach (var file in sourceDir.GetFiles())
            {
                if (overwrite && File.Exists(Path.Combine(dest, file.Name)))
                {
                    File.Delete(Path.Combine(dest, file.Name));
                }

                if (!File.Exists(Path.Combine(dest, file.Name)))
                {
                    file.CopyTo(Path.Combine(dest, file.Name));
                }
            }

            if (recursive)
            {
                foreach (var subDir in sourceSubDirs)
                {
                    var destSubDir = Path.Combine(dest, subDir.Name);
                    CopyDirectory(subDir.FullName, destSubDir, recursive, overwrite);
                }
            }
        }

        private class GenerateRomResults
        {
            public GeneratedRom? Rom { get; init; }
            public string? MsuError { get; init; }
        }
    }
}
