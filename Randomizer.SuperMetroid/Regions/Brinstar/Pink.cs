﻿using System.Collections.Generic;
using static Randomizer.SuperMetroid.ItemType;
using static Randomizer.SuperMetroid.Logic;

namespace Randomizer.SuperMetroid.Regions.Brinstar {

    class Pink : Region {

        public override string Name => "Brinstar Pink";
        public override string Area => "Brinstar";

        public Pink(World world, Logic logic) : base(world, logic) {
            Locations = new List<Location> {
                new Location(this, "Super Missile (pink Brinstar)", LocationType.Chozo, 0x784E4, Logic switch {
                    _ => new Requirement(items => items.CanPassBombPassages() && items.Has(Super))
                }),
                new Location(this, "Missile (pink Brinstar top)", LocationType.Visible, 0x78608),
                new Location(this, "Missile (pink Brinstar bottom)", LocationType.Visible, 0x7860E),
                new Location(this, "Charge Beam", LocationType.Chozo, 0x78614, Logic switch {
                    _ => new Requirement(items => items.CanPassBombPassages())
                }),
                new Location(this, "Power Bomb (pink Brinstar)", LocationType.Visible, 0x7865C, Logic switch {
                    Casual => items => items.CanUsePowerBombs() && items.Has(Super) && items.HasEnergyReserves(1),
                    _ => new Requirement(items => items.CanUsePowerBombs() && items.Has(Super))
                }),
                new Location(this, "Missile (green Brinstar pipe)", LocationType.Visible, 0x78676, Logic switch {
                    _ => new Requirement(items => items.Has(Morph) && (items.Has(PowerBomb) || items.Has(Super)))
                }),
                new Location(this, "Energy Tank, Waterway", LocationType.Visible, 0x787FA, Logic switch {
                    _ => new Requirement(items => items.CanUsePowerBombs() && items.CanOpenRedDoors() && items.Has(SpeedBooster) &&
                        (items.HasEnergyReserves(1) || items.Has(Gravity)))
                }),
                new Location(this, "Energy Tank, Brinstar Gate", LocationType.Visible, 0x78824, Logic switch {
                    Casual => items => items.CanUsePowerBombs() && items.Has(Wave) && items.HasEnergyReserves(1),
                    _ => new Requirement(items => items.CanUsePowerBombs() && (items.Has(Wave) || items.Has(Super)))
                }),
            };
        }

        public override bool CanEnter(List<Item> items) {
            return items.CanOpenRedDoors() && (items.CanDestroyBombWalls() || items.Has(SpeedBooster)) || items.CanUsePowerBombs();
        }

    }

}
