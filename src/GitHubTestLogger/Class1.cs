//#define ENABLE_GH_API

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Octokit;

namespace GitHubTestLogger {

    [FriendlyName(FriendlyName)]
    [ExtensionUri(ExtensionUri)]
    public class GitHubTestLogger : ITestLoggerWithParameters {
        /// <summary>Uri used to uniquely identify the logger.</summary>
        public const string ExtensionUri = "logger://github.com/dogguts/GitHubTestLogger";

        /// <summary>Alternate user friendly string to uniquely identify the console logger.</summary>
        public const string FriendlyName = "github";

        /// <summary>Name of the Check Run/Job</summary>
        private const string CheckRunName = "test-report";

        private readonly string[] MandatoryVars = { "GITHUB_REPOSITORY_OWNER", "GITHUB_TOKEN", "GITHUB_SHA", "GITHUB_REPOSITORY" };

        private IDictionary<string, string> Vars = new Dictionary<string, string>();

        private string GITHUB_REPOSITORY_OWNER = string.Empty;
        private string GITHUB_REPOSITORY_NAME = string.Empty;
        private string GITHUB_TOKEN = string.Empty;
        private string GITHUB_SHA = string.Empty;
        private string GITHUB_WORKSPACE = string.Empty; // /home/runner/work/sandbox/sandbox
        private CheckRun? CurrentCheckRun = null;
        //#if ENABLE_GH_API
        private IGitHubClient? _gitHubClient = null;//= new GitHubClient();
        private IGitHubClient GitHubClient {
            get => _gitHubClient ?? throw new NullReferenceException($"{nameof(GitHubClient)} not initialized");
            set => _gitHubClient = value;
        }
        //#endif 

        /// <summary>Concatenate Dictionaries, keep the first KeyValuePair on duplicate Keys</summary>
        private static Dictionary<string, string> BuildVariables(params Dictionary<string, string>[] args) {
            var result = new Dictionary<string, string>();
            foreach (var arg in args) {
                result = result.Concat(arg.Where(x => !result.ContainsKey(x.Key))).ToDictionary(x => x.Key, x => x.Value);
            }
            return result;
        }

        private static (string filename, string methodname, string sourceline) StackFrameFromTrace(string stackTrace) {
            var s = new StackFrame();
            var stackLine = stackTrace.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()).First();
            var regex = new System.Text.RegularExpressions.Regex(@"(\ *)at (?<method>.*) in (?<filename>.*):line (?<linenumber>\d+)");
            var match = regex.Match(stackTrace);
            return (match.Groups["filename"].Value, match.Groups["method"].Value, match.Groups["linenumber"].Value);
        }

        private string WorkspaceRelativePath(string fullPath) {
            if (!string.IsNullOrEmpty(GITHUB_WORKSPACE)) {
                return fullPath.Replace(GITHUB_WORKSPACE, "");
            } else {
                return fullPath;
            }
        }


        private const string TestMessageFormattingPrefix = " ";
        private const string TestResultPrefix = "  ";

        private static string GetFormattedOutput(IEnumerable<TestResultMessage> testMessageCollection) {
            if (testMessageCollection != null) {
                var sb = new System.Text.StringBuilder();
                foreach (var message in testMessageCollection) {
                    var prefix = String.Format(CultureInfo.CurrentCulture, "{0}{1}", Environment.NewLine, TestMessageFormattingPrefix);
                    var messageText = message.Text?.Replace(Environment.NewLine, prefix).TrimEnd(TestMessageFormattingPrefix.ToCharArray());

                    if (!string.IsNullOrWhiteSpace(messageText)) {
                        sb.AppendFormat(CultureInfo.CurrentCulture, "{0}{1}", TestMessageFormattingPrefix, messageText);
                    }
                }
                return sb.ToString();
            }
            return String.Empty;
        }

