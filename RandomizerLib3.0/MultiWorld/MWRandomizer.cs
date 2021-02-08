﻿using System;
using System.Linq;
using System.Collections.Generic;
using static RandomizerLib.PreRandomizer;
using static RandomizerLib.Logging.LogHelper;
using Modding;
using System.Diagnostics;

namespace RandomizerLib.MultiWorld
{
    public class MWRandomizer
    {
        private List<RandoSettings> settings;
        private Random rand;
        private int players;

        private MWItemManager im;
        private List<Dictionary<string, string>> transitionPlacements;

        private List<Dictionary<string, int>> modifiedCosts;
        private List<List<string>> startProgression;
        private List<List<string>> startItems;

        private Dictionary<MWItem, int> shopCosts;

        public MWRandomizer(List<RandoSettings> settings)
        {
            this.settings = settings;
            rand = new Random(settings[0].Seed);
            players = settings.Count;
        }

        public MWRandomizer(RandoSettings settings, int players)
        {
            this.settings = new List<RandoSettings>();
            for (int i = 0; i < players; i++)
            {
                this.settings.Add(settings.Clone());
            }
            rand = new Random(settings.Seed);
            this.players = players;
        }

        public MWRandomizer(RandoSettings settings)
        {
            this.settings = new List<RandoSettings>();
            this.settings.Add(settings);
            rand = new Random(settings.Seed);
            this.players = 1;
        }

        public List<RandoResult> RandomizeMW(List<string> nicknames = null)
        {
            transitionPlacements = new List<Dictionary<string, string>>();

            modifiedCosts = new List<Dictionary<string, int>>();
            startProgression = new List<List<string>>();
            startItems = new List<List<string>>();

            bool randoSuccess = false;
            while (!randoSuccess)
            {
                modifiedCosts.Clear();
                startProgression.Clear();
                startItems.Clear();

                try
                {
                    for (int i = 0; i < players; i++)
                    {
                        modifiedCosts.Add(RandomizeNonShopCosts(rand, settings[i]));
                        (List<string> playerStartItems, List<string> playerStartProgression) = RandomizeStartingItems(rand, settings[i]);

                        startItems.Add(playerStartItems);
                        startProgression.Add(playerStartProgression);

                        string playerStartName = RandomizeStartingLocation(rand, settings[i], playerStartProgression);
                        settings[i].StartName = playerStartName;

                        if (settings[i].RandomizeTransitions)
                        {
                            Log("Starting transition randomization for player " + i);
                            transitionPlacements.Add(TransitionRandomizer.RandomizeTransitions(settings[i], rand, playerStartName, playerStartItems, playerStartProgression).transitionPlacements);
                        }
                        else
                        {
                            transitionPlacements.Add(null);
                        }
                    }
                    MWRandomizeItems();

                    randoSuccess = true;
                }
                catch (RandomizationError) { }
            }

            return PrepareResult(nicknames);
        }

        private void MWRandomizeItems()
        {
            Stopwatch watch = new Stopwatch();
            watch.Start();

            Log("");
            Log("Beginning item randomization...");

            im = new MWItemManager(players, transitionPlacements, rand, settings, startItems, startProgression, modifiedCosts);

            FirstPass();
            SecondPass();
            PlaceDupes();
            CreateShopCosts();

            Log("Item randomization finished in " + watch.Elapsed.TotalSeconds + " seconds.");
        }

        private void FirstPass()
        {
            Log("Beginning first pass of item placement...");

            bool overflow = false;

            {
                im.ResetReachableLocations();
                im.vm.ResetReachableLocations(im);

                for (int i = 0; i < players; i++)
                {
                    foreach (string item in startProgression[i])
                    {
                        im.UpdateReachableLocations(new MWItem(i, item));
                    }
                }
                Log("Finished first update");
            }

            while (true)
            {
                MWItem placeItem;
                MWItem placeLocation;

                switch (im.availableCount)
                {
                    case 0:
                        if (im.anyLocations)
                        {
                            if (im.canGuess)
                            {
                                if (!overflow) Log("Entered overflow state with 0 reachable locations after placing " + im.nonShopItems.Count + " locations");
                                overflow = true;
                                placeItem = im.GuessItem();
                                im.PlaceProgressionToStandby(placeItem);
                                continue;
                            }
                        }
                        return;
                    case 1:
                        placeItem = im.ForceItem();
                        if (placeItem is null)
                        {
                            if (im.canGuess)
                            {
                                if (!overflow) Log("Entered overflow state with 1 reachable location after placing " + im.nonShopItems.Count + " locations");
                                overflow = true;
                                placeItem = im.GuessItem();
                                im.PlaceProgressionToStandby(placeItem);
                                continue;
                            }
                            else placeItem = im.NextItem();
                        }
                        else
                        {
                            im.Delinearize(rand);
                        }
                        placeLocation = im.NextLocation();
                        break;
                    default:
                        placeItem = im.NextItem();
                        placeLocation = im.NextLocation();
                        break;
                }

                //Log($"i: {placeItem}, l: {placeLocation}, o: {overflow}, p: {LogicManager.GetItemDef(placeItem).progression}");

                if (!overflow && !LogicManager.GetItemDef(placeItem.Item).progression)
                {
                    im.PlaceJunkItemToStandby(placeItem, placeLocation);
                }
                else
                {
                    im.PlaceItem(placeItem, placeLocation);
                }
            }
        }

