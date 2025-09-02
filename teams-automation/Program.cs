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

            Console.WriteLine("Please select a fprofile to use:");
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

            // Error handling for browser launch and navigation
            using var playwright = await Playwright.CreateAsync();
            IBrowserContext? browserContext = null;
            IPage? page = null;
            try
            {
                browserContext = await playwright.Chromium.LaunchPersistentContextAsync(profilePath, new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = false,
                    Channel = "msedge", // Use Microsoft Edge browser
                });
            }
            catch (PlaywrightException ex)
            {
                Console.WriteLine($"Playwright browser launch error: {ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"General error during browser launch: {ex.Message}");
                return;
            }

            try
            {
                page = browserContext.Pages.FirstOrDefault() ?? await browserContext.NewPageAsync();
                var startUrl = Environment.GetEnvironmentVariable("START_URL") ?? "https://example.com";
                await page.GotoAsync(startUrl); // Replace with the desired URL
                Console.WriteLine($"Navigated to {startUrl}");
            }
            catch (PlaywrightException ex)
            {
                Console.WriteLine($"Playwright navigation error: {ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"General error during navigation: {ex.Message}");
                return;
            }

            Console.WriteLine("Please log in to the application in the browser if needed.");
            Console.WriteLine("Press any key in this window to continue once you are logged in...");
            Console.Read();

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
            Console.WriteLine($"Looking for channel: {channelName}");
            ILocator? channelLocator = null;
            bool channelFound = false;
            int scrollAttempts = 0;

            while (!channelFound && scrollAttempts < 10)
            {
                // Look for the exact channel name - use a more specific selector
                var allChannels = page.Locator("div[role='treeitem']");
                var channelCount = await allChannels.CountAsync();

                Console.WriteLine($"Found {channelCount} channels in the sidebar");

                // Find the exact match for the channel name
                for (int i = 0; i < channelCount; i++)
                {
                    var channel = allChannels.Nth(i);
                    var channelText = await channel.InnerTextAsync();

                    if (channelText.Trim().Equals(channelName, StringComparison.OrdinalIgnoreCase))
                    {
                        channelLocator = channel;
                        channelFound = true;
                        Console.WriteLine($"Channel '{channelName}' found at position {i}. Text: '{channelText.Trim()}'");

                        // Scroll the found channel into view and make it visible
                        await channelLocator.ScrollIntoViewIfNeededAsync();
                        await Task.Delay(500); // Wait for scroll to complete

                        // Verify it's actually visible in the viewport
                        var isVisible = await channelLocator.IsVisibleAsync();
                        Console.WriteLine($"Channel is visible in viewport: {isVisible}");

                        if (isVisible)
                        {
                            await channelLocator.HoverAsync();
                            Console.WriteLine($"Hovered over channel '{channelName}'");
                        }
                        break;
                    }
                }

                if (!channelFound)
                {
                    scrollAttempts++;
                    Console.WriteLine($"Channel not found, scrolling attempt {scrollAttempts}...");
                    // Try to scroll the sidebar to load more channels
                    var sidebar = page.Locator("div[role='tree']").First;
                    await sidebar.EvaluateAsync("el => el.scrollBy(0, 300)");
                    await Task.Delay(1000); // Wait for new channels to load
                }
            }

            if (!channelFound || channelLocator == null)
            {
                Console.WriteLine($"Channel '{channelName}' could not be found after scrolling.");
                return;
            }

            // Click on the 'More options' button (...)
            await Task.Delay(500); // Wait for UI to update after hover
            var moreOptionsButton = channelLocator.Locator("button[aria-label*='More options'], button[title*='More options']").First;
            try
            {
                await moreOptionsButton.ClickAsync(new() { Force = true });
                Console.WriteLine("More options button clicked");
            }
            catch (PlaywrightException ex)
            {
                Console.WriteLine($"Could not click More options: {ex.Message}");
                return;
            }

            // Enumerate and log all visible menu items before clicking 'Share channel'
            var menuItems = page.Locator("div[role='menuitem'], div[role='button'][data-testid*='menu-item']");
            var menuItemCount = await menuItems.CountAsync();
            Console.WriteLine($"Found {menuItemCount} menu items:");

            for (int i = 0; i < menuItemCount; i++)
            {
                var menuItem = menuItems.Nth(i);
                var menuItemText = await menuItem.InnerTextAsync();
                var isVisible = await menuItem.IsVisibleAsync();
                var testId = await menuItem.GetAttributeAsync("data-testid");
                Console.WriteLine($"Menu item {i}: '{menuItemText}' (Visible: {isVisible}, TestId: {testId})");
            }

            // Use the correct selector for 'Share channel' based on the actual HTML structure
            var shareChannelMenuItemLocator = page.Locator("div[data-testid='channel-share-with-options-menu-item']");
            bool shareChannelClicked = false;
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    await shareChannelMenuItemLocator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3000 });
                    if (await shareChannelMenuItemLocator.IsVisibleAsync())
                    {
                        // Hover first, then click to reveal submenu
                        await shareChannelMenuItemLocator.HoverAsync();
                        await Task.Delay(500); // Wait for submenu to appear
                        Console.WriteLine($"Hovered over 'Share channel' (attempt {attempt})");
                        shareChannelClicked = true;
                        break;
                    }
                    else
                    {
                        Console.WriteLine($"Attempt {attempt}: 'Share channel' is not visible.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Attempt {attempt}: Could not hover 'Share channel': {ex.Message}");
                    await Task.Delay(1000);
                }
            }

            if (!shareChannelClicked)
            {
                Console.WriteLine("Failed to hover over 'Share channel' after multiple attempts.");
                return;
            }

            // Wait for the submenu to appear and click 'With people'
            var withPeopleMenuItem = page.Locator("div[role='menuitem']:has-text('With people')");
            bool withPeopleClicked = false;
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    await withPeopleMenuItem.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3000 });
                    await withPeopleMenuItem.ClickAsync();
                    Console.WriteLine($"Clicked 'With people' (attempt {attempt})");
                    withPeopleClicked = true;
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Attempt {attempt}: Could not click 'With people': {ex.Message}");
                    await Task.Delay(1000);
                }
            }
            if (!withPeopleClicked)
            {
                Console.WriteLine("Failed to click 'With people' after multiple attempts.");
                return;
            }

            // Wait for the share dialog to appear and add emails
            var shareInputSelector = "input#people-picker-input";
            var fallbackSelector = "input[aria-label='Type a name or email']";
            ILocator? inputBox = null;
            try
            {
                await page.WaitForSelectorAsync(shareInputSelector, new() { Timeout = 5000 });
                inputBox = page.Locator(shareInputSelector);
                Console.WriteLine("Found input using #people-picker-input");
            }
            catch
            {
                try
                {
                    await page.WaitForSelectorAsync(fallbackSelector, new() { Timeout = 5000 });
                    inputBox = page.Locator(fallbackSelector);
                    Console.WriteLine("Found input using aria-label selector");
                }
                catch
                {
                    Console.WriteLine("Could not find the email input box.");
                    return;
                }
            }

            var emails = (await File.ReadAllLinesAsync(emailsFileLocation))
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct()
                .ToList();

            foreach (var email in emails)
            {
                if (!string.IsNullOrWhiteSpace(email))
                {
                    Console.WriteLine($"Adding [{email}]");
                    
                    // Clear the input field first
                    await inputBox.ClickAsync();
                    await inputBox.FillAsync(""); // Clear any existing content
                    
                    // Type the email address
                    await inputBox.TypeAsync(email, new() { Delay = 50 }); // Add slight delay between keystrokes
                    
                    // Wait for suggestions to appear and then press Enter
                    await Task.Delay(1500); // Wait for autocomplete/suggestions to appear
                    await page.Keyboard.PressAsync("Enter");
                    
                    // Wait for the email to be processed and added to the list
                    await Task.Delay(3000);
                    
                    // Verify the email was added by checking if input is cleared
                    var currentValue = await inputBox.InputValueAsync();
                    if (!string.IsNullOrEmpty(currentValue))
                    {
                        Console.WriteLine($"Warning: Input still contains text after adding {email}: {currentValue}");
                        await inputBox.FillAsync(""); // Clear it manually if needed
                        await Task.Delay(500);
                    }
                    
                    Console.WriteLine($"Successfully added [{email}]");
                }
            }

        
            // Try to find and click the 'Share' button in the dialog actions
            // First, let's enumerate all buttons in the dialog to see what's available
            var allButtons = page.Locator("button");
            var buttonCount = await allButtons.CountAsync();
            Console.WriteLine($"Found {buttonCount} buttons in the dialog:");
            
            for (int i = 0; i < Math.Min(buttonCount, 20); i++) // Limit to first 20 buttons
            {
                var button = allButtons.Nth(i);
                try
                {
                    var buttonText = await button.InnerTextAsync();
                    var isVisible = await button.IsVisibleAsync();
                    var className = await button.GetAttributeAsync("class");
                    var role = await button.GetAttributeAsync("role");
                    var type = await button.GetAttributeAsync("type");
                    Console.WriteLine($"Button {i}: Text='{buttonText}', Visible={isVisible}, Type={type}, Role={role}");
                    if (!string.IsNullOrEmpty(className) && className.Length > 100)
                    {
                        Console.WriteLine($"  Classes: {className.Substring(0, 100)}...");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Button {i}: Could not read properties - {ex.Message}");
                }
            }
            
            // Use multiple selector strategies to find the Share button
            var shareButtonSelectors = new[]
            {
                "button.fui-Button:has-text('Share')",
                "button[type='button']:has-text('Share')",
                "div.fui-DialogActions button:has-text('Share')",
                "button.r1alrhcs:has-text('Share')", // Using one of the main CSS classes from the HTML
                "button[role='button']:has-text('Share')"
            };
            
            bool shareButtonClicked = false;
            
            foreach (var selector in shareButtonSelectors)
            {
                var shareButton = page.Locator(selector);
                Console.WriteLine($"Trying selector: {selector}");
                
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        await shareButton.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2000 });
                        
                        if (await shareButton.IsVisibleAsync())
                        {
                            // Scroll the button into view first
                            await shareButton.ScrollIntoViewIfNeededAsync();
                            await Task.Delay(500);
                            
                            // Try clicking with different methods
                            try
                            {
                                await shareButton.ClickAsync(new() { Force = true });
                                Console.WriteLine($"Clicked 'Share' button using selector '{selector}' (attempt {attempt})");
                                shareButtonClicked = true;
                                break;
                            }
                            catch
                            {
                                // If regular click fails, try using JavaScript click
                                await shareButton.EvaluateAsync("element => element.click()");
                                Console.WriteLine($"Clicked 'Share' button using JavaScript with selector '{selector}' (attempt {attempt})");
                                shareButtonClicked = true;
                                break;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"Attempt {attempt}: 'Share' button not visible with selector '{selector}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Attempt {attempt} with selector '{selector}': {ex.Message}");
                        await Task.Delay(1000);
                    }
                }
                
                if (shareButtonClicked) break;
            }
            if (!shareButtonClicked)
            {
                Console.WriteLine("Failed to click 'Share' button after multiple attempts.");
            }

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