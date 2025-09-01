using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using DotNetEnv;

namespace TeamsAutomation
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Env.Load();
            var userDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "User Data");
            var profileDirs = Directory.GetDirectories(userDataDir, "Profile *")
                .Select(p => new { Path = p, Name = GetProfileName(p) })
                .ToList();
            profileDirs.Insert(0, new { Path = Path.Combine(userDataDir, "Default"), Name = "Default" });

            Console.WriteLine("Please select a profile to use:");
            for (int i = 0; i < profileDirs.Count; i++)
            {
                Console.WriteLine($"[{i}] {profileDirs[i].Name}");
            }

            int selectedProfileIndex = -1;
            while (selectedProfileIndex < 0 || selectedProfileIndex >= profileDirs.Count)
            {
                Console.Write("Enter the number of the profile: ");
                if (!int.TryParse(Console.ReadLine(), out selectedProfileIndex))
                {
                    selectedProfileIndex = -1;
                }
            }

            string profilePath = profileDirs[selectedProfileIndex].Path;

            Console.WriteLine($"Using profile: {profileDirs[selectedProfileIndex].Name}");

            using var playwright = await Playwright.CreateAsync();
            var browserContext = await playwright.Chromium.LaunchPersistentContextAsync(profilePath, new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = false,
            });

            var page = browserContext.Pages.FirstOrDefault() ?? await browserContext.NewPageAsync();
            var startUrl = Environment.GetEnvironmentVariable("START_URL") ?? "https://example.com";
            await page.GotoAsync(startUrl); // Replace with the desired URL

            Console.WriteLine("Please log in to the application in the browser if needed.");
            Console.WriteLine("Press any key in this window to continue once you are logged in...");
            Console.ReadKey();

            var channelName = Environment.GetEnvironmentVariable("ChannelName");
            if (string.IsNullOrEmpty(channelName))
            {
                Console.WriteLine("ChannelName not found in .env file. Please add it.");
                return;
            }

            var emailsFileLocation = Environment.GetEnvironmentVariable("EmailsFileLocation");
            if (string.IsNullOrEmpty(emailsFileLocation) || !File.Exists(emailsFileLocation))
            {
                Console.WriteLine("EmailsFileLocation not found or file does not exist. Please check your .env file.");
                return;
            }

            // Find the channel by its exact text and hover over it to reveal more options
            var channelLocator = page.Locator($"div[role='treeitem']:has-text('{channelName}')").First;
            Console.WriteLine($"Looking for channel: {channelName}");
            await channelLocator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 30000 });
            await channelLocator.ScrollIntoViewIfNeededAsync();
            await channelLocator.HoverAsync();

            // Click on the 'More options' button (...)
            var moreOptionsButton = channelLocator.Locator("button[aria-label*='More options']").First;
            await moreOptionsButton.ClickAsync(new() { Force = true });

            // Hover over 'Share channel' to open the sub-menu
            await page.Locator("div[role='menuitem']:has-text('Share channel')").HoverAsync();

            // Click on 'with people'
            await page.Locator("div[role='menuitem']:has-text('With people')").ClickAsync();

            // Wait for the share dialog to appear and add emails
            var shareInputSelector = "input[placeholder*='Type a name, group or channel']";
            await page.WaitForSelectorAsync(shareInputSelector);

            var emails = await File.ReadAllLinesAsync(emailsFileLocation);
            foreach (var email in emails)
            {
                if (!string.IsNullOrWhiteSpace(email))
                {
                    Console.WriteLine($"Adding [{email}]");
                    await page.FillAsync(shareInputSelector, email);
                    await page.Keyboard.PressAsync("Enter");
                    await Task.Delay(1000); // Wait a moment for the entry to be added
                }
            }

            // After adding all emails, you might want to click a 'Share' or 'Done' button
            // For example: await page.ClickAsync("button:has-text('Share')");

            Console.WriteLine("Press any key to close the browser...");
            Console.ReadKey();
            await browserContext.CloseAsync();
        }

        static string GetProfileName(string profilePath)
        {
            var preferencesPath = Path.Combine(profilePath, "Preferences");
            if (!File.Exists(preferencesPath))
            {
                return Path.GetFileName(profilePath);
            }

            try
            {
                var json = File.ReadAllText(preferencesPath);
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("profile", out JsonElement profileElement))
                    {
                        if (profileElement.TryGetProperty("name", out JsonElement nameElement))
                        {
                            return nameElement.GetString() ?? Path.GetFileName(profilePath);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Fallback to directory name in case of any error
                return Path.GetFileName(profilePath);
            }

            return Path.GetFileName(profilePath);
        }
    }
}