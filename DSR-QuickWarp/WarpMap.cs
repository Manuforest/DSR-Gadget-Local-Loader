namespace DSR_QuickWarp
{
    internal static class WarpMap
    {
        internal static int GetGroup(int areaId, int mpAreaId)
        {
            if (areaId > 0)
                return areaId / 1000;
            if (mpAreaId > 0)
                return mpAreaId / 1000;
            return 0;
        }

        internal static int GetGroup(WarpPoint point)
        {
            if (point == null)
                return 0;
            if (point.MapGroup > 0)
                return point.MapGroup;
            return point.AreaId > 0 ? point.AreaId / 1000 : 0;
        }

        internal static bool TryGetAnchorBonfire(int mapGroup, out int bonfireId)
        {
            // Safe loading anchors selected from DSR Gadget's Bonfires.txt.
            // After the game finishes loading this map, QuickWarp performs the
            // precise coordinate warp, so the anchor itself is never the final destination.
            switch (mapGroup)
            {
                case 100: bonfireId = 1002960; return true; // Depths
                case 101: bonfireId = 1012962; return true; // Undead Burg / Parish
                case 102: bonfireId = 1022960; return true; // Firelink Shrine
                case 110: bonfireId = 1102960; return true; // Painted World
                case 120: bonfireId = 1202961; return true; // Darkroot
                case 121: bonfireId = 1212961; return true; // Oolacile
                case 130: bonfireId = 1302960; return true; // Catacombs
                case 131: bonfireId = 1312960; return true; // Tomb of the Giants
                case 132: bonfireId = 1322960; return true; // Ash Lake / Great Hollow
                case 140: bonfireId = 1402961; return true; // Blighttown
                case 141: bonfireId = 1412961; return true; // Demon Ruins / Lost Izalith
                case 150: bonfireId = 1502961; return true; // Sen's Fortress
                case 151: bonfireId = 1512960; return true; // Anor Londo
                case 160: bonfireId = 1602951; return true; // New Londo Ruins
                case 170: bonfireId = 1702960; return true; // Duke's Archives / Crystal Cave
                case 180: bonfireId = 1802961; return true; // Kiln of the First Flame
                case 181: bonfireId = 1812960; return true; // Northern Undead Asylum
                default:
                    bonfireId = -1;
                    return false;
            }
        }
    }
}
