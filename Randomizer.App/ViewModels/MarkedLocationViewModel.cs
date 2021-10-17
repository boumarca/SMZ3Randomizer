﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Randomizer.SMZ3;
using Randomizer.SMZ3.Tracking;

namespace Randomizer.App.ViewModels
{
    public class MarkedLocationViewModel
    {
        private readonly Location _location;
        private readonly ItemData _itemData;
        private readonly Progression _progression;

        public MarkedLocationViewModel(Location location, ItemData itemData, Progression progression)
        {
            _location = location;
            _itemData = itemData;
            _progression = progression;
            var fileName = TrackerWindow.GetItemSpriteFileName(itemData);
            ItemSprite = new BitmapImage(new Uri(fileName));
        }

        public ImageSource ItemSprite { get; }

        public double Opacity => _location.IsAvailable(_progression) ? 1.0 : 0.33;

        public string Item => _itemData.Name;

        public string Location => _location.Name;

        public string Area => _location.Room?.ToString() ?? _location.Region.ToString();
    }
}
