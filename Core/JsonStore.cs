using System;
using System.IO;
using Newtonsoft.Json;

namespace PostApo.Core
{
    public sealed class JsonStore<T> where T : class, new()
    {
        private static readonly JsonSerializerSettings ReadSettings = new JsonSerializerSettings
        {
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        private readonly string _path;
        private readonly string _label;
        private readonly object _gate = new object();

        public JsonStore(string directory, string fileName)
        {
            _label = fileName;
            _path = Path.Combine(directory ?? string.Empty, fileName);
        }

        public T Load()
        {
            lock (_gate)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    if (!File.Exists(_path))
                    {
                        var fresh = new T();
                        WriteUnsafe(fresh);
                        Utils.Log("fichier cree : " + _label);
                        return fresh;
                    }

                    var raw = File.ReadAllText(_path);
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        var fresh = new T();
                        WriteUnsafe(fresh);
                        return fresh;
                    }

                    var parsed = JsonConvert.DeserializeObject<T>(raw, ReadSettings);
                    if (parsed == null)
                    {
                        throw new JsonException("deserialisation nulle");
                    }

                    return parsed;
                }
                catch (Exception ex)
                {
                    Utils.Error("lecture de " + _label + " impossible (" + ex.Message + ") : le fichier est mis de cote et remplace par des valeurs par defaut.");
                    QuarantineUnsafe();

                    var fallback = new T();
                    try { WriteUnsafe(fallback); } catch { }
                    return fallback;
                }
            }
        }

        public bool Save(T value)
        {
            if (value == null) { return false; }

            lock (_gate)
            {
                try
                {
                    WriteUnsafe(value);
                    return true;
                }
                catch (Exception ex)
                {
                    Utils.Error("ecriture de " + _label + " impossible : " + ex.Message);
                    return false;
                }
            }
        }

        private void WriteUnsafe(T value)
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonConvert.SerializeObject(value, Formatting.Indented);
            var tmp = _path + ".tmp";

            File.WriteAllText(tmp, json);

            if (File.Exists(_path))
            {
                File.Replace(tmp, _path, null);
            }
            else
            {
                File.Move(tmp, _path);
            }
        }

        private void QuarantineUnsafe()
        {
            try
            {
                if (!File.Exists(_path)) { return; }
                var target = _path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                File.Move(_path, target);
                Utils.Warn("ancien fichier conserve sous " + Path.GetFileName(target));
            }
            catch { }
        }
    }
}
