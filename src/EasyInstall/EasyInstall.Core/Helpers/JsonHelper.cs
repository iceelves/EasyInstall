using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace EasyInstall.Core.Helpers
{
    public static class JsonHelper
    {
        public static string Serialize<T>(T obj)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, obj);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static T Deserialize<T>(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)serializer.ReadObject(ms);
            }
        }

        public static T DeserializeFromFile<T>(string path)
        {
            return Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
        }

        public static void SerializeToFile<T>(T obj, string path)
        {
            File.WriteAllText(path, Serialize(obj), Encoding.UTF8);
        }
    }
}
