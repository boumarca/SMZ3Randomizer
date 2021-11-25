﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Microsoft.Extensions.Logging;

using Randomizer.Shared;
using Randomizer.SMZ3.Tracking.Configuration;

namespace Randomizer.SMZ3.Tracking.VoiceCommands
{
    /// <summary>
    /// Provides voice commands that reveal about items and locations.
    /// </summary>
    public class SpoilerModule : TrackerModule, IOptionalModule
    {
        private readonly Dictionary<ItemType, int> _itemHintsGiven = new();

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

            AddCommand("Enable hints", GetEnableHintsRule(), (tracker, result) =>
            {
                HintsEnabled = true;
                tracker.Say("Toggled hints on.");
            });
            AddCommand("Disable hints", GetDisableHintsRule(), (tracker, result) =>
            {
                HintsEnabled = false;
                tracker.Say("Toggled hints off.");
            });
            AddCommand("Enable spoilers", GetEnableSpoilersRule(), (tracker, result) =>
            {
                SpoilersEnabled = true;
                tracker.Say("Toggled spoilers on.");
            });
            AddCommand("Disable spoilers", GetDisableSpoilersRule(), (tracker, result) =>
            {
                SpoilersEnabled = false;
                tracker.Say("Toggled spoilers off.");
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
                Tracker.Say(string.Format("You already have every {0}.", item.Name));
                return;
            }
            else if (!item.Multiple && item.TrackingState > 0)
            {
                Tracker.Say(string.Format("You already have {0}.", item.NameWithArticle));
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
                Tracker.Say(string.Format("You've marked {0} at {1} <break strength='weak'/> in {2}", item.NameWithArticle, locationName, regionName));
                return;
            }

            if (HintsEnabled && GiveItemLocationHint(item))
                return;

            if (SpoilersEnabled && GiveItemLocationSpoiler(item))
                return;

            if (!HintsEnabled)
            {
                Tracker.Say("If you want me to give a hint, say 'Hey tracker, enable hints'.");
                return;
            }

            if (!SpoilersEnabled)
            {
                Tracker.Say("If you want me to spoil it, say 'Hey tracker, enable spoilers'.");
                return;
            }

            if (item.Multiple || item.HasStages)
                Tracker.Say(string.Format("I cannot find any more {0}.", item.Plural));
            else
                Tracker.Say(string.Format("I cannot find {0}.", item.NameWithArticle));
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
                Tracker.Say(string.Format("You've marked {1} at {0}.", locationName, markedItem.NameWithArticle));
                return;
            }

            if (!SpoilersEnabled)
            {
                Tracker.Say("Why don't you go find out? Or just say 'Hey tracker, enable spoilers' and I might tell you.");
                return;
            }

            if (location.Item == null || location.Item.Type == ItemType.Nothing)
            {
                Tracker.Say(string.Format("{0} does not have an item. Did you forget to generate a seed first?", locationName));
                return;
            }

            var item = Tracker.Items.FirstOrDefault(x => x.InternalItemType == location.Item.Type);
            if (item != null)
            {
                Tracker.Say(string.Format("{0} has {1}", locationName, item.NameWithArticle));
                return;
            }
            else
            {
                Tracker.Say(string.Format("{0} has {1}, but I don't recognize that item.", locationName, location.Item));
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
                    Tracker.Say(string.Format("There is {0} at {1} <break strength='weak'/> in {2}", item.NameWithArticle, locationName, regionName));
                else
                    Tracker.Say(string.Format("{0} is at {1} <break strength='weak'/> in {2}.", item.NameWithArticle, locationName, regionName));
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
                    Tracker.Say(string.Format("There is {0} at {1} <break strength='weak'/> in {2}, but you cannot get it yet.", item.NameWithArticle, locationName, regionName));
                else
                    Tracker.Say(string.Format("{0} is at {1} <break strength='weak'/> in {2}, but it is out of logic.", item.NameWithArticle, locationName, regionName));
                return true;
            }

            return false;
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
                        if (!itemLocations.Any(x => x.IsAvailable(progression)))
                        {
                            Logger.LogInformation("Giving spoiler for {Item}: not available with current items", item);
                            Tracker.Say(string.Format("You need something else before you can find {0}.", item.NameWithArticle));
                            RememberHintGiven(item);
                            return true;
                        }

