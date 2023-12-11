using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace GLEBuildTool
{
    public static partial class Utility
    {
        public const uint UNRESOLVED = 0xFFFFFFFF;

        public static readonly string[] Branches =
        [
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
        ];

        //Unfinished feature
        public const string GLE_H =
@"//C Bindings for the Galaxy Level Engine.
//Intended for use with Syati. https://github.com/SunakazeKun/Syati

#ifndef __GLE_H
#define __GLE_H

#include ""syati.h""

namespace GLE
{
{0}
}

#endif /* __GLE_H */";

        public static void ThrowWarning(string message, string file, int line)
        {
            Console.WriteLine("========================");
            Console.WriteLine($"WARNING: {message}");
            Console.WriteLine($"in {file}, line {line}.");
            Console.WriteLine("========================");
        }
        public static void ThrowWarningASM(string message)
        {
            Console.WriteLine($"ASM WARNING: {message}");
        }
        public static void ThrowException(string message, string file, int line) => throw new Exception($"Build Error: {message} | File: {file} | Line: {line}");

        public static void ExeCommand(string Command)
        {
            Process process = new();
            ProcessStartInfo startInfo = new()
            {
                WindowStyle = ProcessWindowStyle.Hidden,
                FileName = "cmd.exe",
                Arguments = $"/C {Command}"
            };
            process.StartInfo = startInfo;
            process.Start();
            process.WaitForExit();
        }

        public static (string Region, string RegionShort) MakeRegion(string arg) => arg.ToUpper() switch
        {
            "NTSC-U" or "USA" => ("NTSC-U", "E"),
            "PAL" or "EUR" => ("PAL", "P"),
            "NTSC-J" or "JPN" => ("NTSC-J", "J"),
            "NTSC-K" or "KOR" => ("NTSC-K", "K"),
            "NTSC-W" or "TAW" => ("NTSC-W", "W"),
            _ => throw new Exception($"Invalid region {arg}"),
        };

        public static void GetAllFiles(ref List<string> AllFiles, string SourcePath, bool ShowList)
        {
            Console.WriteLine("Locating Source Files...");

            string[] files = Directory.GetFiles(SourcePath, "*.s", SearchOption.AllDirectories); //Assembly files
            string[] files2 = Directory.GetFiles(SourcePath, "*.lnk", SearchOption.AllDirectories); //Shortcuts to Assembly files
            AllFiles.AddRange(files);
            for (int i = 0; i < files2.Length; i++)
            {
                string Target = Program.GetShortcutTarget(files2[i]);

                if (File.GetAttributes(Target).HasFlag(FileAttributes.Directory))
                    AllFiles.AddRange(Directory.GetFiles(Target, "*.s", SearchOption.AllDirectories));
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
            if (ShowList)
            {
                files = [.. AllFiles];
                for (int i = 0; i < files.Length; i++)
                    Console.WriteLine(files[i]);
            }
        }

        public static void GetSymbols(ref Dictionary<string, uint> Symbols, string SymbolsPath, string Region)
        {
            Console.WriteLine($"Loading Symbols for {Region}...");
            string pth = Path.Combine(SymbolsPath, $"{Region}.txt");
            if (!File.Exists(pth))
            {
                string lnk = pth + ".lnk";
                if (File.Exists(lnk))
                    pth = Program.GetShortcutTarget(lnk);
            }
            string[] SymbolLines = File.ReadAllLines(pth);
            for (int i = 0; i < SymbolLines.Length; i++)
            {
                string[] sympart = SymbolLines[i].Split('=');
                Symbols.Add(sympart[0], uint.Parse(sympart[1][2..], System.Globalization.NumberStyles.HexNumber));
            }
            Console.WriteLine($"Loaded {Symbols.Count} Symbols");
        }
    
        public static int CollectLines(
            ref SortedDictionary<uint, string> CodeLines,
            ref Dictionary<string, uint> Markers,
            ref Dictionary<string, string> Variables,
            ref List<uint> TrashAddresses,
            ref List<string> Bindings,
            string Filepath, 
            string Region,
            Dictionary<string, uint> Symbols,
            ref Dictionary<uint, string> DuplicateAddressTracker)
        {
            string[] Lines = File.ReadAllLines(Filepath);
            Stack<uint> AddressStack = new();
            uint CurrentAddress = 0;
            bool IsActive = true, IsTrashing = false;

            for (int i = 0; i < Lines.Length; i++)
            {
                if (Lines[i].StartsWith(".GLE "))
                {
                    //Special GLE command!
                    string[] split = Lines[i].Split();
                    if (split.Length <= 1)
                        ThrowException("Invalid .GLE Prompt", Filepath, i);

#if DEBUG
                    if (split[1].Equals("DEBUG"))
                    {
                        Debugger.Break();
                    }
#endif

                    if (split[1].Equals("IGNORE"))
                        return i == 0 ? 1 : 2;

                    if (split[1].Equals("PRINTADDRESS"))
                    {
                        Console.WriteLine($"GLE: CURRENT ADDRESS - 0x{CurrentAddress:X8} in file {Filepath} on line {i}");
                        continue;
                    }

                    if (split[1].Equals("PRINTMESSAGE") && split.Length > 2)
                    {
                        Console.WriteLine($"GLE: {Lines[i][18..]}");
                        continue;
                    }

                    //New Build Tool Feature: Bindings and Hooks
                    //
                    //Bindings are C Functions that a Syati program can call to access GLE Functions
                    //
                    //Hooks are Kamek instructions that can extend certain GLE Functions with Syati code
                    if (split[1].Equals("BINDING") && split.Length > 2)
                    {
                        Console.WriteLine($"GLE: BINDING CREATED FOR \"{Lines[i][18..]}\"");
                        throw new NotImplementedException(); //TODO: Make a binding generator!
                        //continue;
                    }

                    if (split[1].Equals("HOOK") && split.Length > 2)
                    {
                        Console.WriteLine($"GLE: HOOK CREATED FOR \"{Lines[i][18..]}\"");
                        throw new NotImplementedException(); //TODO: Make a hook generator!
                        //continue;
                    }

                    if (!IsActive)
                        goto CheckRegion;

                    if (split[1].Equals("ADDRESS"))
                    {
                        if (split.Length <= 2)
                            ThrowException("Invalid .GLE ADDRESS Prompt (Missing address)", Filepath, i);

                        if (Variables.TryGetValue(split[2], out string? valueAddress))
                        {
                            split[2] = valueAddress;
                        }

                        if (Symbols.TryGetValue(split[2], out uint value))
                        {
                            split[2] = $"0x{value:X8}";
                        }
                        else if (Markers.TryGetValue(split[2], out uint value2))
                        {
                            split[2] = $"0x{value2:X8}";
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
                        AddressMath(split, i, ref Result);


                        //If we do not have a current address, we'll need to set the current address.
                        //if we do have a current address, we'll need to push it first.
                        if (CurrentAddress != 0)
                            AddressStack.Push(CurrentAddress);
                        CurrentAddress = Result;
                    }

                    if (split[1].Equals("ENDADDRESS"))
                    {
                        if (AddressStack.Count == 0)
                            continue;

                        CurrentAddress = AddressStack.Pop();
                    }

                    if (split[1].Equals("ASSERT"))
                    {
                        if (split.Length <= 2)
                            ThrowException("Invalid .GLE ASSERT Prompt", Filepath, i);
                        if (Variables.TryGetValue(split[2], out string? valueAddress))
                        {
                            split[2] = valueAddress;
                        }
                        if (Symbols.TryGetValue(split[2], out uint value))
                        {
                            split[2] = $"0x{value:X8}";
                        }
                        else if (Markers.TryGetValue(split[2], out uint value2))
                        {
                            split[2] = $"0x{value2:X8}";
                        }

                        string tmp = split[2][2..];
                        if (!uint.TryParse(tmp, System.Globalization.NumberStyles.HexNumber, null, out uint Result))
                        {
                            ThrowException("Invalid Address", Filepath, i);
                        }

                        //Deal with addictional offsets
                        AddressMath(split, i, ref Result);

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

                    if (split[1].Equals("TRASH"))
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


                if (string.IsNullOrWhiteSpace(Lines[i]) || Lines[i].StartsWith('#') ||!IsActive)
                    continue; //Skip empty lines & Comments


                if (LabelWithCommentRegex().IsMatch(Lines[i]))
                {
                    //Strip Comments from Labels
                    int x = Lines[i].IndexOf(':') + 1;
                    Lines[i] = Lines[i][..x];
                }

                if (LabelRegex().IsMatch(Lines[i]))
                {
                    //Catch Labels
                    //Mark the label's location, as we'll have to fill out the branches ourselves :weary:
                    Markers.Add(Lines[i][0..^1], CurrentAddress);
                    continue;
                }

                if (VariableRegex().IsMatch(Lines[i]))
                {
                    //Catch variables
                    Variables.Add(Lines[i][5..Lines[i].IndexOf(',')], Lines[i][(Lines[i].IndexOf(',') + 2)..]);
                    continue;
                }

                if (CodeLines.ContainsKey(CurrentAddress))
                {
                    //Track down the duplicate address!!

                    string ExistingFile = DuplicateAddressTracker[CurrentAddress];
                    string CurrentFile = new FileInfo(Filepath).Name;

                    ThrowException($"Duplicate Address 0x{CurrentAddress:X8} (It's in {ExistingFile})", Filepath, i);
                }


                //Assign the current line the address, then increment it
                CodeLines.Add(CurrentAddress, Lines[i]);
                DuplicateAddressTracker.Add(CurrentAddress, new FileInfo(Filepath).Name + "Line " + i);

                if (IsTrashing)
                    TrashAddresses.Add(CurrentAddress);


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

            void AddressMath(string[] split, int Line, ref uint Result)
            {
                //Deal with addictional offsets
                for (int x = 3; x < split.Length; x++)
                {
                    char op = split[x][0];
                    string off = split[x][3..];
                    if (!uint.TryParse(off, System.Globalization.NumberStyles.HexNumber, null, out uint r2))
                        ThrowException($"Invalid Number {off}", Filepath, Line);

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
            }
        }
    
    
        public static void PrepareAssembly(
            ref List<string> PreparedFiles,
            ref List<int> CodeCounts,
            ref uint CurrentAddress,
            SortedDictionary<uint, string> CodeLines,
            Dictionary<string, uint> Markers,
            Dictionary<string, string> Variables,
            Dictionary<string, uint> Symbols)
        {
            string? Current = null;
            List<string> InUseVars = [];
            uint StartAddress = 0;
            foreach (KeyValuePair<uint, string> item in CodeLines)
            {
                if (item.Key != CurrentAddress)
                {
                    if (Current is not null)
                    {
                        PreparedFiles.Add(Current);
                        CodeCounts.Add((int)((CurrentAddress - StartAddress) / 4));
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

                    if (Variables.TryGetValue(splitter[1], out string? valueBranch))
                    {
                        splitter[1] = valueBranch; //replace variable name with variable value.
                    }
                    //Try parse a direct vlaue right
                    uint Offset = 0;
                    if (!uint.TryParse(splitter[1][2..], System.Globalization.NumberStyles.HexNumber, null, out uint Result))
                    {
                        //If the number is not...well, a number, we'll need to check the Symbols, then Labels.
                        if (Symbols.TryGetValue(splitter[1], out uint value))
                        {
                            Offset = value - item.Key; //dest - current
                        }
                        else if (Markers.TryGetValue(splitter[1], out uint value2))
                        {
                            Offset = value2 - item.Key; //dest - current
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
                    Next(ref CurrentAddress);
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
                    if (Variables.TryGetValue(v, out string? valueInteger32))
                        v = valueInteger32;

                    if (Markers.TryGetValue(v, out uint value))
                    {
                        v = $"0x{value:X8}";
                    }
                    else if (Symbols.TryGetValue(v, out uint value2))
                    {
                        v = $"0x{value2:X8}";
                    }
                    fixedcode = ".int " + v;
                }

                if (fixedcode.StartsWith("addi "))
                {
                    //Correction for addi's that are way too high

                    string[] splitter = fixedcode.Split(',', StringSplitOptions.TrimEntries);
                    if (!splitter[2].Contains('@') && splitter[2].StartsWith("0x") && uint.TryParse(splitter[2][2..], System.Globalization.NumberStyles.HexNumber, null, out uint addiresult) && (addiresult > 0x7FFF))
                    {
                        addiresult |= 0xFFFF0000;
                        addiresult = (uint)-addiresult;
                        fixedcode = $"{splitter[0].Replace("addi", "subi")}, {splitter[1]}, 0x{addiresult:X8}";
                    }
                }

                Next(ref CurrentAddress);





                //This function allows the use of variables inside variables!
                //Does NOT work for Markers or Symbols!

                string TryAddInlineVariables(KeyValuePair<string, string> CurrentVariable)
                {
                    string x = CurrentVariable.Value;
                    string Result = "";
                    foreach (var var in Variables)
                    {
                        if (x.Contains(var.Key) && !InUseVars.Contains(var.Key))
                        {
                            InUseVars.Add(var.Key);
                            Result += TryAddInlineVariables(var);
                        }
                    }
                    Result += $".set {CurrentVariable.Key}, {CurrentVariable.Value}" + Environment.NewLine;
                    return Result;
                }








                foreach (var var in Variables)
                {
                    if (fixedcode.Contains(var.Key) && !InUseVars.Contains(var.Key))
                    {
                        InUseVars.Add(var.Key);
                        Current += TryAddInlineVariables(var);
                        //Current += $".set {var.Key}, {var.Value}" + Environment.NewLine;
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

                void Next(ref uint CurrentAddress)
                {
                    Current += fixedcode + Environment.NewLine;
                    CurrentAddress += 0x04;
                }
            }
            if (Current is null)
                throw new Exception("How did we get here?");

            PreparedFiles.Add(Current);
            CodeCounts.Add((int)((CurrentAddress - StartAddress) / 4));
        }


        public static (List<string> MemoryPatches, List<string> Dolphin) CompileAssembly(
            List<string> PreparedFiles,
            int i,
            string SourcePath,
            string AssemblyPath,
            List<int> CodeCounts,
            List<uint> Trash)
        {
            List<string> MemoryPatches = [];
            List<string> Dolphin = [];

            string Address = PreparedFiles[i].Substring(1, 8);
            uint CurrentAddress = uint.Parse(Address, System.Globalization.NumberStyles.HexNumber);


            File.WriteAllText(SourcePath, PreparedFiles[i]);
            //ExeCommand($"{Path.Combine(Program.ToolsPath, "powerpc-eabi-as.exe")} -mbig -mregnames -mgekko {SourcePath} -o {AssemblyPath}");
            ExeCommand($"{Path.Combine(Program.ToolsPath, "powerpc-eabi-as.exe")} -mbig -mregnames -mbroadway {SourcePath} -o {AssemblyPath}");
            if (!File.Exists(AssemblyPath))
            {
                throw new Exception("ERROR");
            }
            //Success! Lets read this....ELF? Who thought this was a good idea??

            string RiivoData = "";
            string IniData = "";
            FileStream FS = new(AssemblyPath, FileMode.Open)
            {
                Position = 0x34 //Code location...apparently always fixed
            };
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
            File.Delete(SourcePath);
            File.Delete(AssemblyPath);

            //All code aquired. Lets make a riivo memory patch
            string Mem = $"<memory offset=\"0x{Address.ToUpper()}\" value=\"{RiivoData}\" />";
            if (!string.IsNullOrWhiteSpace(RiivoData))
                MemoryPatches.Add(Mem);
            Dolphin.Add(IniData);

            return (MemoryPatches, Dolphin);
        }


        public static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
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

        [GeneratedRegex(@"\..*:\s+#.*", RegexOptions.Singleline)]
        private static partial Regex LabelWithCommentRegex();
        [GeneratedRegex(@"^.*:$", RegexOptions.Singleline)]
        private static partial Regex LabelRegex();
        [GeneratedRegex(@"\.set\s[a-zA-Z\<\>_0-9]+,\s.*$", RegexOptions.Singleline)]
        private static partial Regex VariableRegex();
    }
}
