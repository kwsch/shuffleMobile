using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using Ionic.Zip;

namespace shuffleMobile
{
    public static class Util
    {
        internal static bool IsHex(string str)
        {
            return str.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
        }
    }

    public partial class Form1 : Form
    {
        private byte[] strong = { 0xD2, 0x06, 0x6F, 0xC6, 0x70, 0xD2, 0xB3, 0xA8, 0x9C, 0x0B, 0x5B, 0xE3, 0x49, 0xF6, 0xA4, 0xDE };

        private void XOR(byte[] data)
        {
            for (int i = 0; i < data.Length - (data.Length % 0x10); i++)
                data[i] ^= strong[i%0x10];
        }
        public Form1()
        {
            InitializeComponent();
            string[] files = Directory.GetFiles("Deploy\\assets");

            if (Directory.Exists("pvr"))
                Directory.Delete("pvr", true);
            Directory.CreateDirectory("pvr");
            if (Directory.Exists("pkm2"))
                Directory.Delete("pkm2", true);
            Directory.CreateDirectory("pkm2");
            foreach (string t in files)
                dump(t);
        }

        private void renameTGA()
        {
            string[] filesTGA =
                Directory.GetFiles(@"C:\Users\Kurt\Documents\Visual Studio 2012\Projects\shuffleMobile\bin\Debug\tga");

            foreach (string f in filesTGA)
            {
                string parent = new FileInfo(f).Directory.FullName;
                string file = Path.GetFileName(f).Replace("(", "").Replace(")", "").Replace(" ", "_");
                File.Move(f, Path.Combine(parent, file));
            }
        }

        internal static string pvrConvert = @"FOR %%c in (*.pvr) do PVRTexToolCLI.exe -i %%c -d -f r8g8b8a8";
        internal static string pkmConvert = @"FOR %%c in (*.tga) do etc1tool.exe %%c --decode";
        private void dump(string filename)
        {

            if (Directory.Exists(filename + "_d"))
                Directory.Delete(filename + "_d", true);

            Directory.CreateDirectory(filename + "_d");

            ShuffleARC_M Archive = new ShuffleARC_M(filename);
            using (var fs = File.OpenRead(filename))
            {
                for (int i = 0; i < Archive.Files.Count(); i++)
                {
                    byte[] ZipBuffer = new byte[Archive.Files[i].Length];
                    fs.Seek(Archive.Files[i].Offset, SeekOrigin.Begin);
                    fs.Read(ZipBuffer, 0, ZipBuffer.Length);

                    string ZipName = Archive.Files[i].NameHash.ToString("X8") + ".zip";
                    string outlet = Path.Combine(filename + "_d", ZipName);
                    File.WriteAllBytes(outlet, ZipBuffer);

                    string file = "";

                    using (ZipFile Zip = new ZipFile(outlet))
                    {
                        if (Zip.Count() == 1)
                        {
                            string FileName = Zip[0].FileName.Trim('\0');
                            if (FileName == "") Zip[0].FileName = file = i.ToString("0000");
                            Zip.Save();
                        }
                        Zip.ExtractAll(filename + "_d");
                    }
                    // Decrypt File
                    File.Delete(outlet);

                    file = Path.Combine(filename + "_d", file);
                    if ((Archive.Files[i].F2 & 0x200) > 0)
                    {
                        byte[] data = File.ReadAllBytes(file);
                        XOR(data);
                        File.WriteAllBytes(file, data);
                    }

                    // Check for PVR
                    try
                    {
                        byte[] z = File.ReadAllBytes(file);
                        int off = BitConverter.ToInt32(z, 0x4);
                        int val = (BitConverter.ToInt32(z, 0x2C + off) & 0xFFFFFF);
                        if (val == 0x525650)
                        {
                            File.WriteAllBytes(Path.Combine("pvr", Archive.Files[i].NameHash + "_" + i.ToString("0000") + ".pvr"), z.Skip(off).ToArray());
                            File.Delete(file);
                            continue;
                        }
                    } catch {}
                    // Check for GHVK pack
                    try
                    {
                        byte[] z = File.ReadAllBytes(file);
                        if ((BitConverter.ToUInt32(z, 0)) == 0x4B564847)
                        {
                            int count = BitConverter.ToInt16(z, 8);
                            // int o1 = BitConverter.ToInt32(z, 0x40);
                            int o2 = BitConverter.ToInt32(z, 0x44);
                            // int o3 = BitConverter.ToInt32(z, 0x48);
                            int o4 = BitConverter.ToInt32(z, 0x4C);
                            int o5 = BitConverter.ToInt32(z, 0x50);

                            string[] filenames = new string[count];
                            byte[][] files = new byte[count][];

                            for (int f = 0; f < count; f++)
                            {
                                int startstr = BitConverter.ToInt32(z, o4 + f*4);
                                int offset = BitConverter.ToInt32(z, o5 + f*4);
                                int length = BitConverter.ToInt32(z, o2 + f*4);
                                filenames[f] = Encoding.ASCII.GetString(z.Skip(startstr).TakeWhile(b => !b.Equals(0)).ToArray());
                                files[f] = z.Skip(offset).Take(length).ToArray();

                                string outFile = Path.Combine("pkm2", Archive.Files[i].NameHash.ToString("X8"), filenames[f]);
                                new FileInfo(outFile).Directory.Create();

                                File.WriteAllBytes(outFile, files[f]);
                            }
                            
                            File.Delete(file);
                            continue;
                        }

                    } 
                    catch {}
                }
            }
        }
    }

    public class ShuffleARC_M
    {
        public uint FileType;
        public uint Magic; //0xB
        public uint Unknown;
        public uint Unknown2;
        public uint FileCount;
        public uint Padding;
        public ShuffleFile[] Files;
        public string FileName;
        public string FilePath;
        public string Extension;
        public bool IsExtdata;
        public bool Valid;

        public ShuffleARC_M(string path)
        {
            if (!File.Exists(path))
            {
                Valid = false;
                return;
            }
            FileName = Path.GetFileNameWithoutExtension(path);
            if (FileName.Length != 8 || !Util.IsHex(FileName))
            {
                Valid = false;
                return;
            }
            FilePath = Path.GetDirectoryName(path);
            Extension = Path.GetExtension(path);
            using (BinaryReader br = new BinaryReader(File.OpenRead(path)))
            {
                if ((FileType = br.ReadUInt32()) != 0xC)
                {
                    Valid = false;
                    return;
                }
                Magic = br.ReadUInt32();
                if (Magic != uint.Parse(FileName, NumberStyles.AllowHexSpecifier))
                {
                    Valid = false;
                    return;
                }
                Valid = true; // Error Checking done.
                Unknown = br.ReadUInt32();
                Unknown2 = br.ReadUInt32();
                FileCount = br.ReadUInt32();
                Padding = br.ReadUInt32();
                Files = new ShuffleFile[FileCount];
                for (int i = 0; i < FileCount; i++)
                {
                    Files[i] = new ShuffleFile
                    {
                        NameHash = br.ReadUInt32(),
                        _B = br.ReadUInt32(),
                        Length = br.ReadUInt32(),
                        Offset = br.ReadUInt32(),
                        F0 = br.ReadUInt32(),
                        F1 = br.ReadUInt32(),
                        F2 = br.ReadUInt32(),
                        F3 = br.ReadUInt32(),
                    };
                }
            }
        }
    }
    public struct ShuffleFile
    {
        public uint NameHash;
        public uint _B;
        public uint Length;
        public uint Offset;
        public uint F0;
        public uint F1;
        public uint F2;
        public uint F3;
    }
}
