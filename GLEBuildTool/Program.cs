using System.Text;

namespace GLEBuildTool
{
    internal class Program
    {
        const string GLEVERSION = "V3_0_0_0";
        const string TITLE = "GLE Build Tool";

        public static string SourcePath => Path.Combine(WorkingPath, "Source");
        public static string SymbolsPath => Path.Combine(WorkingPath, "Symbols");
        public static string ToolsPath => Path.Combine(WorkingPath, "Tools");
        public static string ResourcesPath => Path.Combine(WorkingPath, "Resources");
        public static string CompilePath => Path.Combine(WorkingPath, "_tmp");


        public static string WorkingPath => AppDomain.CurrentDomain.BaseDirectory;
        public static string RiivoOutputPath(string region) => OutputPath(region, "Riivolution");
        public static string DolphinOutputPath(string region) => OutputPath(region, "Dolphin");
        public static string OutputPath(string region, string ver) => Path.Combine(WorkingPath, "Build", $"{region}_{ver}");
        public static string GLEFull(string region, string ver) => $"GalaxyLevelEngine_{ver}_{region}";

        public static string GetShortcutTarget(string file)
        {
            try
            {
                if (Path.GetExtension(file).ToLower() != ".lnk")
                {
                    throw new Exception("Supplied file must be a .LNK file");
                }

                FileStream fileStream = File.Open(file, FileMode.Open, FileAccess.Read);
                using BinaryReader fileReader = new(fileStream);
                fileStream.Seek(0x14, SeekOrigin.Begin);     // Seek to flags
                uint flags = fileReader.ReadUInt32();        // Read flags
                if ((flags & 1) == 1)
                {                      // Bit 1 set means we have to skip the shell item ID list
                    fileStream.Seek(0x4c, SeekOrigin.Begin); // Seek to the end of the header
                    uint offset = fileReader.ReadUInt16();   // Read the length of the Shell item ID list
                    fileStream.Seek(offset, SeekOrigin.Current); // Seek past it (to the file locator info)
                }

                long fileInfoStartsAt = fileStream.Position; // Store the offset where the file info
                                                             // structure begins
                uint totalStructLength = fileReader.ReadUInt32(); // read the length of the whole struct
                fileStream.Seek(0xc, SeekOrigin.Current); // seek to offset to base pathname
                uint fileOffset = fileReader.ReadUInt32(); // read offset to base pathname
                                                           // the offset is from the beginning of the file info struct (fileInfoStartsAt)
                fileStream.Seek((fileInfoStartsAt + fileOffset), SeekOrigin.Begin); // Seek to beginning of
                                                                                    // base pathname (target)
                long pathLength = (totalStructLength + fileInfoStartsAt) - fileStream.Position - 2; // read
                                                                                                    // the base pathname. I don't need the 2 terminating nulls.
                char[] linkTarget = fileReader.ReadChars((int)pathLength); // should be unicode safe
                var link = new string(linkTarget);

                int begin = link.IndexOf("\0\0");
                if (begin > -1)
                {
                    int end = link.IndexOf("\\\\", begin + 2) + 2;
                    end = link.IndexOf('\0', end) + 1;

                    string firstPart = link[..begin];
                    string secondPart = link[end..];

                    return firstPart + secondPart;
                }
                return link;
            }
            catch
            {
                return "";
            }
        }

        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Console.Title = TITLE;

            if (args.Length < 1)
            {
                Console.WriteLine("GLEBuildTool.exe <Region>");
                Console.ReadLine();
                return;
            }

            (string Region, string RegionShort) = Utility.MakeRegion(args[0]);

            Console.WriteLine($"Targeting: {Region}");

            Thread.Sleep(500);

            List<string> AllFiles = new();
            Utility.GetAllFiles(ref AllFiles, SourcePath, args.Any(a => a.Equals("-list")));

            Dictionary<string, uint> Symbols = new();
            Utility.GetSymbols(ref Symbols, SymbolsPath, Region);

            Console.WriteLine("Reading Source Files...");
            SortedDictionary<uint, string> CodeLines = new();
            Dictionary<string, uint> Markers = new();
            Dictionary<string, string> Variables = new();
            List<uint> Trash = new();
            List<string> Bindings = new();
            Dictionary<uint, string> DuplicateAddressTracker = new();
            for (int i = 0; i < AllFiles.Count; i++)
            {
                int result = Utility.CollectLines(ref CodeLines, ref Markers, ref Variables, ref Trash, ref Bindings, AllFiles[i], Region, Symbols, ref DuplicateAddressTracker);
                if (result != 0)
                {
                    //An error(?) occured
                    if (result == 1)
                    {
                        //Not an error
                        Console.WriteLine("File {0} Ignored", AllFiles[i]);
                        continue;
                    }
                    else if (result == 2)
                    {
                        //An error
                        Console.WriteLine("Invalid Ignore - \".GLE IGNORE\" must be the first line in the file!");
                        Console.WriteLine("Press enter to exit");
                        Console.ReadLine();
                        return;
                    }
                }
            }

