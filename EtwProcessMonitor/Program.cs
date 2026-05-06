using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Diagnostics.Tracing.Parsers;

namespace ETWProcessMonitor
{
    internal class Program
    {
        // Args:
        //   --name <substring>   Optional: only show processes whose image name contains substring (case-insensitive)
        //   --image              Optional: also enable ImageLoad keyword (more events; can help for correlating image loads)
        //
        // Examples:
        //   ETWProcessMonitor.exe
        //   ETWProcessMonitor.exe --name Taskmgr.exe
        //   ETWProcessMonitor.exe --image
        //   ETWProcessMonitor.exe --name powershell --image
        public static int Main(string[] args)
        {
            string? nameFilter = GetArgValue(args, "--name");
            bool enableImageLoad = args.Any(a => string.Equals(a, "--image", StringComparison.OrdinalIgnoreCase));

            bool isWin7OrEarlier = Environment.OSVersion.Version < new Version(6, 2);
            string sessionName = isWin7OrEarlier ? KernelTraceEventParser.KernelSessionName : "ETWProcessMonitor";
            
            WriteHeaders(nameFilter, enableImageLoad, sessionName);

            CancellationTokenSource cts = new CancellationTokenSource();

            try
            {
                TraceEventSession session = new(sessionName)
                {
                    StopOnDispose = true,
                    BufferSizeMB = 1
                };

                KernelTraceEventParser.Keywords keywords = KernelTraceEventParser.Keywords.Process;

                if (enableImageLoad)
                {
                    keywords |= KernelTraceEventParser.Keywords.ImageLoad;
                }

                session.EnableKernelProvider(keywords);

                session.Source.Kernel.ProcessStart += data =>
                {
                    if (PassesFilter(data.ImageFileName, nameFilter))
                    {
                        ConsoleColor foregroundColorBackup = Console.ForegroundColor;
                        Console.ForegroundColor = ConsoleColor.Green;

                        Console.WriteLine
                            (
                                $"[STARTED] {data.TimeStamp:O}  PID={data.ProcessID,6}  PPID={data.ParentID,6}  {data.ImageFileName}"
                            );

                        Console.ForegroundColor = foregroundColorBackup;
                    }
                };

                session.Source.Kernel.ProcessStop += data =>
                {
                    if (PassesFilter(data.ImageFileName, nameFilter))
                    {
                        ConsoleColor foregroundColorBackup = Console.ForegroundColor;
                        Console.ForegroundColor = ConsoleColor.Red;

                        Console.WriteLine
                            (
                                $"[STOPPED] {data.TimeStamp:O}  PID={data.ProcessID,6}  PPID={data.ParentID,6}  {data.ImageFileName}"                                 
                            );

                        Console.ForegroundColor = foregroundColorBackup;
                    }
                };
                
                ClosingHandler(cts, session);
                
                Task<bool> pump = Task.Run(() => session.Source.Process(), cts.Token);

                while (!cts.IsCancellationRequested)
                {
                    Thread.Sleep(100);
                }

                session.Source.StopProcessing();
                try
                {
                    pump.Wait(TimeSpan.FromSeconds(2));
                }
                catch
                { 
                    // ignored
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Failed to start ETW session.");
                Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
                Console.Error.WriteLine();
                Console.Error.WriteLine("Tip: Try running this console as Administrator.");
                return 1;
            }
        }

        private static void ClosingHandler(CancellationTokenSource cts, TraceEventSession session)
        {
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                session.Source.StopProcessing();
            };
        }

        private static void WriteHeaders(string? nameFilter, bool enableImageLoad, string sessionName)
        {
            Console.WriteLine("ETW Process Monitor (ProcessStart/Stop)");
            Console.WriteLine($"Session: {sessionName}");
            Console.WriteLine($"Filter:  {(string.IsNullOrWhiteSpace(nameFilter) ? "(none)" : nameFilter)}");
            Console.WriteLine($"ImageLoad enabled: {enableImageLoad}");
            Console.WriteLine("Press Ctrl+C to stop.");
            Console.WriteLine();
        }

        private static bool PassesFilter(string? imageFileName, string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            if (string.IsNullOrWhiteSpace(imageFileName)) return false;

            return imageFileName.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetArgValue(string[] args, string key)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
