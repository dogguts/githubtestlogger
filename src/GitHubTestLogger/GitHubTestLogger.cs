//NOTE: undefine ENABLE_GH_API for local debugging; enables/disables effectively calling the GitHub api 
#define ENABLE_GH_API

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Octokit;

namespace Microsoft.TestPlatform.Extensions.GitHub.TestLogger {

    [FriendlyName(FriendlyName)]
    [ExtensionUri(ExtensionUri)]
    public class GitHubTestLogger : ITestLoggerWithParameters {
        /// <summary>Uri used to uniquely identify the logger.</summary>
        public const string ExtensionUri = "logger://Microsoft/TestPlatform/GitHubLogger/v1";

        /// <summary>Friendly name which uniquely identifies this logger.</summary>
        public const string FriendlyName = "github";

        /// <summary>Default Name of the Check Run/Job</summary>
        private const string DEFAULT_CHECKRUNNAME = "test-report";

        /// <summary>All accumulated variables</summary>
        private IDictionary<string, string> Vars = new Dictionary<string, string>();

        /// <summary>
        /// The owner of the repository.
        /// eg. "dogguts" (when GITHUB_REPOSITORY="dogguts/sandbox")
        ///  </summary>
        private string GITHUB_REPOSITORY_OWNER = string.Empty;
        /// <summary>
        /// The name of the repository.
        /// eg. "sandbox" (when GITHUB_REPOSITORY="dogguts/sandbox")
        /// </summary>
        private string GITHUB_REPOSITORY_NAME = string.Empty;
        /// <summary>The secret GITHUB_TOKEN used to authenticate in this workflow run</summary>
        private string GITHUB_TOKEN = string.Empty;
        /// <summary>
        /// The commit SHA that triggered the workflow.
        /// eg. "8e1de4c0ed3e418caf141a32e57dce4a70e99b8b"
        /// </summary>
        private string GITHUB_SHA = string.Empty;
        /// <summary>
        /// The GitHub workspace directory path. The workspace directory contains a subdirectory with a copy of your repository if your workflow uses the actions/checkout action
        /// eg. "/home/runner/work/sandbox/sandbox"
        /// </summary>
        private string GITHUB_WORKSPACE = string.Empty;

        /// <summary>Name of the Check Run/Job</summary>
        private string GHL_CHECKRUN_NAME = DEFAULT_CHECKRUNNAME;

