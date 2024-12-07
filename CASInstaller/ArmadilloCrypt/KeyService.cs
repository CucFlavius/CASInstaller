using System.Globalization;
using Spectre.Console;

namespace CASInstaller;

public class KeyService
{
    private static Dictionary<ulong, byte[]> keys = new Dictionary<ulong, byte[]>();
    public static Salsa20 SalsaInstance { get; } = new Salsa20();

    public static bool HasKey(ulong keyName) => keys.ContainsKey(keyName);

    public static byte[] GetKey(ulong keyName)
    {
        keys.TryGetValue(keyName, out byte[] key);
        return key;
    }

    public static void SetKey(ulong keyName, byte[] key)
    {
        if (keys.TryGetValue(keyName, out var oldKey))
        {
            AnsiConsole.WriteLine(!oldKey.SequenceEqual(key)
                ? $"Duplicate key name {keyName:X16} with different key: old key {oldKey.ToHexString()} new key {key.ToHexString()}"
                : $"Duplicate key name {keyName:X16} with key: {key.ToHexString()}");
        }

        keys[keyName] = key;
    }

    public static void LoadKeys(string keyFile = "TactKey.csv")
    {
        if (File.Exists(keyFile))
        {
            using (StreamReader sr = new StreamReader(keyFile))
            {
                string line;

                while ((line = sr.ReadLine()) != null)
                {
                    string[] tokens = line.Split(';');

                    if (tokens.Length != 2)
                        continue;

                    ulong keyName = ulong.Parse(tokens[0], NumberStyles.HexNumber);
                    string keyStr = tokens[1];

                    if (keyStr.Length != 32)
                        continue;

                    SetKey(keyName, keyStr.FromHexString());
                }
            }
        }
    }
}