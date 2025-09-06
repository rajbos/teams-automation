using System;
using System.Collections.Generic;
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
            // Check for test mode
            if (args.Length > 0 && args[0] == "--test")
            {
                await RunBatchingTest();
                return;
            }
            
            try
            {
                Env.Load();
                
                string profilePath = await SelectBrowserProfileAsync();
                
                using var playwright = await Playwright.CreateAsync();
                var browserContext = await LaunchBrowserAsync(playwright, profilePath);
                if (browserContext == null) return;

                var page = await NavigateToStartUrlAsync(browserContext);
                if (page == null)
                {
                    await browserContext.CloseAsync();
                    return;
                }

                await WaitForUserLoginAsync();

                var (channelName, emailsFileLocation) = LoadConfiguration();
                if (channelName == null || emailsFileLocation == null)
                {
                    await browserContext.CloseAsync();
                    return;
                }

                var channelLocator = await FindChannelAsync(page, channelName);
                if (channelLocator == null)
                {
                    await browserContext.CloseAsync();
                    return;
                }

                if (!await OpenShareDialogAsync(page, channelLocator))
                {
                    await browserContext.CloseAsync();
                    return;
                }

                if (!await AddEmailsToShareAsync(page, emailsFileLocation))
                {
                    await browserContext.CloseAsync();
                    return;
                }

                Console.WriteLine("\n🎯 All batches completed. Clicking final Share button...");
                await ClickShareButtonAsync(page);
                // extra wait to see what happens on screen, seems like the writeline just continues
                await Task.Delay(5000);

                Console.WriteLine("Press any key to close the browser...");
                Console.ReadKey();
                await browserContext.CloseAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
            }
        }

        static async Task<string> SelectBrowserProfileAsync()
        {
            await Task.CompletedTask;
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
            
            return profilePath;
        }

        static async Task<IBrowserContext?> LaunchBrowserAsync(IPlaywright playwright, string profilePath)
        {
            try
            {
                var browserContext = await playwright.Chromium.LaunchPersistentContextAsync(profilePath, new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = false,
                    Channel = "msedge",
                });
                return browserContext;
            }
            catch (PlaywrightException ex)
            {
                Console.WriteLine($"Playwright browser launch error: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"General error during browser launch: {ex.Message}");
                return null;
            }
        }

        static async Task<IPage?> NavigateToStartUrlAsync(IBrowserContext browserContext)
        {
            try
            {
                var page = browserContext.Pages.FirstOrDefault() ?? await browserContext.NewPageAsync();
                var startUrl = Environment.GetEnvironmentVariable("START_URL") ?? "https://example.com";
                await page.GotoAsync(startUrl);
                Console.WriteLine($"Navigated to {startUrl}");
                return page;
            }
            catch (PlaywrightException ex)
            {
                Console.WriteLine($"Playwright navigation error: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"General error during navigation: {ex.Message}");
                return null;
            }
        }

        static Task WaitForUserLoginAsync()
        {
            Console.WriteLine("Please log in to the application in the browser if needed.");
            Console.WriteLine("Press any key in this window to continue once you are logged in...");
            Console.Read();
            return Task.CompletedTask;
        }

        static (string? channelName, string? emailsFileLocation) LoadConfiguration()
        {
            var channelName = Environment.GetEnvironmentVariable("ChannelName");
            if (string.IsNullOrEmpty(channelName))
            {
                Console.WriteLine("ChannelName not found in .env file. Please add it.");
                return (null, null);
            }

            var emailsFileLocation = Environment.GetEnvironmentVariable("EmailsFileLocation");
            if (string.IsNullOrEmpty(emailsFileLocation) || !File.Exists(emailsFileLocation))
            {
                Console.WriteLine("EmailsFileLocation not found or file does not exist. Please check your .env file.");
                return (null, null);
            }

            return (channelName, emailsFileLocation);
        }

        static async Task<ILocator?> FindChannelAsync(IPage page, string channelName)
        {
            Console.WriteLine($"Looking for channel: {channelName}");
            ILocator? channelLocator = null;
            bool channelFound = false;
            int scrollAttempts = 0;

            while (!channelFound && scrollAttempts < 10)
            {
                var allChannels = page.Locator("div[role='treeitem']");
                var channelCount = await allChannels.CountAsync();

                Console.WriteLine($"Found {channelCount} channels in the sidebar");

                for (int i = 0; i < channelCount; i++)
                {
                    var channel = allChannels.Nth(i);
                    var channelText = await channel.InnerTextAsync();

                    if (channelText.Trim().Equals(channelName, StringComparison.OrdinalIgnoreCase))
                    {
                        channelLocator = channel;
                        channelFound = true;
                        Console.WriteLine($"Channel '{channelName}' found at position {i}. Text: '{channelText.Trim()}'");

                        await channelLocator.ScrollIntoViewIfNeededAsync();
                        await Task.Delay(500);

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
                    var sidebar = page.Locator("div[role='tree']").First;
                    await sidebar.EvaluateAsync("el => el.scrollBy(0, 300)");
                    await Task.Delay(1000);
                }
            }

            if (!channelFound || channelLocator == null)
            {
                Console.WriteLine($"Channel '{channelName}' could not be found after scrolling.");
                return null;
            }

            return channelLocator;
        }

        static async Task<bool> OpenShareDialogAsync(IPage page, ILocator channelLocator)
        {
            await Task.Delay(500);
            var moreOptionsButton = channelLocator.Locator("button[aria-label*='More options'], button[title*='More options']").First;
            try
            {
                await moreOptionsButton.ClickAsync(new() { Force = true });
                Console.WriteLine("More options button clicked");
            }
            catch (PlaywrightException ex)
            {
                Console.WriteLine($"Could not click More options: {ex.Message}");
                return false;
            }

            await LogMenuItemsAsync(page);

            if (!await HoverShareChannelAsync(page))
            {
                return false;
            }

            return await ClickWithPeopleAsync(page);
        }

        static async Task LogMenuItemsAsync(IPage page)
        {
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
        }

        static async Task<bool> HoverShareChannelAsync(IPage page)
        {
            var shareChannelMenuItemLocator = page.Locator("div[data-testid='channel-share-with-options-menu-item']");
            bool shareChannelClicked = false;
            
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    await shareChannelMenuItemLocator.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3000 });
                    if (await shareChannelMenuItemLocator.IsVisibleAsync())
                    {
                        await shareChannelMenuItemLocator.HoverAsync();
                        await Task.Delay(500);
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
            }

            return shareChannelClicked;
        }

        static async Task<bool> ClickWithPeopleAsync(IPage page)
        {
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
            }

            return withPeopleClicked;
        }

        static async Task<bool> AddEmailsToShareAsync(IPage page, string emailsFileLocation)
        {
            var inputBox = await FindEmailInputBoxAsync(page);
            if (inputBox == null)
            {
                Console.WriteLine("Could not find the email input box.");
                return false;
            }

            var emails = await ReadEmailsFromFileAsync(emailsFileLocation);
            var total = emails.Count;
            
            // Process emails in batches of 25
            const int batchSize = 25;
            var batches = new List<List<string>>();
            
            for (int i = 0; i < emails.Count; i += batchSize)
            {
                var batch = emails.Skip(i).Take(batchSize).ToList();
                batches.Add(batch);
            }
            
            Console.WriteLine($"Processing {total} emails in {batches.Count} batches of up to {batchSize} emails each.");
            
            for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
            {
                var currentBatch = batches[batchIndex];
                Console.WriteLine($"\n--- Processing Batch {batchIndex + 1}/{batches.Count} ({currentBatch.Count} emails) ---");
                
                // Add all emails in the current batch
                for (int emailIndex = 0; emailIndex < currentBatch.Count; emailIndex++)
                {
                    var email = currentBatch[emailIndex];
                    if (!string.IsNullOrWhiteSpace(email))
                    {
                        var globalIndex = batchIndex * batchSize + emailIndex + 1;
                        Console.WriteLine($"Adding {globalIndex}/{total}: {email}");
                        await AddSingleEmailAsync(inputBox, email, page);
                    }
                }
                
                // After adding all emails in the batch, click Add button and wait for completion
                if (!await ProcessBatchCompletionAsync(page, batchIndex + 1, batches.Count))
                {
                    Console.WriteLine($"❌ Failed to complete batch {batchIndex + 1}. Stopping processing.");
                    return false;
                }
                
                Console.WriteLine($"✅ Batch {batchIndex + 1}/{batches.Count} completed successfully.");
            }
            
            Console.WriteLine($"\n🎉 All {batches.Count} batches processed successfully!");
            return true;
        }

        static async Task<ILocator?> FindEmailInputBoxAsync(IPage page)
        {
            var shareInputSelector = "input#people-picker-input";
            var fallbackSelector = "input[aria-label='Type a name or email']";
            
            try
            {
                await page.WaitForSelectorAsync(shareInputSelector, new() { Timeout = 5000 });
                Console.WriteLine("Found input using #people-picker-input");
                return page.Locator(shareInputSelector);
            }
            catch
            {
                try
                {
                    await page.WaitForSelectorAsync(fallbackSelector, new() { Timeout = 5000 });
                    Console.WriteLine("Found input using aria-label selector");
                    return page.Locator(fallbackSelector);
                }
                catch
                {
                    return null;
                }
            }
        }

        static async Task<List<string>> ReadEmailsFromFileAsync(string emailsFileLocation)
        {
            var emails = (await File.ReadAllLinesAsync(emailsFileLocation))
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct()
                .ToList();
            
            return emails;
        }

        static async Task AddSingleEmailAsync(ILocator inputBox, string email, IPage page)
        {
            Console.WriteLine($"📧 Adding [{email}]");
            
            try
            {
                await inputBox.ClickAsync();
                await inputBox.FillAsync("");
                
                await inputBox.TypeAsync(email, new() { Delay = 30 });
                
                // Wait for suggestions to appear 
                await Task.Delay(800);
                
                // Try to detect if suggestions appeared by checking for dropdown/suggestions
                var suggestionExists = await CheckForSuggestionsAsync(page);
                if (!suggestionExists)
                {
                    // If no suggestions appeared quickly, wait a bit more
                    await Task.Delay(400);
                }
                
                await page.Keyboard.PressAsync("Enter");
                
                // Wait for email processing - adaptive delay based on input clearing
                bool emailProcessed = false;
                int maxWaitAttempts = 10;
                
                for (int i = 0; i < maxWaitAttempts; i++)
                {
                    await Task.Delay(200);
                    var currentValue = await inputBox.InputValueAsync();
                    
                    if (string.IsNullOrEmpty(currentValue))
                    {
                        emailProcessed = true;
                        Console.WriteLine($"✅ Email [{email}] processed in {(i + 1) * 200}ms");
                        break;
                    }
                }
                
                if (!emailProcessed)
                {
                    // Fallback: check one more time after original delay
                    await Task.Delay(1000);
                    var currentValue = await inputBox.InputValueAsync();
                    
                    if (!string.IsNullOrEmpty(currentValue))
                    {
                        Console.WriteLine($"⚠️ Warning: Input still contains text after adding {email}: {currentValue}");
                        await inputBox.FillAsync("");
                        await Task.Delay(200);
                    }
                    else
                    {
                        Console.WriteLine($"✅ Email [{email}] processed (slower response)");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error adding email [{email}]: {ex.Message}");
            }
        }
        
        static async Task<bool> CheckForSuggestionsAsync(IPage page)
        {
            try
            {
                var suggestionSelectors = new[]
                {
                    "[role='listbox']",
                    "[role='option']", 
                    ".ms-Suggestions",
                    ".ms-BasePicker-suggestion",
                    "[data-testid*='suggestion']",
                    ".fui-Listbox"
                };
                
                foreach (var selector in suggestionSelectors)
                {
                    var suggestions = page.Locator(selector);
                    var count = await suggestions.CountAsync();
                    if (count > 0)
                    {
                        Console.WriteLine($"🔍 Found {count} suggestions");
                        return true;
                    }
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        static async Task<bool> ProcessBatchCompletionAsync(IPage page, int currentBatch, int totalBatches)
        {
            Console.WriteLine($"🔄 Processing completion for batch {currentBatch}/{totalBatches}...");
            
            // For all batches except the last one, click the "Add" button
            if (currentBatch < totalBatches)
            {
                if (!await ClickAddButtonAsync(page))
                {
                    Console.WriteLine("❌ Failed to click Add button.");
                    return false;
                }
                
                // Wait for completion message
                if (!await WaitForCompletionMessageAsync(page))
                {
                    Console.WriteLine("❌ Completion message did not appear after clicking Add button.");
                    return false;
                }
            }
            
            return true;
        }

        static async Task<bool> ClickAddButtonAsync(IPage page)
        {
            Console.WriteLine("🔍 Looking for Add button...");
            
            // Look for different possible Add button selectors
            var addButtonSelectors = new[]
            {
                "button:has-text('Add')",
                "button[aria-label*='Add']",
                "button[title*='Add']",
                "div.fui-DialogActions button:has-text('Add')",
                ".ms-Dialog-actions button:has-text('Add')"
            };
            
            foreach (var selector in addButtonSelectors)
            {
                try
                {
                    var addButton = page.Locator(selector);
                    var count = await addButton.CountAsync();
                    
                    if (count > 0)
                    {
                        Console.WriteLine($"🎯 Found {count} Add button(s) using selector: {selector}");
                        
                        // Use the first visible Add button
                        for (int i = 0; i < count; i++)
                        {
                            var button = addButton.Nth(i);
                            if (await button.IsVisibleAsync())
                            {
                                await button.ScrollIntoViewIfNeededAsync();
                                await Task.Delay(500);
                                
                                try
                                {
                                    await button.ClickAsync(new() { Force = true });
                                    Console.WriteLine("✅ Successfully clicked Add button");
                                    return true;
                                }
                                catch (Exception clickEx)
                                {
                                    Console.WriteLine($"Regular click failed: {clickEx.Message}, trying JavaScript click");
                                    await button.EvaluateAsync("element => element.click()");
                                    Console.WriteLine("✅ Successfully clicked Add button using JavaScript");
                                    return true;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Selector {selector} failed: {ex.Message}");
                }
            }
            
            Console.WriteLine("❌ Could not find or click Add button");
            return false;
        }

        static async Task<bool> WaitForCompletionMessageAsync(IPage page)
        {
            Console.WriteLine("⏳ Waiting for completion message...");
            
            // Look for different possible completion message indicators
            var completionSelectors = new[]
            {
                ":has-text('completed')",
                ":has-text('Complete')",
                ":has-text('Success')",
                ":has-text('Added')",
                ":has-text('Done')",
                "[role='alert']:has-text('completed')",
                ".ms-MessageBar:has-text('completed')",
                ".fui-MessageBar:has-text('completed')"
            };
            
            var maxWaitTime = 30000; // 30 seconds
            var checkInterval = 500; // 0.5 seconds
            var elapsedTime = 0;
            
            while (elapsedTime < maxWaitTime)
            {
                foreach (var selector in completionSelectors)
                {
                    try
                    {
                        var elements = page.Locator(selector);
                        var count = await elements.CountAsync();
                        
                        if (count > 0)
                        {
                            // Check if any of the elements are visible
                            for (int i = 0; i < count; i++)
                            {
                                var element = elements.Nth(i);
                                if (await element.IsVisibleAsync())
                                {
                                    var text = await element.InnerTextAsync();
                                    Console.WriteLine($"✅ Found completion message: '{text.Trim()}'");
                                    
                                    // Wait a bit more for the message to be processed
                                    await Task.Delay(2000);
                                    return true;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Continue with next selector if this one fails
                        Console.WriteLine($"Completion check failed for selector {selector}: {ex.Message}");
                    }
                }
                
                await Task.Delay(checkInterval);
                elapsedTime += checkInterval;
                
                if (elapsedTime % 5000 == 0) // Log every 5 seconds
                {
                    Console.WriteLine($"Still waiting for completion message... ({elapsedTime/1000}s elapsed)");
                }
            }
            
            Console.WriteLine($"❌ Timeout waiting for completion message after {maxWaitTime/1000} seconds");
            return false;
        }

        static async Task<ILocator?> FindUniqueShareButtonAsync(IPage page)
        {
            Console.WriteLine("🎯 Finding the correct Share button...");
            
            try
            {
                var dialogShareButton = page.Locator("div.fui-DialogActions button:has-text('Share')");
                var dialogCount = await dialogShareButton.CountAsync();
                
                if (dialogCount == 1)
                {
                    Console.WriteLine("✅ Found unique Share button in dialog actions");
                    return dialogShareButton;
                }
                else if (dialogCount > 1)
                {
                    Console.WriteLine($"⚠️ Found {dialogCount} Share buttons in dialog actions, using last one");
                    return dialogShareButton.Last;
                }
                                                
                Console.WriteLine("❌ Could not find a unique Share button");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error finding Share button: {ex.Message}");
                return null;
            }
        }

        static async Task ClickShareButtonAsync(IPage page)
        {
            var shareButton = await FindUniqueShareButtonAsync(page);
            if (shareButton != null)
            {
                bool clicked = await TryClickButtonAsync(shareButton, "Smart-detected Share button");
                if (clicked)
                {
                    return;
                }
            }
            Console.WriteLine("❌ Failed to click 'Share' button using smart-detection.");
        }
        
        static async Task<bool> TryClickButtonAsync(ILocator button, string description)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await button.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 2000 });
                    
                    if (await button.IsVisibleAsync())
                    {
                        await button.ScrollIntoViewIfNeededAsync();
                        await Task.Delay(500);
                        
                        try
                        {
                            await button.ClickAsync(new() { Force = true });
                            Console.WriteLine($"✅ Successfully clicked Share button using {description} (attempt {attempt})");
                            return true;
                        }
                        catch (Exception clickEx)
                        {
                            Console.WriteLine($"Regular click failed: {clickEx.Message}");
                            await button.EvaluateAsync("element => element.click()");
                            Console.WriteLine($"✅ Successfully clicked Share button using JavaScript with {description} (attempt {attempt})");
                            return true;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Attempt {attempt}: Share button not visible with {description}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Attempt {attempt} with {description}: {ex.Message}");
                    if (attempt < 3)
                    {
                        await Task.Delay(1000);
                    }
                }
            }
            
            return false;
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
                return Path.GetFileName(profilePath);
            }

            return Path.GetFileName(profilePath);
        }

        // Test method to verify batching logic
        static async Task RunBatchingTest()
        {
            Console.WriteLine("🧪 Running batching logic test...");
            
            // Test with emails-test.csv (52 emails)
            var testFile = "emails-test.csv";
            if (!File.Exists(testFile))
            {
                Console.WriteLine($"❌ Test file {testFile} not found");
                return;
            }

            var emails = await ReadEmailsFromFileAsync(testFile);
            Console.WriteLine($"📧 Loaded {emails.Count} emails from {testFile}");

            // Simulate batching logic
            const int batchSize = 25;
            var batches = new List<List<string>>();
            
            for (int i = 0; i < emails.Count; i += batchSize)
            {
                var batch = emails.Skip(i).Take(batchSize).ToList();
                batches.Add(batch);
            }
            
            Console.WriteLine($"📦 Created {batches.Count} batches:");
            for (int i = 0; i < batches.Count; i++)
            {
                Console.WriteLine($"  Batch {i + 1}: {batches[i].Count} emails");
            }

            // Verify expected behavior
            var expectedBatches = (int)Math.Ceiling((double)emails.Count / batchSize);
            if (batches.Count == expectedBatches)
            {
                Console.WriteLine($"✅ Batching test passed! {batches.Count} batches created as expected");
                
                // Check that first batches have 25 emails and last batch has remainder
                for (int i = 0; i < batches.Count - 1; i++)
                {
                    if (batches[i].Count != batchSize)
                    {
                        Console.WriteLine($"❌ Batch {i + 1} has {batches[i].Count} emails, expected {batchSize}");
                        return;
                    }
                }
                
                var expectedLastBatchSize = emails.Count % batchSize == 0 ? batchSize : emails.Count % batchSize;
                if (batches.Last().Count == expectedLastBatchSize)
                {
                    Console.WriteLine($"✅ Last batch size correct: {batches.Last().Count} emails");
                    Console.WriteLine("🎉 All batching tests passed!");
                }
                else
                {
                    Console.WriteLine($"❌ Last batch has {batches.Last().Count} emails, expected {expectedLastBatchSize}");
                }
            }
            else
            {
                Console.WriteLine($"❌ Batching test failed! Expected {expectedBatches} batches, got {batches.Count}");
            }
        }
    }
}