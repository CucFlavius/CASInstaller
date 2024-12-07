namespace CASInstaller;

public class IDX
{
    public class IndexEntry
    {
        public int Index;
        public int Offset;
        public int Size;
    }
    
    public Dictionary<Hash, IndexEntry> LocalIndexData = new Dictionary<Hash, IndexEntry>();
    
    public IDX(string path)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var br = new BinaryReader(fs))
        {
            int HeaderHashSize = br.ReadInt32();
            int HeaderHash = br.ReadInt32();
            byte[] h2 = br.ReadBytes(HeaderHashSize);

            long padPos = (8 + HeaderHashSize + 0x0F) & 0xFFFFFFF0;
            fs.Position = padPos;

            int EntriesSize = br.ReadInt32();
            int EntriesHash = br.ReadInt32();

            int numBlocks = EntriesSize / 18;

            for (int i = 0; i < numBlocks; i++)
            {
                IndexEntry info = new IndexEntry();
                byte[] keyBytes = br.ReadBytes(9);
                Array.Resize(ref keyBytes, 16);

                Hash key = new Hash(keyBytes);

                byte indexHigh = br.ReadByte();
                int indexLow = br.ReadInt32BE();

                info.Index = (indexHigh << 2 | (byte)((indexLow & 0xC0000000) >> 30));
                info.Offset = (indexLow & 0x3FFFFFFF);

                info.Size = br.ReadInt32();

                if (!LocalIndexData.ContainsKey(key)) // use first key
                    LocalIndexData.Add(key, info);
            }

            padPos = (EntriesSize + 0x0FFF) & 0xFFFFF000;
            fs.Position = padPos;

            //if (fs.Position != fs.Length)
            //    throw new Exception("idx file under read");
        }
    }
}