        private CheckRun? CurrentCheckRun = null;

#if ENABLE_GH_API
        private IGitHubClient? _gitHubClient = null;
        private IGitHubClient GitHubClient {
            get => _gitHubClient ?? throw new NullReferenceException($"{nameof(GitHubClient)} not initialized");
            set => _gitHubClient = value;
        }
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
            var stackLine = stackTrace.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()).First();
            var regex = new System.Text.RegularExpressions.Regex(@"(\ *)at (?<method>.*) in (?<filename>.*):line (?<linenumber>\d+)");
            var match = regex.Match(stackTrace);
            return (match.Groups["filename"].Value, match.Groups["method"].Value, match.Groups["linenumber"].Value);
        }

        private string WorkspaceRelativePath(string fullPath) {
            if (!string.IsNullOrEmpty(GITHUB_WORKSPACE)) {
                return fullPath.Replace(GITHUB_WORKSPACE, "").TrimStart('\\', '/');
            } else {
                return fullPath;
            }
        }

        private static string FormatTimeSpan(TimeSpan timeSpan) {
            if (timeSpan.TotalDays >= 1) {
                return string.Format("{0:0.0000} {1}", timeSpan.TotalDays, "Days");
            } else if (timeSpan.TotalHours >= 1) {
                return string.Format("{0:0.0000} {1}", timeSpan.TotalHours, "Hours");
            } else if (timeSpan.TotalMinutes >= 1) {
                return string.Format("{0:0.0000} {1}", timeSpan.TotalMinutes, "Minutes");
            } else {
                return string.Format("{0:0.0000} {1}", timeSpan.TotalSeconds, "Seconds");
            }
        }

        private string GetFullInformation(TestResult result) {
            var Output = new System.Text.StringBuilder();

            if (!String.IsNullOrEmpty(result.ErrorMessage)) {
                Output.AppendLine("== Error Message ==");
                Output.AppendLine(result.ErrorMessage);
            }

            if (!String.IsNullOrEmpty(result.ErrorStackTrace)) {
                Output.AppendLine("== Stack Trace ==");
                Output.AppendLine(result.ErrorStackTrace);
            }

            var stdOutMessagesList = result.Messages.Where(msg => msg.Category.Equals(TestResultMessage.StandardOutCategory, StringComparison.OrdinalIgnoreCase)).ToList();
            if (stdOutMessagesList.Count > 0) {
                Output.AppendLine("== Standard Output Messages ==");
                foreach (TestResultMessage message in stdOutMessagesList) {
                    Output.AppendLine(message.Text);
                }
            }

            var stdErrMessagesList = result.Messages.Where(msg => msg.Category.Equals(TestResultMessage.StandardErrorCategory, StringComparison.OrdinalIgnoreCase)).ToList();
            if (stdErrMessagesList.Count > 0) {
                Output.AppendLine("== Standard Error Messages ==");
                foreach (TestResultMessage message in stdErrMessagesList) {
                    Output.AppendLine(message.Text);
                }
            }

            var dbgTrcMessagesList = result.Messages.Where(msg => msg.Category.Equals(TestResultMessage.DebugTraceCategory, StringComparison.OrdinalIgnoreCase)).ToList();
            if (dbgTrcMessagesList.Count > 0) {
                Output.AppendLine("== Debug Trace Messages ==");
                foreach (TestResultMessage message in dbgTrcMessagesList) {
                    Output.AppendLine(message.Text);
                }
            }

            var addnlInfoMessagesList = result.Messages.Where(msg => msg.Category.Equals(TestResultMessage.AdditionalInfoCategory, StringComparison.OrdinalIgnoreCase)).ToList();
            if (addnlInfoMessagesList.Count > 0) {
                Output.AppendLine("== Additional Information Messages ==");
                foreach (TestResultMessage message in addnlInfoMessagesList) {
                    Output.AppendLine(message.Text);
                }
            }
            return Output.ToString();
        }

        public void Initialize(TestLoggerEvents events, string testRunDirectory) {
            Initialize(events, new Dictionary<string, string>() { { "TestRunDirectory", testRunDirectory } });
        }

        public void Initialize(TestLoggerEvents events, Dictionary<string, string> parameters) {

            var environmentVariables = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process).ToDictionary<string, string>();

            // Build Vars, parameters take precedence over environment variables
            Vars = BuildVariables(parameters, environmentVariables);

            // Keep some frequently used Vars local

            // name/GHL_CHECKRUN_NAME gets special treatment; parameter="name", env="GHL_CHECKRUN_NAME", default value as defined in const DEFAULT_CHECKRUNNAME
            if (parameters.Keys.Contains("name", StringComparer.InvariantCultureIgnoreCase)) {
                GHL_CHECKRUN_NAME = parameters["name"];
            } else if (Vars.ContainsKey("GHL_CHECKRUN_NAME")) {
                GHL_CHECKRUN_NAME = Vars["GHL_CHECKRUN_NAME"];
            }

            GITHUB_REPOSITORY_OWNER = Vars.GetValueOrDefault("GITHUB_REPOSITORY_OWNER", string.Empty);
            GITHUB_REPOSITORY_NAME = Vars.GetValueOrDefault("GITHUB_REPOSITORY", string.Empty).Split('/').Last();
            GITHUB_TOKEN = Vars.GetValueOrDefault("GITHUB_TOKEN", string.Empty);
            GITHUB_SHA = Vars.GetValueOrDefault("GITHUB_SHA", string.Empty);
            GITHUB_WORKSPACE = Vars.GetValueOrDefault("GITHUB_WORKSPACE", String.Empty);

#if ENABLE_GH_API
            // connection to github api 
            GitHubClient = new GitHubClient(new ProductHeaderValue(GITHUB_REPOSITORY_OWNER)) {
                Credentials = new Credentials(GITHUB_TOKEN)
            };