                        if (itemLocations.Select(x => x.Region).All(x => x is SMRegion))
                        {
                            Logger.LogInformation("Giving spoiler for {Item}: only in SM", item);
                            Tracker.Say(string.Format("You might find {0} on a strange planet.", item.NameWithArticle));
                            RememberHintGiven(item);
                            return true;
                        }

                        if (itemLocations.Select(x => x.Region).All(x => x is Z3Region))
                        {
                            Logger.LogInformation("Giving spoiler for {Item}: only in ALttP", item);
                            Tracker.Say(string.Format("You might find {0} in a world of light and dark.", item.NameWithArticle));
                            RememberHintGiven(item);
                            return true;
                        }

                        // No vague hints possible for this item. Increase the
                        // counter and let the player try again for a more
                        // specific hint.
                        Logger.LogInformation("No level 0 spoilers for {Item}", item);
                        Tracker.Say("Concentrate and ask again.");
                        RememberHintGiven(item);
                        return true;
                    }

                case 1:
                    {
                        if (!itemLocations.Any(x => x.IsAvailable(progression)))
                        {
                            // TODO: Determine what item(s) are missing from
                            // logic, then give a hint about an item that has
                            // hints
                        }

                        var regionWithoutItem = Tracker.World.Locations
                            .Except(itemLocations)
                            .Select(x => x.Region)
                            .Random();

                        if (regionWithoutItem != null)
                        {
                            Logger.LogInformation("Giving spoiler for {Item}: not in {Area}", item, regionWithoutItem.Area);
                            Tracker.Say(string.Format("You won't find {0} in {1}.", item.NameWithArticle, regionWithoutItem.Area));
                            RememberHintGiven(item);
                            return true;
                        }

                        // Unlikely but possible: the item can be found in every
                        // region.
                        Logger.LogInformation("No level 1 spoilers for {Item}", item);
                        Tracker.Say("Concentrate and ask again.");
                        RememberHintGiven(item);
                        return true;
                    }
                    break;

                // Give a vague hint about the region, or tell the player if
                // it's in an optional dungeon
                case 2:
                    {
                        var randomLocation = GetRandomItemLocationWithFilter(item,
                            l => Tracker.WorldInfo.Region(l.Region).Hints?.Count > 0);

                        if (randomLocation != null)
                        {
                            Logger.LogInformation("Giving spoiler for {Item}: in {Region}", item, randomLocation.Region);
                            var regionHint = Tracker.WorldInfo.Region(randomLocation.Region).Hints;
                            if (regionHint != null && regionHint.Count > 0)
                            {
                                Tracker.Say(regionHint);
                                RememberHintGiven(item);
                                return true;
                            }
                        }
                        else
                        {
                            // No locations that have a region hint available.
                            // Increase hints to prevent getting stuck on hints.
                            Logger.LogInformation("No level 2 spoilers for {Item}", item);
                            Tracker.Say("Reply hazy, try again.");
                            RememberHintGiven(item);
                            return true;
                        }
                    }
                    break;

                // Give a more precise hint on subsequent attemps
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
                                Logger.LogInformation("Giving spoiler for {Item}: in room {Room}", item, randomLocation.Room);
                                var roomHint = Tracker.WorldInfo.Room(randomLocation.Room).Hints;
                                if (roomHint != null && roomHint.Count > 0)
                                {
                                    Tracker.Say(roomHint);
                                    RememberHintGiven(item);
                                    return true;
                                }
                            }

                            var locationHint = Tracker.WorldInfo.Location(randomLocation).Hints;
                            if (locationHint != null && locationHint.Count > 0)
                            {
                                Logger.LogInformation("Giving spoiler for {Item}: at location {Location}", item, randomLocation);
                                Tracker.Say(locationHint);
                                RememberHintGiven(item);
                                return true;
                            }
                        }
                        else
                        {
                            // If there isn't any location with this item that
                            // has a hint, let it fall through so tracker can
                            // tell the player to enable spoilers
                            Logger.LogInformation("No level 3 spoilers for {Item}", item);
                        }
                    }
                    break;
            }

            return false;

            int HintsGiven(ItemData item) => _itemHintsGiven.GetValueOrDefault(item.InternalItemType, 0);
            void RememberHintGiven(ItemData item)
            {
                if (_itemHintsGiven.ContainsKey(item.InternalItemType))
                    _itemHintsGiven[item.InternalItemType]++;
                else
                    _itemHintsGiven[item.InternalItemType] = 1;
            }
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