        private void SecondPass()
        {
            Log("Beginning second pass of item placement...");
            im.TransferStandby();

            // We fill the remaining locations and shops with the leftover junk
            while (im.anyItems)
            {
                MWItem placeItem = im.NextItem(checkFlag: false);
                MWItem placeLocation;

                if (im.anyLocations) placeLocation = im.NextLocation(checkLogic: false);
                else placeLocation = new MWItem(rand.Next(players), LogicManager.ShopNames[rand.Next(5)]);

                im.PlaceItemFromStandby(placeItem, placeLocation);
            }

            // try to guarantee no empty shops
            if (im.normalFillShops && im.shopItems.Any(kvp => !kvp.Value.Any()))
            {
                Log("Exited randomizer with empty shop. Attempting repair...");
                Dictionary<MWItem, List<MWItem>> nonprogressionShopItems = im.shopItems.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Where(i => !LogicManager.GetItemDef(i.Item).progression).ToList());
                if (nonprogressionShopItems.Select(kvp => kvp.Value.Count).Aggregate(0, (total, next) => total + next) >= 5)
                {
                    int i = 0;
                    while (im.shopItems.FirstOrDefault(kvp => !kvp.Value.Any()).Key is MWItem emptyShop && nonprogressionShopItems.FirstOrDefault(kvp => kvp.Value.Count > 1).Key is MWItem fullShop)
                    {
                        MWItem item = im.shopItems[fullShop].First();
                        im.shopItems[emptyShop].Add(item);
                        im.shopItems[fullShop].Remove(item);
                        nonprogressionShopItems[emptyShop].Add(item);
                        nonprogressionShopItems[fullShop].Remove(item);
                        i++;
                        if (i > 5)
                        {
                            LogError("Emergency exit from shop repair.");
                            break;
                        }
                    }
                }
                Log("Successfully repaired shops.");
            }

            if (im.anyLocations) LogError("Exited item randomizer with unfilled locations.");
        }

        private void PlaceDupes()
        {
            // Duplicate items should not be placed very early in logic
            int minimumDepth = Math.Min(im.locationOrder.Count / 5, im.locationOrder.Count - 2 * im.duplicatedItems.Count);
            int maximumDepth = im.locationOrder.Count;
            bool ValidIndex(int i)
            {
                MWItem location = im.locationOrder.FirstOrDefault(kvp => kvp.Value == i).Key;
                return location != null && !LogicManager.ShopNames.Contains(location.Item) && !LogicManager.GetItemDef(im.nonShopItems[location].Item).progression;
            }
            List<int> allowedDepths = Enumerable.Range(minimumDepth, maximumDepth).Where(i => ValidIndex(i)).ToList();
            Random rand = new Random(settings[0].Seed + 29);

            foreach (MWItem majorItem in im.duplicatedItems)
            {
                while (allowedDepths.Any())
                {
                    int depth = allowedDepths[rand.Next(allowedDepths.Count)];
                    MWItem location = im.locationOrder.First(kvp => kvp.Value == depth).Key;
                    MWItem swapItem = im.nonShopItems[location];

                    List<MWItem> allShops = new List<MWItem>();
                    for (int i = 0; i < players; i++)
                    {
                        foreach (string shop in LogicManager.ShopNames)
                        {
                            allShops.Add(new MWItem(i, shop));
                        }
                    }
                    MWItem toShop = allShops.OrderBy(shop => im.shopItems[shop].Count).First();

                    im.nonShopItems[location] = new MWItem(majorItem.PlayerId, majorItem.Item + "_(1)");
                    im.shopItems[toShop].Add(swapItem);
                    allowedDepths.Remove(depth);
                    break;
                }
            }
        }
        private void CreateShopCosts()
        {
            shopCosts = new Dictionary<MWItem, int>();
            foreach (KeyValuePair<MWItem, List<MWItem>> kvp in im.shopItems)
            {
                foreach (MWItem item in kvp.Value)
                {
                    int cost = RandomizeShopCost(settings[0].Seed, item);
                    shopCosts[item] = cost;
                }
            }
        }