            foreach (var item in Variables)
            {
                foreach (var item2 in Markers)
                {
                    if (item.Value.Equals(item2.Key))
                    {
                        Variables[item.Key] = "0x" + item2.Value.ToString("X8");
                    }
                }
            }

            Console.WriteLine("Preparing Source Files for Compilation...");
            //Dictionary sorted. Time to make files out of these!


            List<string> PreparedFiles = new();
            List<int> CodeCounts = new();
            uint CurrentAddress = 0;
            Utility.PrepareAssembly(ref PreparedFiles, ref CodeCounts, ref CurrentAddress, CodeLines, Markers, Variables, Symbols);

            Console.WriteLine("Compiling...");
            List<string> MemoryPatches = new();
            List<string> Dolphin = new()
            {
                "[OnFrame]",
                "$GalaxyLevelEngine"
            };

            List<Task<(List<string> MemoryPatches, List<string> Dolphin)>> BuildTasks = new();

            //Create the folder to compile in
            string CompileFolder = Path.Combine(CompilePath, Region);
            if (Directory.Exists(CompileFolder))
                Directory.Delete(CompileFolder, true);

            Directory.CreateDirectory(CompileFolder);

            for (int i = 0; i < PreparedFiles.Count; i++)
            {
                string path = Path.Combine(CompileFolder, $"{i}.asm");
                string path2 = Path.Combine(CompileFolder, $"{i}.out");
                int ii = i;
                BuildTasks.Add(Task.Run(() => Utility.CompileAssembly(PreparedFiles, ii, path, path2, CodeCounts, Trash)));
            }

            bool IsAllDone;
            int CompleteCount;
            do
            {
                IsAllDone = true;
                CompleteCount = 0;
                for (int i = 0; i < BuildTasks.Count; i++)
                {
                    if (!BuildTasks[i].IsCompleted)
                        IsAllDone = false;
                    else
                        CompleteCount++;
                }
                Console.Title = $"{TITLE} ({CompleteCount}/{PreparedFiles.Count})";
            }
            while (!IsAllDone);

            for (int i = 0; i < BuildTasks.Count; i++)
            {
                Dolphin.AddRange(BuildTasks[i].Result.Dolphin);
                MemoryPatches.AddRange(BuildTasks[i].Result.MemoryPatches);
            }

            Dolphin.Add("");
            Dolphin.Add("[OnFrame_Enabled]");
            Dolphin.Add("$GalaxyLevelEngine");

            //we now have fully compiled information!
            Console.WriteLine("Compile succeeded");
            Console.Title = TITLE;

            //Additional Resources are optional
            if (Directory.Exists(RiivoOutputPath(Region)))
                Directory.Delete(RiivoOutputPath(Region), true);
            if (Directory.Exists(DolphinOutputPath(Region)))
                Directory.Delete(DolphinOutputPath(Region), true);

            Directory.CreateDirectory(RiivoOutputPath(Region));
            Directory.CreateDirectory(DolphinOutputPath(Region));

            Console.WriteLine("Saving Generated Patches...");
            File.WriteAllLines(Path.Combine(RiivoOutputPath(Region), $"GalaxyLevelEngine_{GLEVERSION}_{Region}.xml"), MemoryPatches.ToArray());
            File.WriteAllLines(Path.Combine(DolphinOutputPath(Region), $"SB3{RegionShort.ToUpper()}01.ini"), Dolphin.ToArray());

            if (!Directory.Exists(ResourcesPath))
            {
                Console.WriteLine("No Additional resource files found.");
                goto NoResJump;
            }

            Console.WriteLine("Copying Additional Resources...");
            Utility.DirectoryCopy(Path.Combine(ResourcesPath, "Riivolution"), Path.Combine(RiivoOutputPath(Region), GLEFull(Region, GLEVERSION)), true);
            Utility.DirectoryCopy(Path.Combine(ResourcesPath, "Dolphin_Additions"), Path.Combine(DolphinOutputPath(Region), GLEFull(Region, GLEVERSION)), true);
            Utility.DirectoryCopy(Path.Combine(ResourcesPath, "Riivolution"), Path.Combine(DolphinOutputPath(Region), GLEFull(Region, GLEVERSION), "files"), true);

        NoResJump:
            Console.WriteLine("Build Finished! Check the Build folder!");
            Thread.Sleep(2000);
        }
    }
}