        private string GetFullInformation(TestResult result) {
            var Output = new System.Text.StringBuilder();

            if (!String.IsNullOrEmpty(result.ErrorMessage)) {
                Output.AppendFormatLine(CultureInfo.CurrentCulture, "{0}{1}", TestResultPrefix, "Error Message:");
                Output.AppendFormatLine(CultureInfo.CurrentCulture, "{0}{1}{2}", TestResultPrefix, TestMessageFormattingPrefix, result.ErrorMessage);
            }

            if (!String.IsNullOrEmpty(result.ErrorStackTrace)) {
                Output.AppendFormatLine("{0}{1}", TestResultPrefix, "Stack Trace:");
                Output.AppendFormatLine(CultureInfo.CurrentCulture, "{0}{1}", TestResultPrefix, result.ErrorStackTrace);
            }

            var stdOutMessagesList = result.Messages.Where(msg => msg.Category.Equals(TestResultMessage.StandardOutCategory, StringComparison.OrdinalIgnoreCase)).ToList();
            if (stdOutMessagesList.Count > 0) {
                var stdOutMessages = GetFormattedOutput(stdOutMessagesList);

                if (!string.IsNullOrEmpty(stdOutMessages)) {
                    Output.AppendFormatLine("{0}{1}", TestResultPrefix, "Standard Output Messages:");
                    Output.AppendLine(stdOutMessages);
                }
            }

            var stdErrMessagesList = result.Messages.Where(msg => msg.Category.Equals(TestResultMessage.StandardErrorCategory, StringComparison.OrdinalIgnoreCase)).ToList();
            if (stdErrMessagesList.Count > 0) {
                var stdErrMessages = GetFormattedOutput(stdErrMessagesList);

                if (!string.IsNullOrEmpty(stdErrMessages)) {
                    Output.AppendFormatLine("{0}{1}", TestResultPrefix, "Standard Error Messages:");
                    Output.AppendLine(stdErrMessages);
                }
            }

            var dbgTrcMessagesList = result.Messages.Where(msg => msg.Category.Equals(TestResultMessage.DebugTraceCategory, StringComparison.OrdinalIgnoreCase)).ToList();
            if (dbgTrcMessagesList.Count > 0) {
                var dbgTrcMessages = GetFormattedOutput(dbgTrcMessagesList);

                if (!string.IsNullOrEmpty(dbgTrcMessages)) {
                    Output.AppendFormatLine("{0}{1}", TestResultPrefix, "Debug Trace Messages:");
                    Output.AppendLine(dbgTrcMessages);
                }
            }

            var addnlInfoMessagesList = result.Messages.Where(msg => msg.Category.Equals(TestResultMessage.AdditionalInfoCategory, StringComparison.OrdinalIgnoreCase)).ToList();
            if (addnlInfoMessagesList.Count > 0) {
                var addnlInfoMessages = GetFormattedOutput(addnlInfoMessagesList);

                if (!string.IsNullOrEmpty(addnlInfoMessages)) {
                    Output.AppendFormatLine("{0}{1}", TestResultPrefix, "Additional Information Messages:");
                    Output.AppendLine(addnlInfoMessages);
                }
            }
            return Output.ToString();
        }

        public void Initialize(TestLoggerEvents events, string testRunDirectory) {
            Initialize(events, new Dictionary<string, string>() { { "TestRunDirectory", testRunDirectory } });
        }

        public void Initialize(TestLoggerEvents events, Dictionary<string, string> parameters) {

            Console.WriteLine("Initialize1-w");
            Debugger.Launch();

            var environmentVariables = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process).ToDictionary<string, string>();

            // Build Vars, parameters take precedence over environment variables
            Vars = BuildVariables(parameters, environmentVariables);

            // Keep some frequently used Vars 
            GITHUB_REPOSITORY_OWNER = Vars.GetValueOrDefault("GITHUB_REPOSITORY_OWNER", string.Empty);

            GITHUB_REPOSITORY_NAME = Vars.GetValueOrDefault("GITHUB_REPOSITORY", string.Empty).Split('/').Last();

            GITHUB_TOKEN = Vars.GetValueOrDefault("GITHUB_TOKEN", string.Empty);

            GITHUB_SHA = Vars.GetValueOrDefault("GITHUB_SHA", string.Empty);

            GITHUB_WORKSPACE = Vars.GetValueOrDefault("GITHUB_WORKSPACE", String.Empty);

            // connection to github api 
            //#if ENABLE_GH_API
            GitHubClient = new GitHubClient(new ProductHeaderValue(GITHUB_REPOSITORY_OWNER)) {
                Credentials = new Credentials(GITHUB_TOKEN)
            };
            //#endif
            foreach (var p in Vars) {
                Console.WriteLine("+++ variable: " + p.Key + "=" + p.Value);
            }

            // Raised when a test message is received.
            events.TestRunMessage += Events_TestRunMessage;

            // Raised when a test run starts.
            events.TestRunStart += async (s, e) => await Events_TestRunStart(s, e);


            // Raised when a test result is received.
            events.TestResult += async (s, e) => await Events_TestResult(s, e);

            // Raised when a test run is complete.
            events.TestRunComplete += Events_TestRunComplete;

            // Raised when test discovery starts
            events.DiscoveryStart += Events_DiscoveryStart;

            // Raised when a discovery message is received.
            events.DiscoveryMessage += Events_DiscoveryMessage;

            // Raised when discovered tests are received
            events.DiscoveredTests += Events_DiscoveredTests;

