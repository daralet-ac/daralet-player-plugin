using System;
using System.Collections.Generic;
using System.Numerics;

namespace DaraletPlayer.Map {
    /// <summary>What the dungeon map is asked to draw each frame.</summary>
    internal class DungeonScene {
        public DungeonLayout Layout;
        /// <summary>Where you are in the landblock, in meters; null to draw every floor as yours.</summary>
        public Vector3? Self;
        /// <summary>Degrees clockwise from north.</summary>
        public double Heading;
        /// <summary>Turns the map so the way you face is up.</summary>
        public bool Turn;
        public bool AllFloors;
        /// <summary>The cells you've been in (whole cell ids), drawn grey; null for none.</summary>
        public HashSet<uint> Visited;
        /// <summary>What they're drawn in on your floor: ramps lean towards the ramp colour, lower floors darker.</summary>
        public uint VisitedColor;
        public List<DungeonMarker> Markers;
        public Func<DungeonMarkerKind, bool> ShowMarker;
        /// <summary>The way to the route's target, as points to walk through; null for none.</summary>
        public List<Vector3> Route;
    }
}
