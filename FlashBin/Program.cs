using FlashBin;
using Mono.Options;

internal class Program
{
    private static bool shouldShowHelp = false;
    private static readonly List<string> fileList = new();
    private static string baseAddressString = "0x10040000";
    private static string maskAddressString = "0x1003F000";

    private static uint GetFlashAddressMask(string filename)
    {
        var fileInfo = new FileInfo(filename);

        var size = fileInfo.Length;
        if (size < 1)
        {
            return 0;
        }

        var maskBits = 0u;

        size -= 1;
        while (size > 0)
        {
            size >>= 1;
            maskBits++;
        }

        var memCapacity = (uint)(1 << (int)maskBits);
        var addressMask = memCapacity - 1;
        return addressMask;
    }

    private static List<Block> GetUF2BlocksFromFile(string binFilename, uint flashRomAddress)
    {
        var blocks = new List<Block>();

        var blockAddress = flashRomAddress;

        using var fs = new FileStream(binFilename, FileMode.Open, FileAccess.Read);
        while (true)
        {
            var currentBlock = new Block(blockAddress);
            var buffer = new byte[currentBlock.BlockSize];
            var bytesRead = fs.Read(buffer, 0, currentBlock.BlockSize);
            if (bytesRead == 0)
            {
                break;
            }

            currentBlock.SetBlockBytes(buffer);
            blocks.Add(currentBlock);
            blockAddress += (uint)currentBlock.BlockSize;
        }
        return blocks;
    }

    private static List<Block> GetUF2RomaddrMaskBlock(uint maskAddress, uint romaddrMask)
    {
        var blocks = new List<Block>();
        var currentBlock = new Block(maskAddress);
        currentBlock.SetBlockBytes(BitConverter.GetBytes(romaddrMask));

        // Copy 16 times a 256 block to fill a 4096 byte sector
        for (var i = 0; i < 16; i++)
        {
            blocks.Add(currentBlock);
        }

        return blocks;
    }

    private static byte[] CombineByteArrays(byte[] first, byte[] second)
    {
        byte[] combined = new byte[first.Length + second.Length];
        Buffer.BlockCopy(first, 0, combined, 0, first.Length);
        Buffer.BlockCopy(second, 0, combined, first.Length, second.Length);
        return combined;
    }

    private static void Process(string inputFile, uint baseAddress, uint maskAddress)
    {
        var directory = Path.GetDirectoryName(inputFile);
        if (directory == null)
        {
            return;
        }
        var outputFile = Path.Combine(directory,  $"{Path.GetFileNameWithoutExtension(inputFile)}.uf2");

        var uf2Blocks = new List<Block>();
        var byteStream = Array.Empty<byte>();

        uint romaddrMask = GetFlashAddressMask(inputFile);
        if (maskAddress != 0xffffffff)
        {
            uf2Blocks.AddRange(GetUF2RomaddrMaskBlock(maskAddress, romaddrMask));
        }

        uf2Blocks.AddRange(GetUF2BlocksFromFile(inputFile, baseAddress));

        int totalBlocks = uf2Blocks.Count;
        int currentBlock = 0;

        foreach (var block in uf2Blocks)
        {
            byteStream = CombineByteArrays(byteStream, block.Encode(currentBlock, totalBlocks));
            currentBlock++;
        }

        Console.WriteLine("Total blocks written: " + totalBlocks);

        File.WriteAllBytes(outputFile, byteStream);
    }

    private static void Main(string[] args)
    {
        var options = new OptionSet {
            { "b|base=", "Sets the base address where the .bin file is going to be flashed, default 0x10040000", b => baseAddressString = b },
            { "m|mask=", "Sets the base address where the flashrom address mask will be flashed, default 0x1003F000", m => maskAddressString = m },
            { "f|file=", "File to add, can be multiple.", f => fileList.Add(f) },
            { "h|help", "show this message and exit", h => shouldShowHelp = h != null },
        };

        try
        {
            List<string> extra = options.Parse(args);

            if (shouldShowHelp || args.Length == 0)
            {
                Console.WriteLine("FlashBin:");
                Console.WriteLine("Generate flashbios .uf2 files from .bin for RP2040 Flash.");
                options.WriteOptionDescriptions(System.Console.Out);
                return;
            }

            uint baseAddress;
            if (baseAddressString.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == false || uint.TryParse(baseAddressString.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out baseAddress) == false)
            {
                throw new OptionException("Base address is invalid", "base");
            }

            uint maskAddress;
            if (maskAddressString.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == false || uint.TryParse(maskAddressString.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out maskAddress) == false)
            {
                throw new OptionException("Mask address is invalid", "mask");
            }

            if (fileList.Count == 0)
            {
                throw new OptionException("No file/files specified", "file");
            }

            foreach (var file in fileList)
            {
                var fullPath = Path.GetFullPath(file);
                if (File.Exists(fullPath) == false)
                {
                    throw new OptionException($"File '{file}' not found", "file");
                }
            }

            foreach (string file in fileList)
            {
                Process(Path.GetFullPath(file), baseAddress, maskAddress);
            }
        }
        catch (OptionException e)
        {
            Console.Write("FlashBin:");
            Console.WriteLine("Generate flashbios .uf2 files from .bin for RP2040 Flash.");
            Console.WriteLine(e.Message);
            Console.WriteLine("Try `FlashBin --help' for more information.");
            return;
        }
    }
}