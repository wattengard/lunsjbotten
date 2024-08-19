using System;
using System.Net.Http.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Bouvet.Lunsjbotten
{
    public class PostMenu
    {
        private readonly ILogger _logger;
        private static readonly string LUNSJBOXEN_URL = "https://lunsjboxen.no/ukens-meny/";
        private static readonly string SLACK_HOOK_URL = Environment.GetEnvironmentVariable("SLACK_HOOK", EnvironmentVariableTarget.Process);
        private static readonly string SLACK_TEST_HOOK_URL = "https://hooks.slack.com/triggers/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";
        private static readonly string OPENAI_SECRET = Environment.GetEnvironmentVariable("OPENAI_SECRET", EnvironmentVariableTarget.Process);

        public PostMenu(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<PostMenu>();
        }

        [Function("PostMenu")]
        public async void Run([TimerTrigger("0 */5 * * * *")] TimerInfo myTimer)
        {
            var testMessage = new SlackMessage("Testing Lunsjbotten message", "C0668DHHEHG");

            _logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            var client = new HttpClient();
            var result = await client.PostAsJsonAsync(SLACK_TEST_HOOK_URL, testMessage);

            Console.WriteLine(result.StatusCode);
            
            if (myTimer.ScheduleStatus is not null)
            {
                _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");
            }
        }

        IEnumerable<Menu> ExtractMenu(HtmlNode menuNode)
        {
            var returnable = new List<Menu>();

            var menuItems = menuNode.QuerySelectorAll(".elementor-widget-container");
            var weekNumber = int.Parse(Regex.Match(menuItems.First().InnerText, @"\d\d").Value);

            for (int i = 0; i < 5; i++)
            {
                var weekday = i switch
                {
                    0 => DayOfWeek.Monday,
                    1 => DayOfWeek.Tuesday,
                    2 => DayOfWeek.Wednesday,
                    3 => DayOfWeek.Thursday,
                    4 => DayOfWeek.Friday,
                    _ => DayOfWeek.Monday
                };

                var date = ISOWeek.ToDateTime(DateTime.Now.Year, weekNumber, weekday);
                var dayNumber = (i * 3) + 1;
                var day = menuItems[dayNumber].InnerText;
                var text = menuItems[dayNumber + 1].InnerText;
                var allerg = menuItems[dayNumber + 2].InnerText;

                returnable.Add(new Menu(date, day, text, allerg));
            }

            return returnable;
        }
    }

    record Menu(DateTime date, string Day, string Text, string Allergens);
    record AiMenuContainer(IEnumerable<AiMenu> data);
    record AiMenu(DateTime dato, string tekst);
    record SlackMessage(string message, string targetChannel);
}
