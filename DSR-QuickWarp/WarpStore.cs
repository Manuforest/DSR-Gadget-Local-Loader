using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DSR_QuickWarp
{
    [DataContract]
    internal sealed class WarpPoint
    {
        [DataMember(Order = 1)] public string Name { get; set; }
        [DataMember(Order = 2)] public int AreaId { get; set; }
        [DataMember(Order = 3)] public float X { get; set; }
        [DataMember(Order = 4)] public float Y { get; set; }
        [DataMember(Order = 5)] public float Z { get; set; }
        [DataMember(Order = 6)] public float Angle { get; set; }
        [DataMember(Order = 7)] public string SavedAt { get; set; }
        [DataMember(Order = 8, EmitDefaultValue = false)] public int MapGroup { get; set; }

        public override string ToString()
        {
            return string.Format("{0}  [Area {1}]", Name, AreaId);
        }
    }

    [DataContract]
    internal sealed class WarpDatabase
    {
        [DataMember(Order = 1)] public WarpPoint Quick { get; set; }
        [DataMember(Order = 2)] public List<WarpPoint> Points { get; set; }

        internal WarpDatabase()
        {
            Points = new List<WarpPoint>();
        }
    }

    internal sealed class WarpStore
    {
        private readonly string _path;
        internal WarpDatabase Database { get; private set; }

        internal WarpStore()
        {
            _path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "quickwarp.json");
            Database = Load();
            NormalizeMapGroups(Database);
        }

        private WarpDatabase Load()
        {
            if (!File.Exists(_path))
                return new WarpDatabase();

            try
            {
                using (FileStream stream = File.OpenRead(_path))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(WarpDatabase));
                    WarpDatabase result = serializer.ReadObject(stream) as WarpDatabase;
                    if (result == null)
                        return new WarpDatabase();
                    if (result.Points == null)
                        result.Points = new List<WarpPoint>();
                    return result;
                }
            }
            catch
            {
                return new WarpDatabase();
            }
        }

        private static void NormalizeMapGroups(WarpDatabase database)
        {
            if (database == null)
                return;

            NormalizeMapGroup(database.Quick);
            if (database.Points == null)
                database.Points = new List<WarpPoint>();
            foreach (WarpPoint point in database.Points)
                NormalizeMapGroup(point);
        }

        private static void NormalizeMapGroup(WarpPoint point)
        {
            // Backward compatibility with v0.1/v0.2 files that did not store MapGroup.
            if (point != null && point.MapGroup <= 0 && point.AreaId > 0)
                point.MapGroup = point.AreaId / 1000;
        }

        internal void Save()
        {
            string tempPath = _path + ".tmp";
            using (FileStream stream = File.Create(tempPath))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(WarpDatabase));
                serializer.WriteObject(stream, Database);
            }

            if (File.Exists(_path))
                File.Delete(_path);
            File.Move(tempPath, _path);
        }
    }
}
