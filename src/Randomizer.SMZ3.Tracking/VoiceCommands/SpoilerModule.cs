﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.Extensions.Logging;

using Randomizer.Shared;
using Randomizer.SMZ3.Regions;
using Randomizer.SMZ3.Tracking.Configuration;

namespace Randomizer.SMZ3.Tracking.VoiceCommands
{
    /// <summary>
    /// Provides voice commands that reveal about items and locations.
    /// </summary>
    public class SpoilerModule : TrackerModule, IOptionalModule
    {
        private readonly Dictionary<ItemType, int> _itemHintsGiven = new();
        private readonly Playthrough _playthrough;

        /// <summary>
        /// Initializes a new instance of the <see cref="SpoilerModule"/> class.
        /// </summary>
        /// <param name="tracker">The tracker instance.</param>
        /// <param name="logger">Used to write logging information.</param>
        public SpoilerModule(Tracker tracker, ILogger<SpoilerModule> logger)
            : base(tracker, logger)
        {
            HintsEnabled = tracker.Options.HintsEnabled;
            SpoilersEnabled = tracker.Options.SpoilersEnabled;
            _playthrough = Playthrough.Generate(new[] { tracker.World }, tracker.World.Config);

            AddCommand("Enable hints", GetEnableHintsRule(), (tracker, result) =>
            {
                HintsEnabled = true;
                tracker.Say(x => x.Hints.EnabledHints);
            });
            AddCommand("Disable hints", GetDisableHintsRule(), (tracker, result) =>
            {
                HintsEnabled = false;
                tracker.Say(x => x.Hints.DisabledHints);
            });
            AddCommand("Enable spoilers", GetEnableSpoilersRule(), (tracker, result) =>
            {
                SpoilersEnabled = true;
                tracker.Say(x => x.Spoilers.EnabledSpoilers);
            });
            AddCommand("Disable spoilers", GetDisableSpoilersRule(), (tracker, result) =>
            {
                SpoilersEnabled = false;
                tracker.Say(x => x.Spoilers.DisabledSpoilers);
            });

            AddCommand("Reveal item location", GetItemSpoilerRule(), (tracker, result) =>
            {
                var item = GetItemFromResult(tracker, result, out var itemName);
                RevealItemLocation(item);
            });

            AddCommand("Reveal location item", GetLocationSpoilerRule(), (tracker, result) =>
            {
                var location = GetLocationFromResult(tracker, result);
                RevealLocationItem(location);
            });
        }

        /// <summary>
        /// Gets or sets a value indicating whether Tracker may give hints when
        /// asked about items or locations.
        /// </summary>
        public bool HintsEnabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether Tracker may give spoilers
        /// when asked about items or locations.
        /// </summary>
        public bool SpoilersEnabled { get; set; }

        /// <summary>
        /// Gives a hint or spoiler about the location of an item.
        /// </summary>
        /// <param name="item">The item to find.</param>
        public void RevealItemLocation(ItemData item)
        {
            if (item.HasStages && item.TrackingState >= item.MaxStage)
            {
                Tracker.Say(x => x.Spoilers.TrackedAllItemsAlready, item.Name);
                return;
            }
            else if (!item.Multiple && item.TrackingState > 0)
            {
                Tracker.Say(x => x.Spoilers.TrackedItemAlready, item.NameWithArticle);
                return;
            }

            var markedLocation = Tracker.MarkedLocations
                .Where(x => x.Value.InternalItemType == item.InternalItemType)
                .Select(x => Tracker.World.Locations.Single(y => y.Id == x.Key))
                .Where(x => !x.Cleared)
                .Random();
            if (markedLocation != null)
            {
                var locationName = Tracker.GetName(markedLocation);
                var regionName = Tracker.WorldInfo.Region(markedLocation.Region).Name;
                Tracker.Say(x => x.Spoilers.MarkedItem, item.NameWithArticle, locationName, regionName);
                return;
            }

            if (HintsEnabled && GiveItemLocationHint(item))
                return;

            if (SpoilersEnabled && GiveItemLocationSpoiler(item))
                return;

            if (!HintsEnabled)
            {
                Tracker.Say(x => x.Hints.PromptEnableItemHints);
                return;
            }

            if (!SpoilersEnabled)
            {
                Tracker.Say(x => x.Spoilers.PromptEnableItemSpoilers);
                return;
            }

            if (item.Multiple || item.HasStages)
                Tracker.Say(x => x.Spoilers.ItemsNotFound, item.Plural);
            else
                Tracker.Say(x => x.Spoilers.ItemNotFound, item.NameWithArticle);
        }

