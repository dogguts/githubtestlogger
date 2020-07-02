using Octokit;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ConsoleTest {
  

    class Program {
        public const string ErrorStackTrace = "   at xUnitTest.UnitTest1.MoreStackDepth() in D:\\Dropbox\\Projects\\GitHubTestLogger\\src\\xUnitTest\\UnitTest1.cs:line 14\r\n   at xUnitTest.UnitTest1.FactTestFailure() in D:\\Dropbox\\Projects\\GitHubTestLogger\\src\\xUnitTest\\UnitTest1.cs:line 19";
        public static void Main() {
            MainAsync().GetAwaiter().GetResult();
        }

        static async Task MainAsync() {
            /* // get source from stacktrace 
            var stackLine = ErrorStackTrace.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()).First();
            var regex = new System.Text.RegularExpressions.Regex(@"(\ *)at (?<method>.*) in (?<filename>.*):line (?<linenumber>\d+)");
            var match = regex.Match(ErrorStackTrace);
            string sourceMethod = match.Groups["method"].Value;
            string sourceFile = match.Groups["filename"].Value;
            string sourceLine = match.Groups["linenumber"].Value;
            */
            var env = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process);
            var GITHUB_REPOSITORY_OWNER = env["GITHUB_REPOSITORY_OWNER"].ToString();

            var github = new GitHubClient(new ProductHeaderValue(GITHUB_REPOSITORY_OWNER)) {
                Credentials = new Credentials("a566f1e921b2af225370d55ee114e53d61394e58"),// NOTE: !! real token
            };
            var user = await github.User.Get("dogguts");
            //  var ghappauth  = await github.GitHubApps.GetCurrent();


            //  var check  = await github.Check.Run.g

            //var check = new NewCheckRun("test-report", "330c56287f9cc82273c39dfe6b99959879357b33") {
            //    Output = new NewCheckRunOutput("NewCheckRunOutput.Title", "NewCheckRunOutput.Summary") {
            //        Text = "NewCheckRunOutput.Text",
            //    },
            //    Status = CheckStatus.InProgress,
            //    //Conclusion = CheckConclusion.Neutral , 
            //};
            //var checkRun = await github.Check.Run.Create("dogguts", "sandbox", check);

            //var repositories = await github.Repository.GetAllForCurrent();
            //var user = await github.User.Get("dogguts");
            //Console.WriteLine(user.Followers + " folks love the half ogre!");

            Console.WriteLine("Hello World!");
        }
    }
}
