using System.Globalization;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using HtmlAgilityPack.CssSelectors.NetCore;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace Bouvet.Lunsjbotten
{
    public class PostMenu
    {
        private readonly ILogger _logger;
        private static readonly string LUNSJBOXEN_URL = "https://lunsjboxen.no/ukens-meny/";
        private static readonly string SLACK_HOOK_URL = Environment.GetEnvironmentVariable("SLACK_HOOK", EnvironmentVariableTarget.Process);
        private static readonly string OPENAI_SECRET = Environment.GetEnvironmentVariable("OPENAI_SECRET", EnvironmentVariableTarget.Process);

        public PostMenu(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<PostMenu>();
        }

        [Function("PostMenu")]
        public async Task Run([TimerTrigger("0 45 7 * * 1-5")] TimerInfo myTimer)
        {
            _logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            var ukesmenyer = ParseOnlineMenu().ToList();
            var dagensIndeks = ukesmenyer.FindIndex(u => u.date.Date == DateTime.Now.Date);

            var dagens = ukesmenyer[dagensIndeks];
            var morgendagens = ukesmenyer[dagensIndeks + 1];

            if (dagens == null)
            {
                _logger.LogError("No menu found for todays date!");
                return;
            }

            var bedreTekst = ImproveMenuText(dagens.Text) ?? dagens.Text;
            var morgenTekst = Summary(morgendagens.Text) ?? morgendagens.Text;

            var slackMessage = new SlackMessage(bedreTekst, dagens.Allergens, morgenTekst);

            await PostToSlack(slackMessage);
            
            if (myTimer.ScheduleStatus is not null)
            {
                _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");
            }
        }

        private string? ImproveMenuText(string input) {
            var aiClient = new ChatClient("gpt-4o-mini", OPENAI_SECRET);
            var aiText = aiClient.CompleteChat($"Forbedre teksten og returner kun resultatet: {input}");

            return aiText.Value.Content.FirstOrDefault()?.Text;
        }

        private string? Summary(string input) {
            var aiClient = new ChatClient("gpt-4o-mini", OPENAI_SECRET);
            var aiText = aiClient.CompleteChat($"Lag en oppsummering på 3-5 ord og returner kun resultatet: {input}");

            return aiText.Value.Content.FirstOrDefault()?.Text;
        }

        private IEnumerable<Menu> ParseOnlineMenu() {
            var menus = new List<Menu>();
            var web = new HtmlWeb();
            var document = web.Load(LUNSJBOXEN_URL);

            return document.DocumentNode
                .QuerySelectorAll(".ukesmenyer")
                .SelectMany(ExtractMenu);
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

        private async Task PostToSlack(SlackMessage message) {
            var client = new HttpClient();
            var result = await client.PostAsJsonAsync(SLACK_HOOK_URL, message);
            _logger.LogInformation("Call to slack api returned {statuscode}", result.StatusCode);
        }
    }

    record Menu(DateTime date, string Day, string Text, string Allergens);
    record AiMenuContainer(IEnumerable<AiMenu> data);
    record AiMenu(DateTime dato, string tekst);
    record SlackMessage(string meny, string allergener, string nestemeny);
}
