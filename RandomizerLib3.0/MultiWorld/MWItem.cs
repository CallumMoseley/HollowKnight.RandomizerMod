﻿using System;
using Newtonsoft.Json;

namespace RandomizerLib.MultiWorld
{
    [Serializable]
    public class MWItem
    {
        public int PlayerId { get; set; }
        public string Item { get; set; }

        public MWItem()
        {
            PlayerId = -1;
            Item = "";
        }

        public MWItem(int playerId, string item)
        {
            PlayerId = playerId;
            Item = item;
        }

        public MWItem(string idItem)
        {
            (PlayerId, Item) = LogicManager.ExtractPlayerID(idItem);
        }

        public override bool Equals(object obj)
        {
            if (obj.GetType() != GetType()) return false;
            MWItem other = (MWItem) obj;
            return PlayerId == other.PlayerId && Item == other.Item;
        }

        public override int GetHashCode()
        {
            return (PlayerId, Item).GetHashCode();
        }

        // TODO: Maybe this was a mistake... right now player IDs in code are 0 indexed, and in anything user facing are 1 indexed
        // This has caused its fair share of bugs however
        public override string ToString()
        {
            return "MW(" + (PlayerId + 1) + ")_" + Item;
        }

        public static explicit operator MWItem(string s)
        {
            return new MWItem(s);
        }
    }
}
