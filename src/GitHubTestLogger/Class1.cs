using System;
using System.Collections.Generic;
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
        public readonly string GITHUB_TOKEN = "";
        public void Initialize(TestLoggerEvents events, Dictionary<string, string> parameters) {
            //GITHUB_TOKEN
            Console.WriteLine("Initialize1-w");

            foreach (var p in parameters) {
                Console.WriteLine("param: " + p.Key + "=" + p.Value);
            }
            //System.Diagnostics.Debugger.Launch();
            events.TestRunMessage += Events_TestRunMessage;
            events.TestResult += this.TestResultHandler;
            events.TestRunComplete += Events_TestRunComplete;
            events.TestRunStart += Events_TestRunStart;

            events.DiscoveredTests += Events_DiscoveredTests;

            var env = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process);
            var GITHUB_REPOSITORY_OWNER = env["GITHUB_REPOSITORY_OWNER"].ToString();

            var github = new GitHubClient(new ProductHeaderValue(GITHUB_REPOSITORY_OWNER)) {
                Credentials = new Credentials(parameters["GITHUB_TOKEN"]) //"a566f1e921b2af225370d55ee114e53d61394e58"),// NOTE: !! real token
            };
            var user = github.User.Get("dogguts").Result;
            Console.WriteLine("/// GotUser:" + user.Name);
            Console.WriteLine("env.?GotTOKEN?:" + env["GITHUB_TOKEN"].ToString());
 
        }

        private void Events_DiscoveredTests(object sender, DiscoveredTestsEventArgs e) {
            throw new NotImplementedException();
        }

        private void Events_TestRunStart(object sender, TestRunStartEventArgs e) {
            //nouse
            // throw new NotImplementedException();
         
        }

        private void Events_TestRunComplete(object sender, TestRunCompleteEventArgs e) {
            //  throw new NotImplementedException();
        }

        private void Events_TestRunMessage(object sender, TestRunMessageEventArgs e) {
            // throw new NotImplementedException();
        }

        public void Initialize(TestLoggerEvents events, string testRunDirectory) {
            Console.WriteLine("Initialize2-x");
            //throw new NotImplementedException();
        }



        private void TestResultHandler(object sender, TestResultEventArgs e) {

            Console.WriteLine(e.Result.TestCase.FullyQualifiedName + "+++ " + e.Result.Outcome.ToString());
            //Console.WriteLine(e.Result.TestCase.FullyQualifiedName);
            //Console.WriteLine(e.Result.TestCase.Source);
            //Console.WriteLine(e.Result.TestCase.LineNumber);
        }


    }
}
