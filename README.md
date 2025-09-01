# Teams Automation

A .NET Core application for automating Microsoft Teams operations using Playwright.

## Overview

This application automates common Microsoft Teams actions through browser automation with Playwright. It's designed to help administrators and power users save time on repetitive tasks in Microsoft Teams. On some environments you have no access to the direct API's to be able to automate these tasks, but we can still automate the browser ofc :smile: !

The main feature we are starting on is adding users to a specific channel.

## Prerequisites

- .NET 9.0 SDK or later
- Microsoft Playwright
- Microsoft Teams account with appropriate permissions

## Setup

1. Clone the repository:
   ```
   git clone https://github.com/rajbos/teams-automation.git
   cd teams-automation
   ```

2. Install dependencies:
   ```
   dotnet restore
   ```

3. Install Playwright browsers:
   ```
   pwsh bin/Debug/net9.0/playwright.ps1 install
   ```

4. Create environment configuration:
   - Copy `.env-example` to `.env`
   - Update the values in the `.env` file with your Teams information

## Usage

Run the application with:

```
dotnet run
```

### Input Data

The application uses `emails.csv` to process data. This currently only supports email addresses.

## Features

- Automated Teams login (with manual login flow)
- Batch operations based on CSV input
- Configurable through environment variables

The default is to add share the channel you configured to all the users in the provided CSV file into the channel.

## Configuration

Configure the application by setting these environment variables in your `.env` file:
- START_URL="https://teams.microsoft.com.mcas.ms/v2/"
- ChannelName="GitHub Copilot"
- EmailsFileLocation="emails.csv"

## Development

### Build

```
dotnet build
```

### Run with watch (for development)

```
dotnet watch run
```

### Publish

```
dotnet publish
```

## License

[MIT](LICENSE)

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request