        private List<RandoResult> PrepareResult(List<string> nicknames)
        {
            List<RandoResult> results = new List<RandoResult>();
            if (nicknames == null) nicknames = new List<string>();

            int randoId = (new Random()).Next();

            Dictionary<MWItem, int> allModifiedCosts = new Dictionary<MWItem, int>();

            int geoSeed = settings[0].Seed;

            for (int i = 0; i < players; i++)
            {
                geoSeed += settings[i].GetSettingsSeed();
                foreach (var kvp in modifiedCosts[i])
                {
                    allModifiedCosts.Add(new MWItem(i, kvp.Key), kvp.Value);
                }
            }

            for (int i = 0; i < players; i++)
            {
                RandoResult result = new RandoResult();
                result.playerId = i;
                result.players = players;
                result.randoId = randoId;
                result.settings = settings[i];
                result.settings.Seed = settings[0].Seed;
                result.settings.GeoSeed = geoSeed;
                result.startItems = startItems[i];
                result.transitionPlacements = transitionPlacements[i];
                result.variableCosts = allModifiedCosts;
                result.itemPlacements = new Dictionary<MWItem, MWItem>();
                result.locationOrder = im.locationOrder;
                result.shopCosts = shopCosts;
                result.nicknames = nicknames;

                // Need to flip L -> I to I -> L since each item is unique but locations (shops in particular) are not
                foreach (var kvp in im.nonShopItems)
                {
                    result.itemPlacements[kvp.Value] = kvp.Key;
                }

                // Add item locations to placements
                foreach (KeyValuePair<MWItem, List<MWItem>> kvp in im.shopItems)
                {
                    foreach (MWItem item in kvp.Value)
                    {
                        result.itemPlacements[item] = kvp.Key;
                    }
                }

                results.Add(result);
            }

            return results;
        }

