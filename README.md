# RalphDemo - Copilot SDK Pattern

This repository demonstrates the "Ralph Wiggum Pattern" using the GitHub Copilot SDK for .NET. The pattern involves an autonomous agent that iteratively attempts to fix failing tests in a target application.

## Project Structure

- **RalphAgent**: A console application that acts as the AI agent. It uses the Copilot SDK to interact with the model and tools to run tests and modify code.
- **TargetApp**: A simple application (Calculator) intentionally containing bugs or incomplete implementation.
- **TargetApp.Tests**: Unit tests for the TargetApp. The agent's goal is to make these tests pass.

## Prerequisites

- .NET 10.0 SDK
- GitHub Copilot CLI installed and authenticated
- Access to GitHub Copilot

## Getting Started

1.  **Navigate to the Agent directory:**
    ```bash
    cd RalphDemo/RalphAgent
    ```

2.  **Run the Agent:**
    ```bash
    dotnet run
    ```

    The agent will:
    -   Start a session with the configured model (e.g., `claude-sonnet-4.5`).
    -   Run the tests in `TargetApp.Tests`.
    -   Analyze failures and attempt to fix `TargetApp`.
    -   Repeat until all tests pass or the maximum iteration count is reached.

## Configuration

The agent is configured in `RalphDemo/RalphAgent/Program.cs`. You can modify:
-   `MaxIterations`: Maximum number of repair attempts.
-   `SessionConfig`: Model selection and system message.

## references
- [Copilot SDK Learning Path](copilot_sdk_learning_path.md)
- [Ralph Pattern Description](ralph_pattern_csharp.md)