            // Raised when test discovery is complete
            events.DiscoveryComplete += Events_DiscoveryComplete;
        }


        /// <summary>Raised when a test message is received.</summary>
        private void Events_TestRunMessage(object sender, TestRunMessageEventArgs e) {
            Console.WriteLine($"*** Events_TestRunMessage *** {e.Level}: {e.Message}");

        }

        /// <summary>Raised when a test run starts.</summary> 
        private async Task Events_TestRunStart(object sender, TestRunStartEventArgs e) {
            Console.WriteLine($"*** Events_TestRunStart ***");



            Console.Write("attempt check run creation");

            var newCheckRun = new NewCheckRun(CheckRunName, GITHUB_SHA) {
                Output = new NewCheckRunOutput("somethingg titly", "Something summary Running...") {
                    Text = "NewCheckRunOutput.Text",
                },
                Status = CheckStatus.Queued,
            };
            //#if ENABLE_GH_API

            CurrentCheckRun = await GitHubClient.Check.Run.Create(GITHUB_REPOSITORY_OWNER, GITHUB_REPOSITORY_NAME, newCheckRun);
            //#endif
        }

        /// <summary>Raised when a test result is received.</summary>
        private async Task Events_TestResult(object sender, TestResultEventArgs e) {
            Console.WriteLine($"*** Events_TestResult *** {e.Result.TestCase.FullyQualifiedName}: {e.Result.ErrorMessage}-{e.Result.Outcome} ");

            if (e.Result.Outcome == TestOutcome.Passed || e.Result.Outcome == TestOutcome.None) {
                //don't annotate successfull tests
                return;
            }

            NewCheckRunAnnotation? newAnnotation = null;
            switch (e.Result.Outcome) {
                case TestOutcome.Failed: // CheckAnnotationLevel.Failure
                    var (filename, methodname, sourceline) = StackFrameFromTrace(e.Result.ErrorStackTrace);
                    newAnnotation = new NewCheckRunAnnotation(WorkspaceRelativePath(filename), int.Parse(sourceline), int.Parse(sourceline) + 5, CheckAnnotationLevel.Failure, e.Result.ErrorMessage);
                    newAnnotation.Title = e.Result.DisplayName;
                    newAnnotation.RawDetails = GetFullInformation(e.Result);
                    break;
                case TestOutcome.Skipped: //CheckAnnotationLevel.Warning
                    break;
                case TestOutcome.NotFound: // CheckAnnotationLevel.Failure
                    break;
            }
            if (newAnnotation != null) {
                var check = new CheckRunUpdate() {
                    Output = new NewCheckRunOutput(CheckRunName, "Running...") {
                        Annotations = new List<NewCheckRunAnnotation>() { newAnnotation }
                    },
                    Status = CheckStatus.InProgress
                };
                //#if ENABLE_GH_API
                CurrentCheckRun = await GitHubClient.Check.Run.Update(GITHUB_REPOSITORY_OWNER, GITHUB_REPOSITORY_NAME, CurrentCheckRun?.Id ?? -1, check);
                //#endif
            }

            /*
    TestOutcome:
        None = 0,
        Passed = 1,
        Failed = 2,
        Skipped = 3,
        NotFound = 4
             */
            /*
    public enum CheckAnnotationLevel {
        Notice = 0,
        Warning = 1,
        Failure = 2
    }             
             */

            //  gitHubClient.Check.Run.Update ()


            //Console.WriteLine(e.Result.TestCase.FullyQualifiedName + "+++ " + e.Result.Outcome.ToString());
            //Console.WriteLine(e.Result.TestCase.FullyQualifiedName);
            //Console.WriteLine(e.Result.TestCase.Source);
            //Console.WriteLine(e.Result.TestCase.LineNumber);
        }

        /// <summary>Raised when a test run is complete.</summary>
        private void Events_TestRunComplete(object sender, TestRunCompleteEventArgs e) {
            Console.WriteLine($"*** Events_TestRunComplete ***");
            var xxxx = e.TestRunStatistics.Stats;

        }

        /// <summary>Raised when test discovery starts</summary> 
        private void Events_DiscoveryStart(object sender, DiscoveryStartEventArgs e) {
            Console.WriteLine($"*** Events_DiscoveryStart *** {e.DiscoveryCriteria}");
        }

        /// <summary>Raised when a discovery message is received.</summary>
        private void Events_DiscoveryMessage(object sender, TestRunMessageEventArgs e) {
            Console.WriteLine($"*** Events_DiscoveryMessage *** {e.Level}: {e.Message}");
        }

        /// <summary>Raised when discovered tests are received</summary>
        private void Events_DiscoveredTests(object sender, DiscoveredTestsEventArgs e) {
            Console.WriteLine($"*** Events_DiscoveredTests *** {e.DiscoveredTestCases}");
        }

        /// <summary>Raised when test discovery is complete</summary>
        private void Events_DiscoveryComplete(object sender, DiscoveryCompleteEventArgs e) {
            Console.WriteLine($"*** Events_DiscoveryComplete *** {e.TotalCount}");
        }
    }
}