#endif
            // Raised when a test run starts.
            events.TestRunStart += Events_TestRunStart;

            // Raised when a test result is received.
            events.TestResult += Events_TestResult;

            // Raised when a test run is complete.
            events.TestRunComplete += Events_TestRunComplete;
        }

        /// <summary>Raised when a test run starts.</summary> 
        private void Events_TestRunStart(object sender, TestRunStartEventArgs e) {
            // Create new check run, keep in CurrentCheckRun
            var newCheckRun = new NewCheckRun(GHL_CHECKRUN_NAME, GITHUB_SHA) {
                Output = new NewCheckRunOutput(GHL_CHECKRUN_NAME, "Starting..."),
                Status = CheckStatus.Queued,
            };
#if ENABLE_GH_API
            CurrentCheckRun = GitHubClient.Check.Run.Create(GITHUB_REPOSITORY_OWNER, GITHUB_REPOSITORY_NAME, newCheckRun).Result;
#endif
        }

        /// <summary>Raised when a test result is received.</summary>
        private void Events_TestResult(object sender, TestResultEventArgs e) {
            if (e.Result.Outcome == TestOutcome.Passed || e.Result.Outcome == TestOutcome.None) {
                //don't annotate successfull tests
                return;
            }

            // Update the CurrentCheckRun with a new Annotation of the test failure (also set Status=InProgress)
            NewCheckRunAnnotation? newAnnotation = null;
            switch (e.Result.Outcome) {
                case TestOutcome.Failed:
                    var (filename, methodname, sourceline) = StackFrameFromTrace(e.Result.ErrorStackTrace);
                    newAnnotation = new NewCheckRunAnnotation(WorkspaceRelativePath(filename), int.Parse(sourceline), int.Parse(sourceline) + 5, CheckAnnotationLevel.Failure, e.Result.ErrorMessage) {
                        Title = e.Result.DisplayName,
                        RawDetails = GetFullInformation(e.Result)
                    };
                    break;
                case TestOutcome.Skipped:
                    //TODO: (low) CheckAnnotationLevel.Warning ?
                    break;
                case TestOutcome.NotFound:
                    // TODO: (low) CheckAnnotationLevel.Failure ? (this can actually happen!?)
                    break;
            }
            if (newAnnotation != null) {
                var check = new CheckRunUpdate() {
                    Output = new NewCheckRunOutput(GHL_CHECKRUN_NAME, "Running...") {
                        Annotations = new List<NewCheckRunAnnotation>() { newAnnotation }
                    },
                    Status = CheckStatus.InProgress
                };
#if ENABLE_GH_API
                CurrentCheckRun = GitHubClient.Check.Run.Update(GITHUB_REPOSITORY_OWNER, GITHUB_REPOSITORY_NAME, CurrentCheckRun?.Id ?? -1, check).Result;
#endif
            }
        }

        /// <summary>Raised when a test run is complete.</summary>
        private void Events_TestRunComplete(object sender, TestRunCompleteEventArgs e) {
            // Update the CurrentCheckRun with a test summary, set the test run conclusion and Status=Completed
            CheckConclusion gh_conclusion = CheckConclusion.Neutral;

            var stringBuilder = new StringBuilder();
            if (e.TestRunStatistics.Stats.Any(stat => stat.Key == TestOutcome.Failed && stat.Value > 0)) {
                // failed
                gh_conclusion = CheckConclusion.Failure;
                stringBuilder.AppendLine(":red_circle: Test Run Failed.");
            } else {
                // passed
                gh_conclusion = CheckConclusion.Success;
                stringBuilder.AppendLine(":green_circle: Test Run Successful.");
            }
            if (e.IsAborted || e.IsCanceled) {
                gh_conclusion = CheckConclusion.Cancelled;
            }

            stringBuilder.AppendLine($"Total tests: {e.TestRunStatistics.ExecutedTests}");

            foreach (var stat in e.TestRunStatistics.Stats) {
                stringBuilder.AppendLine(" - " + stat.Key switch
                {
                    TestOutcome.None => ":question:",
                    TestOutcome.Passed => ":heavy_check_mark:",
                    TestOutcome.Failed => ":heavy_multiplication_x:",
                    TestOutcome.Skipped => ":large_orange_diamond:",
                    TestOutcome.NotFound => ":skull_and_crossbones:",
                    _ => ":skull:"
                } + " " + $"{stat.Key}: {stat.Value}");
            }
            stringBuilder.AppendLine();
            stringBuilder.AppendLine($"Total time: {FormatTimeSpan(e.ElapsedTimeInRunningTests)}");

            var check = new CheckRunUpdate() {
                Output = new NewCheckRunOutput(GHL_CHECKRUN_NAME, stringBuilder.ToString()),
                Status = CheckStatus.Completed,
                Conclusion = gh_conclusion
            };

#if ENABLE_GH_API
            Console.WriteLine("*** Events_TestRunComplete *** Check.Run.Update");
            try {
                CurrentCheckRun = GitHubClient.Check.Run.Update(GITHUB_REPOSITORY_OWNER, GITHUB_REPOSITORY_NAME, CurrentCheckRun?.Id ?? -1, check).Result;
            } catch (System.Exception ex) {
                Console.WriteLine("*** Events_TestRunComplete *** <Exception>");
                Console.WriteLine(ex.ToString());
                Console.WriteLine("*** Events_TestRunComplete *** </Exception>");
            }
            Console.WriteLine("*** Events_TestRunComplete *** Check.Run.Update/done");
#endif
        }

    }
}
