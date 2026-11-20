using System;
using UnityEngine;

namespace Salasasa.Venues
{
    [Serializable]
    public sealed class VenueApplicationHelp
    {
        [Serializable]
        public sealed class VenueNotes
        {
            public string rulesTitle;
            public string[] rules;
            public string name, dates, description;
        }
        public VenueNotes @event, @base;
        public string organizer, remark;
        public VenueNotes For(VenueKind kind) => kind == VenueKind.Base ? @base : @event;
        public static VenueApplicationHelp Load()
        {
            var asset = Resources.Load<TextAsset>("VenueApplicationHelp");
            return asset ? JsonUtility.FromJson<VenueApplicationHelp>(asset.text) : null;
        }
    }
}