        /// <summary>
        /// Gives a hint or spoiler about the given location.
        /// </summary>
        /// <param name="location">The location to ask about.</param>
        public void RevealLocationItem(Location location)
        {
            var locationName = Tracker.WorldInfo.Location(location).Name;
            if (Tracker.MarkedLocations.TryGetValue(location.Id, out var markedItem))
            {
                Tracker.Say(x => x.Spoilers.MarkedLocation, locationName, markedItem.NameWithArticle);
                return;
            }

            if (!SpoilersEnabled)
            {
                Tracker.Say(x => x.Spoilers.PromptEnableLocationSpoilers);
                return;
            }

            if (location.Item == null || location.Item.Type == ItemType.Nothing)
            {
                Tracker.Say(x => x.Spoilers.EmptyLocation, locationName);
                return;
            }

            var item = Tracker.Items.FirstOrDefault(x => x.InternalItemType == location.Item.Type);
            if (item != null)
            {
                Tracker.Say(x => x.Spoilers.LocationHasItem, locationName, item.NameWithArticle);
                return;
            }
            else
            {
                Tracker.Say(x => x.Spoilers.LocationHasUnknownItem, locationName, location.Item);
                return;
            }
        }

        internal bool GiveItemLocationSpoiler(ItemData item)
        {
            var progression = Tracker.GetProgression(assumeKeys: Tracker.World.Config.Keysanity);
            var reachableLocation = Tracker.World.Locations
                .Where(x => x.Item.Type == item.InternalItemType)
                .Where(x => !x.Cleared)
                .Where(x => x.IsAvailable(progression))
                .Random();
            if (reachableLocation != null)
            {
                var locationName = Tracker.WorldInfo.Location(reachableLocation).Name;
                var regionName = Tracker.WorldInfo.Region(reachableLocation.Region).Name;
                if (item.Multiple || item.HasStages)
                    Tracker.Say(x => x.Spoilers.ItemsAreAtLocation, item.NameWithArticle, locationName, regionName);
                else
                    Tracker.Say(x => x.Spoilers.ItemIsAtLocation, item.NameWithArticle, locationName, regionName);
                return true;
            }

            var worldLocation = Tracker.World.Locations
                .Where(x => x.Item.Type == item.InternalItemType)
                .Where(x => !x.Cleared)
                .Random();
            if (worldLocation != null)
            {
                var locationName = Tracker.WorldInfo.Location(worldLocation).Name;
                var regionName = Tracker.WorldInfo.Region(worldLocation.Region).Name;
                if (item.Multiple || item.HasStages)
                    Tracker.Say(x => x.Spoilers.ItemsAreAtOutOfLogicLocation, item.NameWithArticle, locationName, regionName);
                else
                    Tracker.Say(x => x.Spoilers.ItemIsAtOutOfLogicLocation, item.NameWithArticle, locationName, regionName);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gives the specified item hint for the specified item.
        /// </summary>
        /// <param name="selectHint">Selects the hint to give.</param>
        /// <param name="item">The item that was asked about.</param>
        /// <param name="additionalArgs">
        /// ADditional arguments used to format the hint text.
        /// </param>
        /// <returns>
        /// <c>true</c> if a hint was given. <c>false</c> if the selected hint
        /// was null, empty or returned a <c>null</c> hint.
        /// </returns>
        protected virtual bool GiveItemHint(Func<HintsConfig, SchrodingersString?> selectHint,
            ItemData item, params object?[] additionalArgs)
        {
            var args = Args.Combine(item.NameWithArticle, additionalArgs);
            if (!Tracker.Say(responses => selectHint(responses.Hints), args))
                return false;

            if (_itemHintsGiven.ContainsKey(item.InternalItemType))
                _itemHintsGiven[item.InternalItemType]++;
            else
                _itemHintsGiven[item.InternalItemType] = 1;
            return true;
        }

        /// <summary>
        /// Gives the specified item hint for the specified item.
        /// </summary>
        /// <param name="hint">The hint to give.</param>
        /// <param name="item">The item that was asked about.</param>
        /// <param name="additionalArgs">
        /// Additional arguments used to format the hint text.
        /// </param>
        /// <returns>
        /// <c>true</c> if a hint was given. <c>false</c> if <paramref
        /// name="hint"/> was null, empty or returned a <c>null</c> hint.
        /// </returns>
        protected virtual bool GiveItemHint(SchrodingersString? hint,
            ItemData item, params object?[] additionalArgs)
        {
            var args = Args.Combine(item.NameWithArticle, additionalArgs);
            if (!Tracker.Say(hint, args))
                return false;

            if (_itemHintsGiven.ContainsKey(item.InternalItemType))
                _itemHintsGiven[item.InternalItemType]++;
            else
                _itemHintsGiven[item.InternalItemType] = 1;
            return true;
        }

        private bool GiveItemLocationHint(ItemData item)
        {
            var progression = Tracker.GetProgression(assumeKeys: Tracker.World.Config.Keysanity);
            var itemLocations = Tracker.World.Locations
                .Where(x => x.Item.Type == item.InternalItemType)
                .Where(x => !x.Cleared);

            switch (HintsGiven(item))
            {
                // If unobtainable, give hint about that. Otherwise, give hint
                // about where the item is NOT
                case 0:
                    {
                        var isInLogic = itemLocations.Any(x => x.IsAvailable(progression));
                        if (!isInLogic)
                            return GiveItemHint(x => x.ItemNotInLogic, item);

                        var isOnlyInSuperMetroid = itemLocations.Select(x => x.Region).All(x => x is SMRegion);
                        if (isOnlyInSuperMetroid)
                            return GiveItemHint(x => x.ItemInSuperMetroid, item);

                        var isOnlyInALinkToThePast = itemLocations.Select(x => x.Region).All(x => x is Z3Region);
                        if (isOnlyInALinkToThePast)
                            return GiveItemHint(x => x.ItemInALttP, item);

                        var regionWithoutItem = Tracker.World.Locations
                            .Except(itemLocations)
                            .Select(x => x.Region)
                            .Random();
                        if (regionWithoutItem != null)
                            return GiveItemHint(x => x.ItemNotInArea, item, regionWithoutItem.Area);

                        // In the unlikely event it's in every region: Increase
                        // the counter and let the player try again for a more
                        // specific hint.
                        return GiveItemHint(x => x.NoApplicableHints, item);
                    }

                case 1:
                    {
                        if (!itemLocations.Any(x => x.IsAvailable(progression)))
                        {
                            // TODO: Determine what item(s) are missing from
                            // logic, then give a hint about an item that has
                            // hints
                        }

                        var sphere = _playthrough.Spheres.IndexOf(x => x.Items.Any(i => i.Type == item.InternalItemType));
                        var earlyThreshold = Math.Floor(_playthrough.Spheres.Count * 0.25);
                        var lateThreshold = Math.Ceiling(_playthrough.Spheres.Count * 0.75);
                        Logger.LogDebug("Giving spoiler for {Item}: sphere {Sphere}, early: {Early}, late: {Late}", item, sphere, earlyThreshold, lateThreshold);
                        if (sphere == 0)
                            return GiveItemHint(x => x.ItemInSphereZero, item);
                        if (sphere <= earlyThreshold)
                            return GiveItemHint(x => x.ItemInEarlySphere, item);
                        if (sphere >= lateThreshold)
                            return GiveItemHint(x => x.ItemInLateSphere, item);

                        return GiveItemHint(x => x.NoApplicableHints, item);
                    }

                case 2:
                    {
                        var randomLocation = GetRandomItemLocationWithFilter(item, x => true);
                        if (randomLocation?.Region is Z3Region and IHasReward dungeon && dungeon.Reward != Reward.Agahnim)
                        {
                            if (randomLocation.Region.Locations.Any(x => x.Cleared))
                                return GiveItemHint(x => x.ItemInPreviouslyVisitedDungeon, item);
                            else
                                return GiveItemHint(x => x.ItemInUnvisitedDungeon, item);
                        }

                        if (randomLocation?.Region.Locations.Any(x => x.Cleared) == true)
                            return GiveItemHint(x => x.ItemInPreviouslyVisitedRegion, item);

                        var randomLocationWithHint = GetRandomItemLocationWithFilter(item,
                            l => Tracker.WorldInfo.Region(l.Region).Hints?.Count > 0);

                        if (randomLocationWithHint != null)
                        {
                            var regionHint = Tracker.WorldInfo.Region(randomLocationWithHint.Region).Hints;
                            if (regionHint != null && regionHint.Count > 0)
                                return GiveItemHint(regionHint, item);
                        }

                        return GiveItemHint(x => x.NoApplicableHints, item);
                    }

                case 3:
                    {
                        var randomLocation = GetRandomItemLocationWithFilter(item,
                            location =>
                            {
                                if (location.Room != null && Tracker.WorldInfo.Room(location.Room).Hints?.Count > 0)
                                    return true;
                                return Tracker.WorldInfo.Location(location).Hints?.Count > 0;
                            });

                        if (randomLocation != null)
                        {
                            if (randomLocation.Room != null)
                            {
                                var roomHint = Tracker.WorldInfo.Room(randomLocation.Room).Hints;
                                if (roomHint != null && roomHint.Count > 0)
                                {
                                    return GiveItemHint(roomHint, item);
                                }
                            }

                            var locationHint = Tracker.WorldInfo.Location(randomLocation).Hints;
                            if (locationHint != null && locationHint.Count > 0)
                            {
                                return GiveItemHint(locationHint, item);
                            }
                        }
                        else
                        {
                            // If there isn't any location with this item that
                            // has a hint, let it fall through so tracker can
                            // tell the player to enable spoilers
                            Logger.LogInformation("No level 3 hints for {Item}", item);
                        }
                    }
                    break;
            }

            return false;

            int HintsGiven(ItemData item) => _itemHintsGiven.GetValueOrDefault(item.InternalItemType, 0);
        }

        private Location? GetRandomItemLocationWithFilter(ItemData item, Func<Location, bool> predicate)
        {
            var progression = Tracker.GetProgression(assumeKeys: Tracker.World.Config.Keysanity);
            var randomLocation = Tracker.World.Locations
                .Where(x => x.Item.Type == item.InternalItemType)
                .Where(x => !x.Cleared)
                .Where(x => x.IsAvailable(progression))
                .Where(predicate)
                .Random();

            if (randomLocation == null)
            {
                // If the item is not at any accessible location, try to look in
                // out-of-logic places, too.
                randomLocation = Tracker.World.Locations
                    .Where(x => x.Item.Type == item.InternalItemType)
                    .Where(x => !x.Cleared)
                    .Where(predicate)
                    .Random();
            }

            return randomLocation;
        }

        private GrammarBuilder GetItemSpoilerRule()
        {
            var items = GetItemNames();

            return new GrammarBuilder()
                .Append("Hey tracker, ")
                .OneOf("where is", "where's", "where are")
                .Optional("the", "a", "an")
                .Append(ItemNameKey, items);
        }

        private GrammarBuilder GetLocationSpoilerRule()
        {
            var locations = GetLocationNames();

            var whatsAtRule = new GrammarBuilder()
                .Append("Hey tracker, ")
                .OneOf("what's", "what is")
                .OneOf("at", "in")
                .Optional("the")
                .Append(LocationKey, locations);

            var whatDoesLocationHaveRule = new GrammarBuilder()
                .Append("Hey tracker, ")
                .Append("what does")
                .Optional("the")
                .Append(LocationKey, locations)
                .Append("have")
                .Optional("for me");

            return GrammarBuilder.Combine(whatsAtRule, whatDoesLocationHaveRule);
        }

        private GrammarBuilder GetEnableSpoilersRule()
        {
            return new GrammarBuilder()
                .Append("Hey tracker, ")
                .OneOf("enable", "turn on")
                .Append("spoilers");
        }

        private GrammarBuilder GetDisableSpoilersRule()
        {
            return new GrammarBuilder()
                .Append("Hey tracker, ")
                .OneOf("disable", "turn off")
                .Append("spoilers");
        }

        private GrammarBuilder GetEnableHintsRule()
        {
            return new GrammarBuilder()
                .Append("Hey tracker, ")
                .OneOf("enable", "turn on")
                .Append("hints");
        }

        private GrammarBuilder GetDisableHintsRule()
        {
            return new GrammarBuilder()
                .Append("Hey tracker, ")
                .OneOf("disable", "turn off")
                .Append("hints");
        }
    }
}
