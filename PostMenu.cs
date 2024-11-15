using System.Globalization;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using HtmlAgilityPack.CssSelectors.NetCore;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace Bouvet.Lunsjbotten;

public class PostMenu
{
    private readonly ILogger _logger;
    private static readonly string LUNSJBOXEN_URL = "https://lunsjboxen.no/ukens-meny/";
    private static readonly string SLACK_HOOK_URL = Environment.GetEnvironmentVariable("SLACK_HOOK", EnvironmentVariableTarget.Process);
    private static readonly string OPENAI_SECRET = Environment.GetEnvironmentVariable("OPENAI_SECRET", EnvironmentVariableTarget.Process);

    private static readonly string[] DAGER = new string[] { "Mandag", "Tirsdag", "Onsdag", "Torsdag", "Fredag" };

    public PostMenu(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<PostMenu>();
    }

    [Function("PostMenu")]
    public async Task Run([TimerTrigger("0 45 7 * * 1-5")] TimerInfo myTimer)
    {
        _logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

        var ukesmenyer = ParseOnlineMenu().OrderBy(q => q.date).ToList();
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
        var haikuTekst = Haiku(dagens.Text) ?? "Ingen haiku i dag";

        var dag = morgendagens.date.DayOfWeek == DayOfWeek.Monday
            ? "Mandagens"
            : "Morgendagens";

        var slackMessage = new SlackMessage(bedreTekst, dagens.Allergens, morgenTekst, haikuTekst, dag);

        await PostToSlack(slackMessage);

    }

    private string? ImproveMenuText(string input)
    {
        var aiClient = new ChatClient("gpt-4o-mini", OPENAI_SECRET);
        var aiText = aiClient.CompleteChat($"Forbedre teksten og returner kun resultatet: {input}");

        return aiText.Value.Content.FirstOrDefault()?.Text;
    }

    private string? Summary(string input)
    {
        var aiClient = new ChatClient("gpt-4o-mini", OPENAI_SECRET);
        var aiText = aiClient.CompleteChat($"Lag en oppsummering på 3-5 ord og returner kun resultatet: {input}");

        return aiText.Value.Content.FirstOrDefault()?.Text;
    }

    private string? Haiku(string input)
    {
        var aiClient = new ChatClient("gpt-4o-mini", OPENAI_SECRET);
        var aiText = aiClient.CompleteChat($"Lag en haiku om hovedingrediensen i følgende meny og returner kun resultatet: {input}");

        return aiText.Value.Content.FirstOrDefault()?.Text;
    }

    private IEnumerable<Menu> ParseOnlineMenu()
    {
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

        returnable.AddRange(menuItems
            .Where(mi => DAGER.Contains(mi.InnerText))
            .Select(mi =>
            {
                var idx = menuItems.IndexOf(mi);
                return new Menu(
                    mi.InnerText switch
                    {
                        "Mandag" => ISOWeek.ToDateTime(DateTime.Now.Year, weekNumber, DayOfWeek.Monday),
                        "Tirsdag" => ISOWeek.ToDateTime(DateTime.Now.Year, weekNumber, DayOfWeek.Tuesday),
                        "Onsdag" => ISOWeek.ToDateTime(DateTime.Now.Year, weekNumber, DayOfWeek.Wednesday),
                        "Torsdag" => ISOWeek.ToDateTime(DateTime.Now.Year, weekNumber, DayOfWeek.Thursday),
                        "Fredag" => ISOWeek.ToDateTime(DateTime.Now.Year, weekNumber, DayOfWeek.Friday),
                        _ => ISOWeek.ToDateTime(DateTime.Now.Year, weekNumber, DayOfWeek.Monday) // Copout...
                    },
                    mi.InnerText,
                    menuItems.Count > idx + 1 ? menuItems[idx + 1].InnerText : "Meny mangler.",
                    menuItems.Count > idx + 2 ? menuItems[idx + 2].InnerText : "Allergener mangler.");
            }).ToList());

        return returnable;
    }

    private async Task PostToSlack(SlackMessage message)
    {
        var client = new HttpClient();
        var result = await client.PostAsJsonAsync(SLACK_HOOK_URL, message);
        _logger.LogInformation("Call to slack api returned {statuscode}", result.StatusCode);
    }
}

record Menu(DateTime date, string Day, string Text, string Allergens);
record AiMenuContainer(IEnumerable<AiMenu> data);
record AiMenu(DateTime dato, string tekst);
record SlackMessage(string meny, string allergener, string nestemeny, string haiku, string dag);