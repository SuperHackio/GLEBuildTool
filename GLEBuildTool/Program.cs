using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace GLEBuildTool
{
    class Program
    {
        const string GLEVERSION = "V2_0_0_0";
        static string WorkingPath => AppDomain.CurrentDomain.BaseDirectory;
        static string SourcePath => Path.Combine(WorkingPath, "Source");
        static string SymbolsPath => Path.Combine(WorkingPath, "Symbols");
        static string ToolsPath => Path.Combine(WorkingPath, "Tools");
        static string ResourcesPath => Path.Combine(WorkingPath, "Resources");
        static string RiivoOutputPath(string region) => OutputPath(region, "Riivolution");
        static string DolphinOutputPath(string region) => OutputPath(region, "Dolphin");
        static string OutputPath(string region, string ver) => Path.Combine(WorkingPath, "Build", $"{region}_{ver}");
        static string GLEFull(string region, string ver) => $"GalaxyLevelEngine_{ver}_{region}";
        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Console.Title = "GLE Build Tool";

            if (args.Length < 1)
            {
                Console.WriteLine("GLEBuildTool.exe <Region>");
                Console.ReadLine();
                return;
            }

            (string Region, string RegionShort) = MakeRegion(args[0]);

            Console.WriteLine($"Targeting: {Region}");

            Thread.Sleep(1000);

            Console.WriteLine("Locating Source Files...");
            string[] files = Directory.GetFiles(SourcePath, "*.s", SearchOption.AllDirectories); //Assembly files
            string[] files2 = Directory.GetFiles(SourcePath, "*.lnk", SearchOption.AllDirectories); //Shortcuts to Assembly files
            List<string> AllFiles = new();
            AllFiles.AddRange(files);
            for (int i = 0; i < files2.Length; i++)
            {
                string Target = GetShortcutTarget(files2[i]);

                if (File.GetAttributes(Target).HasFlag(FileAttributes.Directory))
                {
                    AllFiles.AddRange(Directory.GetFiles(Target, "*.s", SearchOption.AllDirectories));
                }
                else
                    AllFiles.Add(Target);
            }
            if (AllFiles.Count == 0)
            {
                Console.WriteLine("No files found!");
                return;
            }
            //files = AllFiles.OrderBy(Path.GetFileName).ToArray();
            AllFiles.Sort();
            files = AllFiles.ToArray();

            if (args.Any(a => a.Equals("-list")))
            {
                for (int i = 0; i < files.Length; i++)
                {
                    Console.WriteLine(files[i]);
                }
            }

            Console.WriteLine($"Loading Symbols for {Region}...");
            Dictionary<string, uint> Symbols = new();
            string[] SymbolLines = File.ReadAllLines(Path.Combine(SymbolsPath, $"{Region}.txt"));
            for (int i = 0; i < SymbolLines.Length; i++)
            {
                string[] sympart = SymbolLines[i].Split('=');
                Symbols.Add(sympart[0], uint.Parse(sympart[1][2..], System.Globalization.NumberStyles.HexNumber));
            }
            Console.WriteLine($"Loaded {Symbols.Count} Symbols");

            Console.WriteLine("Reading Source Files...");
            SortedDictionary<uint, string> CodeLines = new();
            Dictionary<string, uint> Markers = new();
            Dictionary<string, string> Variables = new();
            List<uint> Trash = new();
            for (int i = 0; i < files.Length; i++)
            {
                int result = CollectLines(ref CodeLines, ref Markers, ref Variables, files[i], Region, Symbols, ref Trash);
                if (result != 0)
                {
                    //An error(?) occured
                    if (result == 1)
                    {
                        //Not an error
                        Console.WriteLine("File {0} Ignored", files[i]);
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
                        Variables[item.Key] = "0x"+item2.Value.ToString("X8");
                    }
                }
            }

            Console.WriteLine("Preparing Source Files for Compilation...");
            //Dictionary sorted. Time to make files out of these!

            List<string> PreparedFiles = new();
            List<int> CodeCounts = new();
            uint CurrentAddress = 0;
            string Current = null;
            List<string> InUseVars = new();
            uint StartAddress = 0;
            foreach (KeyValuePair<uint, string> item in CodeLines)
            {
                if (item.Key != CurrentAddress)
                {
                    if (Current is not null)
                    {
                        PreparedFiles.Add(Current);
                        CodeCounts.Add((int)((CurrentAddress - StartAddress)/4));
                    }

                    CurrentAddress = item.Key;
                    Current = $"#{item.Key:X8}{Environment.NewLine}";
                    StartAddress = CurrentAddress;
                    InUseVars.Clear();
                }

                string fixedcode = item.Value.TrimStart();
                //Here is where we update all branches
                if (Branches.Any(B => fixedcode.StartsWith(B)))
                {
                    string[] splitter = fixedcode.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    if (Variables.ContainsKey(splitter[1]))
                    {
                        splitter[1] = Variables[splitter[1]]; //replace variable name with variable value.
                    }
                    //Try parse a direct vlaue right
                    uint Offset = 0;
                    if (!uint.TryParse(splitter[1][2..], System.Globalization.NumberStyles.HexNumber, null, out uint Result))
                    {
                        //If the number is not...well, a number, we'll need to check the Symbols, then Labels.
                        if (Symbols.ContainsKey(splitter[1]))
                        {
                            Offset = Symbols[splitter[1]] - item.Key; //dest - current
                        }
                        else if (Markers.ContainsKey(splitter[1]))
                        {
                            Offset = Markers[splitter[1]] - item.Key; //dest - current
                        }
                        else
                        {
                            throw new Exception($"Invalid Branch to {splitter[1]}. Perhaps a symbol is missing?");
                        }
                        fixedcode = $"{splitter[0]} 0x{Offset:X8}";
                    }
                    else
                    {
                        if (Result > 0x80000000 && Result < 0xFE000000)
                        {
                            Offset = Result - item.Key; //dest - current
                            fixedcode = $"{splitter[0]} 0x{Offset:X8}";
                        }
                    }
                    //Offset = Result - item.Key; //dest - current
                    //fixedcode = $"{splitter[0]} 0x{Offset:X8}";
                    Next();
                    continue;
                }

                if (fixedcode.StartsWith(".string ") || fixedcode.StartsWith(".wstring "))
                {
                    bool IsWide = fixedcode.StartsWith(".wstring ");
                    string[] split = fixedcode.Split('\"', StringSplitOptions.RemoveEmptyEntries);
                    Encoding enc = IsWide ? Encoding.BigEndianUnicode : Encoding.GetEncoding("Shift-JIS");
                    int stride = IsWide ? 2 : 1;
                    byte[] strdata = enc.GetBytes(split[1]);
                    fixedcode = "";
                    int strcount = strdata.Length;
                    for (int i = 0; i < strdata.Length; i++)
                        fixedcode += $".byte 0x{strdata[i]:X2}" + Environment.NewLine;

                    Add0x00(stride); //Null Terminator is stride length

                    if (split.Length >= 3 && split[2].Equals(" AUTO"))
                    {
                        //strcount += (int)CurrentAddress % 4;
                        while ((CurrentAddress + strcount) % 4U != 0)
                        {
                            Add0x00(1); //Padding is only 0x01 in length.
                        }
                    }
                    Current += fixedcode + Environment.NewLine;
                    CurrentAddress += (uint)strcount;
                    continue;

                    void Add0x00(int count)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            fixedcode += ".byte 0x00" + Environment.NewLine;
                            strcount++;
                        }
                    }
                }

                if (fixedcode.StartsWith(".int "))
                {
                    //Special override for taking the integers of symbols and labels

                    string v = fixedcode[5..];
                    if (Variables.ContainsKey(v))
                        v = Variables[v];

                    if (Markers.ContainsKey(v))
                    {
                        v = "0x"+Markers[v].ToString("X8");
                    }
                    else if (Symbols.ContainsKey(v))
                    {
                        v = "0x" + Symbols[v].ToString("X8");
                    }
                    fixedcode = ".int " + v;
                }

                if (fixedcode.StartsWith("addi "))
                {
                    //Correction for addi's that are way too high

                    string[] splitter = fixedcode.Split(',', StringSplitOptions.TrimEntries);
                    if (!splitter[2].Contains("@") && splitter[2].StartsWith("0x") && uint.TryParse(splitter[2][2..], System.Globalization.NumberStyles.HexNumber, null, out uint addiresult) && (addiresult > 0x7FFF))
                    {
                        addiresult |= 0xFFFF0000;
                        addiresult = (uint)-addiresult;
                        fixedcode = $"{splitter[0].Replace("addi","subi")}, {splitter[1]}, 0x{addiresult:X8}";
                    }
                }

                Next();

                foreach (var var in Variables)
                {
                    if (fixedcode.Contains(var.Key) && !InUseVars.Contains(var.Key))
                    {
                        InUseVars.Add(var.Key);
                        Current += $".set {var.Key}, {var.Value}" + Environment.NewLine;
                    }
                }
                foreach (var var in Markers)
                {
                    if (fixedcode.Contains(var.Key) && !InUseVars.Contains(var.Key))
                    {
                        InUseVars.Add(var.Key);
                        Current += $".set {var.Key}, {var.Value}" + Environment.NewLine;
                    }
                }
                foreach (var var in Symbols)
                {
                    if (fixedcode.Contains(var.Key) && !InUseVars.Contains(var.Key))
                    {
                        InUseVars.Add(var.Key);
                        Current += $".set {var.Key}, {var.Value}" + Environment.NewLine;
                    }
                }

                void Next()
                {
                    Current += fixedcode + Environment.NewLine;
                    CurrentAddress += 0x04;
                }
            }
            PreparedFiles.Add(Current);
            CodeCounts.Add((int)((CurrentAddress - StartAddress) / 4));

            Console.WriteLine("Compiling...");
            List<string> MemoryPatches = new();
            List<string> Dolphin = new();
            Dolphin.Add("[OnFrame]");
            Dolphin.Add("$GalaxyLevelEngine");
            for (int i = 0; i < PreparedFiles.Count; i++)
            {
                string Address = PreparedFiles[i].Substring(1, 8);
                CurrentAddress = uint.Parse(Address, System.Globalization.NumberStyles.HexNumber);
                string path = Path.Combine(WorkingPath, "file.asm");
                string path2 = Path.Combine(WorkingPath, "a.out");
                File.WriteAllText(path, PreparedFiles[i]);
                ExeCommand($"{Path.Combine(ToolsPath, "powerpc-eabi-as.exe")} -mbig -mregnames -mgekko {path}");
                if (!File.Exists(path2))
                {
                    throw new Exception("ERROR");
                }
                //Success! Lets read this....ELF? Who thought this was a good idea??

                string RiivoData = "";
                string IniData = "";
                FileStream FS = new(path2, FileMode.Open);
                FS.Position = 0x34; //Code location...apparently always fixed
                for (int x = 0; x < CodeCounts[i]; x++)
                {
                    byte[] read = new byte[4];
                    FS.Read(read, 0, 4);
                    read = read.Reverse().ToArray();
                    string v = BitConverter.ToUInt32(read).ToString("X8");
                    if (!Trash.Contains(CurrentAddress))
                    {
                        RiivoData += v;
                        IniData += $"0x{CurrentAddress:X8}:dword:0x{v}" + Environment.NewLine;
                    }

                    CurrentAddress += 0x04;
                }
                FS.Close();
                File.Delete(path);
                File.Delete(path2);

                //All code aquired. Lets make a riivo memory patch
                string Mem = $"<memory offset=\"0x{Address.ToUpper()}\" value=\"{RiivoData}\" />";
                MemoryPatches.Add(Mem);
                Dolphin.Add(IniData);
            }

            Dolphin.Add("");
            Dolphin.Add("[OnFrame_Enabled]");
            Dolphin.Add("$GalaxyLevelEngine");

            //we now have fully compiled information!
            Console.WriteLine("Compile succeeded");
            if (Directory.Exists(RiivoOutputPath(Region)))
                Directory.Delete(RiivoOutputPath(Region), true);
            if (Directory.Exists(DolphinOutputPath(Region)))
                Directory.Delete(DolphinOutputPath(Region), true);

            Directory.CreateDirectory(RiivoOutputPath(Region));
            Directory.CreateDirectory(DolphinOutputPath(Region));

            Console.WriteLine("Copying Resources...");
            File.WriteAllLines(Path.Combine(RiivoOutputPath(Region), $"GalaxyLevelEngine_{GLEVERSION}_{Region}.xml"), MemoryPatches.ToArray());
            File.WriteAllLines(Path.Combine(DolphinOutputPath(Region), $"SB3{RegionShort.ToUpper()}01.ini"), Dolphin.ToArray());

            if (args.Any(A => A.Equals("--nores")))
                goto NoResJump;

            DirectoryCopy(Path.Combine(ResourcesPath, "Riivolution"), Path.Combine(RiivoOutputPath(Region), GLEFull(Region, GLEVERSION)), true);
            DirectoryCopy(Path.Combine(ResourcesPath, "Dolphin_Additions"), Path.Combine(DolphinOutputPath(Region), GLEFull(Region, GLEVERSION)), true);
            DirectoryCopy(Path.Combine(ResourcesPath, "Riivolution"), Path.Combine(DolphinOutputPath(Region), GLEFull(Region, GLEVERSION), "files"), true);

            NoResJump:
            Console.WriteLine("Build Finished! Check the Build folder!");
            Thread.Sleep(2000);
        }


        static int CollectLines(ref SortedDictionary<uint, string> CodeLines, ref Dictionary<string, uint> Markers, ref Dictionary<string, string> Variables, string Filepath, string Region, Dictionary<string, uint> Symbols, ref List<uint> TrashAddresses)
        {
            string[] Lines = File.ReadAllLines(Filepath);
            Stack<uint> AddressStack = new();
            uint CurrentAddress = 0;
            bool IsActive = true, IsTrashing = false;
            for (int i = 0; i < Lines.Length; i++)
            {
                //For every line, mark down an address. We're gonna group everything up before passing it to the compiler.
                if (Lines[i].StartsWith(".GLE "))
                {
                    //Special GLE command! Lets split and investigate...

#if DEBUG
                    if (Lines[i].Equals(".GLE DEBUG"))
                    {
                        //Debugger.Break();
                    }
#endif

                    if (Lines[i].Equals(".GLE PRINTADDRESS"))
                    {
                        Console.WriteLine($"GLE: CURRENT ADDRESS - 0x{CurrentAddress:X8} in file {Filepath} on line {i}");
                        continue;
                    }
                    else if (Lines[i].StartsWith(".GLE PRINTMESSAGE "))
                    {
                        Console.WriteLine($"GLE: {Lines[i][18..]}");
                        continue;
                    }

                    string[] split = Lines[i].Split();
                    if (split.Length <= 1)
                    {
                        ThrowException("Invalid .GLE Prompt", Filepath, i);
                    }

                    if (split[1].Equals("IGNORE"))
                        if (i == 0)
                            return 1; //Ignore the rest of this file. Preferably only use at the start of a file to dummy it out
                        else
                            return 2; //Invalid ignore

                    if (!IsActive)
                        goto CheckRegion;
                    if (split[1].Equals("ADDRESS"))
                    {
                        if (split.Length <= 2)
                            ThrowException("Invalid .GLE ADDRESS Prompt", Filepath, i);

                        if (Variables.ContainsKey(split[2]))
                        {
                            split[2] = Variables[split[2]];
                        }

                        if (Symbols.ContainsKey(split[2]))
                        {
                            split[2] = "0x"+Symbols[split[2]].ToString("X8");
                        }
                        else if (Markers.ContainsKey(split[2]))
                        {
                            split[2] = "0x" + Markers[split[2]].ToString("X8");
                        }

                        if (split[2][1] != 'x')
                        {
                            ThrowException($"Invalid Address {split[2]}. (Offset must be in Hexadecimal)", Filepath, i);
                        }

                        string tmp = split[2][2..];
                        if (!uint.TryParse(tmp, System.Globalization.NumberStyles.HexNumber, null, out uint Result))
                        {
                            ThrowException($"Invalid Address {tmp}", Filepath, i);
                        }

                        //Deal with addictional offsets
                        for (int x = 3; x < split.Length; x++)
                        {
                            char op = split[x][0];
                            string off = split[x][3..];
                            if (!uint.TryParse(off, System.Globalization.NumberStyles.HexNumber, null, out uint r2))
                            {
                                ThrowException($"Invalid Number {off}", Filepath, i);
                            }

                            switch (op)
                            {
                                case '+':
                                    Result += r2;
                                    break;
                                case '-':
                                    Result -= r2;
                                    break;
                            }
                        }

                        //If we do not have a current address, we'll need to set the current address.
                        //if we do have a current address, we'll need to push it first.
                        if (CurrentAddress != 0)
                            AddressStack.Push(CurrentAddress);
                        CurrentAddress = Result;
                    }
                    else if (split[1].Equals("ENDADDRESS"))
                    {
                        if (split.Length == 3 && split[2].Equals("x"))
                        {

                        }
                        if (AddressStack.Count == 0)
                            continue;

                        CurrentAddress = AddressStack.Pop();
                    }
                    else if (split[1].Equals("ASSERT"))
                    {
                        if (split.Length <= 2)
                            ThrowException("Invalid .GLE ASSERT Prompt", Filepath, i);
                        if (Variables.ContainsKey(split[2]))
                        {
                            split[2] = Variables[split[2]];
                        }
                        if (Symbols.ContainsKey(split[2]))
                        {
                            split[2] = "0x" + Symbols[split[2]].ToString("X8");
                        }
                        else if (Markers.ContainsKey(split[2]))
                        {
                            split[2] = "0x" + Markers[split[2]].ToString("X8");
                        }

                        string tmp = split[2][2..];
                        if (!uint.TryParse(tmp, System.Globalization.NumberStyles.HexNumber, null, out uint Result))
                        {
                            ThrowException("Invalid Address", Filepath, i);
                        }

                        //Deal with addictional offsets
                        for (int x = 3; x < split.Length; x++)
                        {
                            char op = split[x][0];
                            string off = split[x][3..];
                            if (!uint.TryParse(off, System.Globalization.NumberStyles.HexNumber, null, out uint r2))
                            {
                                ThrowException($"Invalid Number {off}", Filepath, i);
                            }

                            switch (op)
                            {
                                case '+':
                                    Result += r2;
                                    break;
                                case '-':
                                    Result -= r2;
                                    break;
                            }
                        }

                        if (CurrentAddress > Result)
                        {
                            if (CurrentAddress - Result > 1000)
                                ThrowWarning($"Misalignment, code may have gone past an Assertion! (0x{Result:X8}, 0x{(CurrentAddress - Result):X8})", Filepath, i);
                            else
                                ThrowException($"Code compiles beyond the allowed point! (0x{Result:X8}, 0x{(CurrentAddress - Result):X8})", Filepath, i);
                        }
                        else if (CurrentAddress == Result)
                        {
                            ThrowWarning($"GLE ASSERT notes that there's no extra space for code before 0x{Result:X8}. (You're still good to go!)", Filepath, i);
                        }
                    }
                    else if (split[1].Equals("TRASH"))
                    {
                        if (split.Length <= 2)
                            ThrowException("Invalid .GLE TRASH Prompt", Filepath, i);

                        if (split[2].Equals("BEGIN"))
                        {
                            IsTrashing = true;
                        }
                        else if (split[2].Equals("END"))
                        {
                            IsTrashing = false;
                        }
                    }
                    CheckRegion:
                    if (split[1].Equals("REGION"))
                    {
                        if (split.Length <= 2)
                        {
                            ThrowWarning("Invalid .GLE REGION prompt. Disabling region checking until next .GLE REGION", Filepath, i);
                            IsActive = true;
                        }
                        (string Region, string RegionShort) p = MakeRegion(Region);
                        if (split[2].Equals("END"))
                            IsActive = true;
                        else
                            IsActive = split[2].Equals(p.Region) || split[2].Equals(p.RegionShort);
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(Lines[i]) || !IsActive)
                    continue; //Skip empty lines

                if (Lines[i].StartsWith("#"))
                    continue; //Skip Comments

                if (Regex.IsMatch(Lines[i], @"\..*:\s+#.*", RegexOptions.Singleline))
                {
                    //TODO: Either Strip comments form labels, or invalidate them!
                    int x = Lines[i].IndexOf(':')+1;
                    Lines[i] = Lines[i][..x];
                }

                if (Regex.IsMatch(Lines[i], @"^.*:$", RegexOptions.Singleline))
                {
                    //Catch Labels
                    //Mark the label's location, as we'll have to fill out the branches ourselves :weary:
                    Markers.Add(Lines[i][0..^1], CurrentAddress);
                    continue;
                }

                if (Regex.IsMatch(Lines[i], @"\.set\s[a-zA-Z\<\>_0-9]+,\s.*$"))
                {
                    //Catch variables
                    Variables.Add(Lines[i][5..Lines[i].IndexOf(',')], Lines[i][(Lines[i].IndexOf(',') + 2)..]);
                    continue;
                }

                if (CodeLines.ContainsKey(CurrentAddress))
                {
                    ThrowException($"Duplicate Address 0x{CurrentAddress:X8}", Filepath, i);
                }

                //Assign the current line the address, then increment it
                CodeLines.Add(CurrentAddress, Lines[i]);
                if (IsTrashing)
                {
                    TrashAddresses.Add(CurrentAddress);
                }

                if (Lines[i].TrimStart().StartsWith(".string ") || Lines[i].TrimStart().StartsWith(".wstring "))
                {
                    string fixedcode = Lines[i].TrimStart();
                    bool IsWide = fixedcode.StartsWith(".wstring ");
                    string[] split = fixedcode.Split('\"', StringSplitOptions.RemoveEmptyEntries);
                    Encoding enc = IsWide ? Encoding.BigEndianUnicode : Encoding.GetEncoding("Shift-JIS");
                    int stride = IsWide ? 2 : 1;
                    byte[] strdata = enc.GetBytes(split[1]);
                    int strcount = strdata.Length + stride;

                    if (split.Length >= 3 && split[2].Equals(" AUTO"))
                    {
                        while ((CurrentAddress + strcount) % 4U != 0)
                        {
                            strcount++;
                        }
                    }
                    if (IsTrashing)
                        for (int x = 0; x < strcount; x++)
                        {
                            TrashAddresses.Add(CurrentAddress + (uint)x);
                        }
                    CurrentAddress += (uint)strcount;
                    continue;
                }
                else
                    CurrentAddress += 0x04;
            }
            return 0;
        }

        static (string Region, string RegionShort) MakeRegion(string arg)
        {
            return arg.ToUpper() switch
            {
                "NTSC-U" or "USA" => ("NTSC-U", "E"),
                "PAL" or "EUR" => ("PAL", "P"),
                "NTSC-J" or "JPN" => ("NTSC-J", "J"),
                "NTSC-K" or "KOR" => ("NTSC-K", "K"),
                "NTSC-W" or "TAW" => ("NTSC-W", "W"),
                _ => throw new Exception($"Invalid region {arg}"),
            };
        }

        static void ExeCommand(string Command)
        {
            Process process = new();
            ProcessStartInfo startInfo = new();
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.FileName = "cmd.exe";
            startInfo.Arguments = $"/C {Command}";
            process.StartInfo = startInfo;
            process.Start();
            process.WaitForExit();
        }

        static void ThrowWarning(string message, string file, int line)
        {
            Console.WriteLine("========================");
            Console.WriteLine($"WARNING: {message}");
            Console.WriteLine($"in {file}, line {line}.");
            Console.WriteLine("========================");
        }
        static void ThrowException(string message, string file, int line) => throw new Exception($"Build Error: {message} | File: {file} | Line: {line}");

        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();

            // If the destination directory doesn't exist, create it.       
            Directory.CreateDirectory(destDirName);

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string tempPath = Path.Combine(destDirName, file.Name);
                file.CopyTo(tempPath, false);
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string tempPath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, tempPath, copySubDirs);
                }
            }
        }

        static readonly string[] Branches = new string[]
        {
            "b ",
            "bl ",
            "ble ",
            "blt ",
            "beq ",
            "bne ",
            "bge ",
            "bgt ",

            "ble+ ",
            "blt+ ",
            "beq+ ",
            "bne+ ",
            "bge+ ",
            "bgt+ ",

            "ble- ",
            "blt- ",
            "beq- ",
            "bne- ",
            "bge- ",
            "bgt- ",

            "bdnz ",
            "bdnz- ",
            "bdnz+ ",
        };

        private static string GetShortcutTarget(string file)
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
                {                      // Bit 1 set means we have to
                                       // skip the shell item ID list
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

                    string firstPart = link.Substring(0, begin);
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
    }
}
