#define ENABLE_GH_API
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        private Dictionary<string, string> Vars;

        private string GITHUB_REPOSITORY_OWNER;
        private string GITHUB_REPOSITORY_NAME;
        private string GITHUB_TOKEN;
        private string GITHUB_SHA;
        private string GITHUB_WORKSPACE; // /home/runner/work/sandbox/sandbox
        private CheckRun CurrentCheckRun = null;
#if ENABLE_GH_API
        private GitHubClient gitHubClient = null;
#endif 

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
            Vars.TryGetValue("GITHUB_REPOSITORY_OWNER", out GITHUB_REPOSITORY_OWNER);
            GITHUB_REPOSITORY_OWNER = GITHUB_REPOSITORY_OWNER ?? "";

            Vars.TryGetValue("GITHUB_REPOSITORY_NAME", out GITHUB_REPOSITORY_NAME);
            GITHUB_REPOSITORY_NAME = GITHUB_REPOSITORY_NAME?.Split('/').Last() ?? "";

            Vars.TryGetValue("GITHUB_TOKEN", out GITHUB_TOKEN);
            GITHUB_TOKEN = GITHUB_TOKEN ?? "";

            Vars.TryGetValue("GITHUB_SHA", out GITHUB_SHA);
            GITHUB_SHA = GITHUB_SHA ?? "";

            Vars.TryGetValue("GITHUB_WORKSPACE", out GITHUB_WORKSPACE);
            GITHUB_WORKSPACE = GITHUB_WORKSPACE ?? "";

            // connection to github api 
#if ENABLE_GH_API
            gitHubClient = new GitHubClient(new ProductHeaderValue(GITHUB_REPOSITORY_OWNER)) {
                Credentials = new Credentials(GITHUB_TOKEN)
            };
#endif
            //foreach (var p in Vars) {
            //    Console.WriteLine("variable: " + p.Key + "=" + p.Value);
            //}

            // Raised when a test message is received.
            events.TestRunMessage += Events_TestRunMessage;

            // Raised when a test run starts.
            events.TestRunStart += Events_TestRunStart;

            // Raised when a test result is received.
            events.TestResult += Events_TestResult;

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
        private void Events_TestRunStart(object sender, TestRunStartEventArgs e) {
            Console.WriteLine($"*** Events_TestRunStart ***");



            Console.Write("attempt check run creation");

            var check = new NewCheckRun(CheckRunName, GITHUB_SHA) {
                Output = new NewCheckRunOutput(CheckRunName, "Running...") {
                    //Text = "NewCheckRunOutput.Text",
                },
                Status = CheckStatus.InProgress,
            };
#if ENABLE_GH_API
            CurrentCheckRun = gitHubClient.Check.Run.Create(GITHUB_REPOSITORY_OWNER, GITHUB_REPOSITORY_NAME, check).Result;
#endif
        }

        /// <summary>Raised when a test result is received.</summary>
        private void Events_TestResult(object sender, TestResultEventArgs e) {
            Console.WriteLine($"*** Events_TestResult *** {e.Result.TestCase.FullyQualifiedName}: {e.Result.ErrorMessage}-{e.Result.Outcome} ");

            if (e.Result.Outcome == TestOutcome.Passed || e.Result.Outcome == TestOutcome.None) {
                //don't annotate successfull tests
                return;
            }

            NewCheckRunAnnotation newAnnotation = null;
            switch (e.Result.Outcome) {
                case TestOutcome.Failed: // CheckAnnotationLevel.Failure
                    var (filename, methodname, sourceline) = StackFrameFromTrace(e.Result.ErrorStackTrace);
                    newAnnotation = new NewCheckRunAnnotation(WorkspaceRelativePath(filename), int.Parse(sourceline), int.Parse(sourceline) + 5, CheckAnnotationLevel.Failure, e.Result.ErrorMessage);
                    newAnnotation.Title = e.Result.DisplayName;
                    break;
                case TestOutcome.Skipped: //CheckAnnotationLevel.Warning
                    break;
                case TestOutcome.NotFound: // CheckAnnotationLevel.Failure
                    break;
            }
            if (newAnnotation != null) {
                var check = new CheckRunUpdate();
                check.Output = new NewCheckRunOutput(CheckRunName, "Running...") {
                    Annotations = new List<NewCheckRunAnnotation>() { newAnnotation }
                };
#if ENABLE_GH_API
                CurrentCheckRun = gitHubClient.Check.Run.Update(GITHUB_REPOSITORY_OWNER, GITHUB_REPOSITORY_NAME, CurrentCheckRun.Id, check).Result;
#endif
            }
            // update check run with additional annotations
            //gitHubClient.Check.Run.
            //var check = new CheckRunUpdate();
            //  var newAnnotation = new NewCheckRunAnnotation(CheckAnnotationLevel);
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

            Console.WriteLine($"*** Events_TestResult *** {e.Result.TestCase.FullyQualifiedName}: {e.Result.ErrorMessage}-{e.Result.Outcome} ");
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
