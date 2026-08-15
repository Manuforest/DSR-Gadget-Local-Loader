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
