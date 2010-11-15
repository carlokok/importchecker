//
// Import Checker; simple program to show what 1 or more dlls import
//
// Authors:
//  Carlo Kok <ck@remobjects.com>
//
// Copyright (C) 2010 RemObjects Software
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Metadata;
using Mono.Options;
using System.IO;
using System.Xml.Serialization;
using System.Text.RegularExpressions;

namespace ImportChecker
{
    class Program 
    {
        enum Mode { None, Create, Check }
        
        static List<string> input = new List<string>();
        static List<string> excludedLibraries = new List<string>();
        static List<string> includedLibraries = new List<string>();
        static string output = null;
        static List<string> searchPath = new List<string>();

        static DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();



        internal static bool IsFiltered(TypeReference tref) // true = skip this one
        {
            if (includedLibraries.Count != 0)
            {
                bool gotmatch=  false;
                for (int i= 0 ;i < includedLibraries.Count; i++)
                {
                    if (Match(tref.Scope.Name, includedLibraries[i]))
                    {
                        gotmatch = true;
                        break;
                    }
                }
                if (!gotmatch) return true; 
            }
            for (int i = 0; i < excludedLibraries.Count; i++)
                if (Match(tref.Scope.Name, excludedLibraries[i]))
                    return true;

            return false;
        }

        
        private static bool Match(string p, string match)
        {
            p = p.ToLowerInvariant();
            match = match.ToLowerInvariant();
            return Regex.IsMatch(p, Regex.Escape(match).Replace(@"\*", ".*").Replace(@"\?", "."), RegexOptions.Singleline);
        }

        static int Main(string[] args)
        {
            resolver.ResolveFailure += new AssemblyResolveEventHandler(ResolveFailure);
            Mode lMode = Mode.None;
            bool lHelp = false;
            OptionSet opt = new OptionSet();
            opt.Add("e|excludelibrary=", "Exclude an assembly name (accepts standard path filters * and ?), just the name part", v => excludedLibraries.Add(v));
            opt.Add("i|includelibrary=", "Include an assembly name (accepts standard path filters * and ?), just the name part", v => includedLibraries.Add(v));
            opt.Add("o|output=", "Output to a file", v => output = v);
            opt.Add("s|searchpath=", "Add a search path", v=> searchPath.Add(v));
            opt.Add("help", "Show help", v => lHelp = v != null);
            opt.Add("create", "Create an xml file (-o, and 1 or more dlls as input)", v =>
            {
                if (lMode != Mode.None) throw new OptionException("mode cannot be set twice", "create");
                lMode = Mode.Create;
            });
            opt.Add("check", "Compare an xml file against the existing dlls (1 or more xml files as input)", v =>
            {
                if (lMode != Mode.None) throw new OptionException("mode cannot be set twice", "check");
                lMode = Mode.Check;
            });

            try
            {
                foreach (string file in opt.Parse(args))
                {
                    if (file.StartsWith("@")) // allow parameter input from a file
                    {
                        input.AddRange(ArgumentStringSplit(File.ReadAllText(file.Substring(1))));
                    }
                    else
                        input.Add(file);
                }
                if (lHelp)
                {
                    Console.Error.WriteLine("Usage: ImportChecker [Options] files");
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Options: ");
                    opt.WriteOptionDescriptions(Console.Error);
                    return 1;
                }
                if (lMode == Mode.None)
                    throw new OptionException("No mode selected. --check or --create required", "mode");
                foreach (var el in input)
                {
                    if (!File.Exists(el))
                        throw new OptionException("File " + el + " does not exist", "file");
                }

                if (lMode == Mode.Check)
                    return Check();
                else
                    return Generate();

            }
            catch (OptionException e)
            {
                Console.Error.WriteLine("Error: " + e.Message);
                Console.Error.WriteLine("Use --help for help");
                return 1;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Error: " + e.ToString());
                Console.Error.WriteLine("Use --help for help");
                return 1;
            }

            /*
             List<string> lInput = new List<string>();
            List<string> lExcludedLibraries = new List<string>();
            List<string> lIncludedLibraries = new List<string>();
            string lOutput = null;
            List<string> lSearchPath = new List<string>();
            Mode lMode = Mode.None;
             
             */
        }

        private static int Check()
        {
            XmlSerializer ser = new XmlSerializer(typeof(importchecker));
            bool ok = true;
            foreach (var el in input)
            {
                using (StreamReader sr = new StreamReader(el))
                {
                    importchecker check = ser.Deserialize(sr) as importchecker;
                    if (!CheckImports(check))
                        ok = false;
                }
            }
            if (ok)
                return 0;
            return 2;
        }

        private static bool CheckImports(importchecker check)
        {
            throw new NotImplementedException();
        }

        private static int Generate()
        {
            Worker wrk = new Worker();
            foreach (var el in input)
            {
                ReaderParameters rp = new ReaderParameters(ReadingMode.Immediate);
                rp.AssemblyResolver = resolver;
                rp.ReadSymbols = false;
                ModuleDefinition md = ModuleDefinition.ReadModule(el, rp);

                wrk.Work(md);
            }

            XmlSerializer ser = new XmlSerializer(wrk.Output.GetType());
            if (output == null)
                ser.Serialize(Console.Out, wrk.Output);
            else
                using (StreamWriter sw = new StreamWriter(output, false, Encoding.UTF8))
                    ser.Serialize(sw, wrk.Output);
            return 0;
        }

        private static IEnumerable<string> ArgumentStringSplit(string args)
        {
            List<string> res = new List<string>();
            StringBuilder sb = new StringBuilder();
            int instr = 0;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case '\'':
                        if (instr == 1) goto default;
                        if (instr == 2 && i < args.Length - 1 && args[i + 1] == '\'')
                        {
                            i++;
                            sb.Append("\'");
                        }
                        else
                        {
                            if (instr == 0)
                                instr = 2;
                            else
                                instr = 0;
                        }
                        break;
                    case '\"':
                        if (instr == 2) goto default;
                        if (instr == 1 && i < args.Length - 1 && args[i + 1] == '\"')
                        {
                            i++;
                            sb.Append("\"");
                        }
                        else
                        {
                            if (instr == 0)
                                instr = 1;
                            else
                                instr = 0;
                        }
                        break;
                    case ' ':
                        if (instr == 0)
                        {
                            res.Add(sb.ToString());
                            if (sb.Length > 0)
                            sb.Length = 0;
                        }
                        else
                            sb.Append(" ");
                        break;
                    default:
                        sb.Append(args[i]);
                        break;
                }
            }
            if (sb.Length > 0)
                res.Add(sb.ToString());
            return res;
        }
        static AssemblyDefinition ResolveFailure(object sender, AssemblyNameReference reference)
        {
            for (int i =0; i < searchPath.Count; i++) {
                string cmb = Path.Combine(searchPath[i], reference.Name);
                if (File.Exists(cmb))
                {
                    try
                    {
                        ModuleDefinition md = ModuleDefinition.ReadModule(cmb);
                        if (md.Assembly.Name.FullName == reference.FullName)
                            return md.Assembly;
                    }
                    catch
                    {
                        // could be bad input
                    }
                }
            }
            return null;
        }

    }
}
