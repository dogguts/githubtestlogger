using Octokit;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ConsoleTest {
    class Program {
        public static void Main() {
            MainAsync().GetAwaiter().GetResult();
        }

        static async Task MainAsync() {
            Console.WriteLine("Hello World!");
        }
    }
}