        public int RandomizeShopCost(int seed, MWItem mwItem)
        {
            string item = mwItem.Item;
            int cost;
            ReqDef def = LogicManager.GetItemDef(item);

            Random rand = new Random(seed + item.GetHashCode()); // make shop item cost independent from prior randomization

            int baseCost = 100;
            int increment = 10;
            int maxCost = 500;

            int priceFactor = 1;
            if (def.geo > 0) priceFactor = 0;
            if (item.StartsWith("Soul_Totem") || item.StartsWith("Lore_Tablet")) priceFactor = 0;
            if (item.StartsWith("Rancid") || item.StartsWith("Mask")) priceFactor = 2;
            if (item.StartsWith("Pale_Ore") || item.StartsWith("Charm_Notch")) priceFactor = 3;
            if (item == "Focus") priceFactor = 10;
            if (item.StartsWith("Godtuner") || item.StartsWith("Collector") || item.StartsWith("World_Sense")) priceFactor = 0;
            cost = baseCost + increment * rand.Next(1 + (maxCost - baseCost) / increment); // random from 100 to 500 inclusive, multiples of 10
            cost *= priceFactor;

            return Math.Max(cost, 1);
        }
        /*private bool ValidateItemRandomization()
        {
            Log("Beginning item placement validation...");

            List<MWItem> unfilledLocations;
            if (im.normalFillShops) unfilledLocations = im.randomizedLocations.Except(im.nonShopItems.Keys).Except(im.shopItems.Keys).ToList();
            else unfilledLocations = im.randomizedLocations.Except(im.nonShopItems.Keys).Where(item => !LogicManager.ShopNames.Contains(item.Item)).ToList();

            if (unfilledLocations.Any())
            {
                Log("Unable to validate!");
                string m = "The following locations were not filled: ";
                foreach (MWItem l in unfilledLocations) m += l + ", ";
                Log(m);
                return false;
            }

            HashSet<(MWItem, MWItem)> LIpairs = new HashSet<(MWItem, MWItem)>(im.nonShopItems.Select(kvp => (kvp.Key, kvp.Value)));
            foreach (var kvp in im.shopItems)
            {
                LIpairs.UnionWith(kvp.Value.Select(i => (kvp.Key, i)));
            }

            var lookup = LIpairs.ToLookup(pair => pair.Item2, pair => pair.Item1).Where(x => x.Count() > 1);
            if (lookup.Any())
            {
                Log("Unable to validate!");
                string m = "The following items were placed multiple times: ";
                foreach (var x in lookup) m += x.Key + ", ";
                Log(m);
                string l = "The following locations were filled by these items: ";
                foreach (var x in lookup) foreach (MWItem k in x) l += k + ", ";
                Log(l);
                return false;
            }

            *//*
            // Potentially useful debug logs
            foreach (string item in im.GetRandomizedItems())
            {
                if (im.nonShopItems.Any(kvp => kvp.Value == item))
                {
                    Log($"Placed {item} at {im.nonShopItems.First(kvp => kvp.Value == item).Key}");
                }
                else if (im.shopItems.Any(kvp => kvp.Value.Contains(item)))
                {
                    Log($"Placed {item} at {im.shopItems.First(kvp => kvp.Value.Contains(item)).Key}");
                }
                else LogError($"Unable to find where {item} was placed.");
            }
            foreach (string location in im.GetRandomizedLocations())
            {
                if (im.nonShopItems.TryGetValue(location, out string item))
                {
                    Log($"Filled {location} with {item}");
                }
                else if (im.shopItems.ContainsKey(location))
                {
                    Log($"Filled {location}");
                }
                else LogError($"{location} was not filled.");
            }
            *//*

            MWProgressionManager pm = new MWProgressionManager(
                players,
                settings,
                RandomizerState.Validating,
                im,
                tm,
                modifiedCosts: modifiedCosts
                );
            pm.Add(startProgression);

            HashSet<MWItem> locations = new HashSet<MWItem>(im.randomizedLocations.Union(im.vm.progressionLocations));
            HashSet<MWItem> transitions = new HashSet<MWItem>();
            HashSet<MWItem> items = im.randomizedItems;
            items.ExceptWith(startItems);

            if (settings.RandomizeTransitions)
            {
                transitions.UnionWith(LogicManager.TransitionNames(settings));
                tm.ResetReachableTransitions();
                tm.UpdateReachableTransitions(pm, startTransition);
            }

            im.vm.ResetReachableLocations(im, false, pm);

            int passes = 0;
            while (locations.Any() || items.Any() || transitions.Any())
            {
                if (settings.RandomizeTransitions) transitions.ExceptWith(tm.reachableTransitions);

                foreach (MWItem location in locations.Where(loc => pm.CanGet(loc)).ToList())
                {
                    locations.Remove(location);

                    if (VanillaManager.progressionLocations.Contains(location))
                    {
                        im.vm.UpdateVanillaLocations(im, location, false, pm);
                        if (settings.RandomizeTransitions && !LogicManager.ShopNames.Contains(location)) tm.UpdateReachableTransitions(pm, location, true);
                        else if (settings.RandomizeTransitions)
                        {
                            foreach (string i in VanillaManager.progressionShopItems[location])
                            {
                                tm.UpdateReachableTransitions(pm, i, true);
                            }
                        }
                    }

                    else if (im.nonShopItems.TryGetValue(location, out MWItem item))
                    {
                        items.Remove(item);

                        if (LogicManager.GetItemDef(item).progression)
                        {
                            pm.Add(item);
                            if (settings.RandomizeTransitions) tm.UpdateReachableTransitions(pm, item, true);
                        }
                    }

                    else if (im.shopItems.TryGetValue(location, out List<MWItem> shopItems))
                    {
                        foreach (string newItem in shopItems)
                        {
                            items.Remove(newItem);
                            if (LogicManager.GetItemDef(newItem).progression)
                            {
                                pm.Add(newItem);
                                if (settings.RandomizeTransitions) tm.UpdateReachableTransitions(pm, newItem, true);
                            }
                        }
                    }

                    else
                    {
                        Log("Unable to validate!");
                        Log($"Location {location} did not correspond to any known placement.");
                        return false;
                    }
                }

                passes++;
                if (passes > 400)
                {
                    Log("Unable to validate!");
                    Log("Progression: " + pm.ListObtainedProgression() + Environment.NewLine + "Grubs: " + pm.obtained[LogicManager.grubIndex] + Environment.NewLine + "Essence: " + pm.obtained[LogicManager.essenceIndex]);
                    string m = string.Empty;
                    foreach (string s in items) m += s + ", ";
                    Log("Unable to get items: " + m);
                    m = string.Empty;
                    foreach (string s in locations) m += s + ", ";
                    Log("Unable to get locations: " + m);
                    m = string.Empty;
                    foreach (string s in transitions) m += s + ",";
                    Log("Unable to get transitions: " + m);
                    return false;
                }
            }
            //LogItemPlacements(pm);
            Log("Validation successful.");
            return true;
        }*/
    }
}
