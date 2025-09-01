# Teams Automation

This project is a .NET Core application that automates tasks in the Microsoft Edge browser using Playwright. It allows users to specify a browser profile, navigate to a URL, and interact with DOM elements.

## Project Structure

- `teams-automation.csproj`: Project file containing dependencies and build settings.
- `Program.cs`: Entry point of the application that handles browser automation.
- `README.md`: Documentation for the project.

## Getting Started

### Prerequisites

- .NET Core SDK (version 3.1 or later)
- Microsoft Edge browser
- Playwright library

### Installation

1. Clone the repository:
   ```
   git clone https://github.com/yourusername/teams-automation.git
   cd teams-automation
   ```

2. Restore the project dependencies:
   ```
   dotnet restore
   ```

3. Install Playwright:
   ```
   dotnet add package Microsoft.Playwright
   ```

4. Install the necessary browsers:
   ```
   playwright install
   ```

### Usage

1. Run the application:
   ```
   dotnet run
   ```

2. When prompted, enter the name of the browser profile you wish to use.

3. The application will launch the Edge browser with the specified profile, navigate to the desired URL, and allow you to interact with the DOM.

### Contributing

Contributions are welcome! Please open an issue or submit a pull request for any enhancements or bug fixes.

### License

This project is licensed under the MIT License. See the LICENSE file for details.