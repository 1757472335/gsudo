﻿using gsudo.Commands;
using gsudo.Native;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace gsudo.Helpers
{
    public static class ArgumentsHelper
    {
        internal static string[] AugmentCommand(string[] args)
        {
            string currentShellExeName;
            Shell currentShell = ShellHelper.DetectInvokingShell(out currentShellExeName);

            // Is our current shell Powershell ? (Powershell.exe -calls-> gsudo)
            if (currentShell.In(Shell.PowerShell, Shell.PowerShellCore))
            {
                if (args.Length == 0)
                    return new string[]
                        { currentShellExeName };
                else
                    return new string[] 
                        { currentShellExeName ,
                            "-NoProfile",
                            String.Join(" ", args)
                        };
            }
            
            // Not Powershell, or Powershell Core, assume CMD.
            if (args.Length == 0)
            {
                return new string[]
                    { Environment.GetEnvironmentVariable("COMSPEC"), "/k" };
            }
            else
            {
                args[0] = UnQuote(args[0]);
                var exename = ProcessFactory.FindExecutableInPath(args[0]);
                if (exename==null)
                {
                    // add "CMD /C" prefix to commands such as MD, CD, DIR..
                    // We are sure this will not be an interactive experience, 

                    return new string[]
                        { Environment.GetEnvironmentVariable("COMSPEC"), "/c" }
                        .Concat(args).ToArray();
                }
                else
                {
                    args[0] = exename;
                    return args;
                }
            }
        }

        public static string[] SplitArgs(string args)
        {
            args = args.Trim();
            var results = new List<string>();
            int pushed = 0;
            int curr = 0;
            bool insideQuotes = false;
            while (curr < args.Length)
            {
                if (args[curr] == '"')
                    insideQuotes=!insideQuotes;
                else if (args[curr] == ' ' && !insideQuotes)
                {
                    results.Add(args.Substring(pushed, curr-pushed));
                    pushed = curr+1;
                }
                curr++;                
            }

            if (pushed < curr)
                results.Add(args.Substring(pushed, curr - pushed));
            return results.ToArray();
        }

        internal static int? ParseCommonSettings(ref string[] args)
        {
            Stack<string> stack = new Stack<string>(args.Reverse());

            while (stack.Any())
            {
                var arg = stack.Peek();
                if (arg.In("-v", "--version"))
                {
                    HelpCommand.ShowVersion();
                    return 0;
                }
                else if (arg.In("-h", "--help", "help", "/?"))
                {
                    HelpCommand.ShowHelp();
                    return 0;
                }
                else if (arg.In("-n", "--new"))
                {
                    GlobalSettings.NewWindow = true;
                    stack.Pop();
                }
                else if (arg.In("-w", "--wait"))
                {
                    GlobalSettings.Wait = true;
                    stack.Pop();
                }
                else if (arg.In("--debug"))
                {
                    GlobalSettings.Debug = true;
                    GlobalSettings.LogLevel.Value = LogLevel.All;
                    stack.Pop();
                }
                else if (arg.In("--loglevel"))
                {
                    stack.Pop();
                    arg = stack.Pop();
                    try
                    {
                        GlobalSettings.LogLevel.Value = (LogLevel)Enum.Parse(typeof(LogLevel), arg, true);
                    }
                    catch
                    {
                        Logger.Instance.Log($"\"{arg}\" is not a valid LogLevel. Valid values are: All, Debug, Info, Warning, Error, None", LogLevel.Error);
                        return Constants.GSUDO_ERROR_EXITCODE;
                    }
                }
                else if (arg.In("--raw"))
                {
                    GlobalSettings.ForceRawConsole.Value = true;
                    stack.Pop();
                }
                else if (arg.In("--vt"))
                {
                    GlobalSettings.ForceVTConsole.Value = true;
                    stack.Pop();
                }
                else if (arg.StartsWith("-", StringComparison.Ordinal))
                {
                    Logger.Instance.Log($"Invalid option: {arg}", LogLevel.Error);
                    return Constants.GSUDO_ERROR_EXITCODE;
                }
                else
                {
                    break;
                }
            }

            args = stack.ToArray();
            return null;
        }

        internal static ICommand ParseCommand(string[] args)
        {
            if (args.Length == 0) return new RunCommand() { CommandToRun = Array.Empty<string>() };

            if (args[0].Equals("run", StringComparison.OrdinalIgnoreCase))
                return new RunCommand() { CommandToRun = args.Skip(1) };

            if (args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
                return new HelpCommand();

            if (args[0].Equals("gsudoservice", StringComparison.OrdinalIgnoreCase))
            {
                bool hasLoglevel = false;
                LogLevel logLevel = LogLevel.Info;
                if (args.Length>3)
                {
                    hasLoglevel = Enum.TryParse<LogLevel>(args[3], true, out logLevel);
                }

                return new ServiceCommand()
                {
                    allowedPid = int.Parse(args[1], CultureInfo.InvariantCulture),
                    allowedSid = args[2],
                    LogLvl = hasLoglevel ? logLevel : (LogLevel?)null,
                };
            }

            if (args[0].Equals("gsudoctrlc", StringComparison.OrdinalIgnoreCase))
                return new CtrlCCommand() { pid = int.Parse(args[1], CultureInfo.InvariantCulture) };

            if (args[0].Equals("config", StringComparison.OrdinalIgnoreCase))
                return new ConfigCommand() { key = args.Skip(1).FirstOrDefault(), value = args.Skip(2) };

            return new RunCommand() { CommandToRun = args };
        }

        internal static string GetRealCommandLine()
        {
            System.IntPtr ptr = ConsoleApi.GetCommandLine();
            string commandLine = Marshal.PtrToStringAuto(ptr);
            Logger.Instance.Log($"Command Line: {commandLine}", LogLevel.Debug);

            if (commandLine[0] == '"')
                return commandLine.Substring(commandLine.IndexOf('"', 1) + 1).TrimStart(' ');
            else if (commandLine.IndexOf(' ', 1) >= 0)
                return commandLine.Substring(commandLine.IndexOf(' ', 1) + 1).TrimStart(' ');
            else
                return string.Empty;
        }

        static string UnQuote(string v)
        {
            if (string.IsNullOrEmpty(v)) return v;
            if (v[0] == '"' && v[v.Length - 1] == '"')
                return v.Substring(1, v.Length - 2);
            else if (v[0] == '"')
                return v.Substring(1);
            else
                return v;
        }
    }
}
